//! Internal failure policy, independently testable without a GPU or native window.
use std::{
    sync::{mpsc, Arc, OnceLock},
    time::{Duration, Instant},
};

#[derive(Clone, Default)]
pub(crate) struct DeviceFailure(Arc<OnceLock<String>>);
impl DeviceFailure {
    pub fn observe(&self, reason: wgpu::DeviceLostReason, message: String) {
        // Normal explicit destruction is not a driver failure. First failure wins;
        // callbacks may run concurrently and must never borrow Renderer or unwind.
        if reason != wgpu::DeviceLostReason::Destroyed {
            let _ = self.0.set(format!(
                "device lost: {message}; stop and recreate the renderer"
            ));
        }
    }
    pub fn check(&self) -> Result<(), String> {
        match self.0.get() {
            Some(error) => Err(error.clone()),
            None => Ok(()),
        }
    }
}

pub(crate) fn acquire<T>(
    mut get: impl FnMut() -> Result<T, wgpu::SurfaceError>,
    mut configure: impl FnMut(),
) -> Result<Option<T>, String> {
    let result = match get() {
        Err(wgpu::SurfaceError::Lost | wgpu::SurfaceError::Outdated) => {
            configure();
            get() // Exactly one retry, even if the second result is Lost/Outdated.
        }
        result => result,
    };
    match result {
        Ok(frame) => Ok(Some(frame)),
        Err(wgpu::SurfaceError::Timeout) => Ok(None),
        Err(error) => Err(format!("surface: {error}")),
    }
}

// Bound application-level readback waiting. PollType::Poll does not wait for GPU
// completion; a driver stuck inside a native call still needs process supervision.
pub(crate) fn wait_readback<T>(
    receiver: mpsc::Receiver<Result<T, String>>,
    timeout: Duration,
    mut poll: impl FnMut() -> Result<(), String>,
) -> Result<T, String> {
    let start = Instant::now();
    loop {
        poll()?;
        match receiver.try_recv() {
            Ok(result) => return result,
            Err(mpsc::TryRecvError::Disconnected) => {
                return Err("readback callback disconnected".into())
            }
            Err(mpsc::TryRecvError::Empty) => {}
        }
        let Some(remaining) = timeout.checked_sub(start.elapsed()) else {
            return Err("readback timed out; stop rendering and investigate the device".into());
        };
        std::thread::sleep(remaining.min(Duration::from_millis(1)));
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn surface_policy_is_bounded_and_does_not_poison_future_acquires() {
        use wgpu::SurfaceError::*;
        for first in [Lost, Outdated] {
            for second in [
                Ok(1),
                Err(Timeout),
                Err(Lost),
                Err(Outdated),
                Err(OutOfMemory),
                Err(Other),
            ] {
                let mut results = vec![Err(first.clone()), second.clone()].into_iter();
                let mut configured = 0;
                let result = acquire(
                    || results.next().expect("unbounded retry"),
                    || configured += 1,
                );
                assert_eq!(configured, 1);
                assert_eq!(
                    result.is_err(),
                    matches!(second, Err(Lost | Outdated | OutOfMemory | Other))
                );
            }
        }
        for first in [Ok(1), Err(Timeout), Err(OutOfMemory), Err(Other)] {
            let mut calls = 0;
            let result = acquire(
                || {
                    calls += 1;
                    first.clone()
                },
                || panic!("unexpected configure"),
            );
            assert_eq!(calls, 1);
            assert_eq!(result.is_err(), matches!(first, Err(OutOfMemory | Other)));
        }
        assert_eq!(acquire(|| Ok(2), || panic!()).unwrap(), Some(2));
    }
    #[test]
    fn first_device_failure_is_latched_across_threads_but_destroy_is_ignored() {
        let state = DeviceFailure::default();
        state.observe(wgpu::DeviceLostReason::Destroyed, "normal shutdown".into());
        assert!(state.check().is_ok());
        let callback = state.clone();
        std::thread::spawn(move || {
            callback.observe(wgpu::DeviceLostReason::Unknown, "injected failure".into())
        })
        .join()
        .unwrap();
        let first = state.check().unwrap_err();
        state.observe(wgpu::DeviceLostReason::Unknown, "later failure".into());
        assert_eq!(state.check().unwrap_err(), first);
        assert!(DeviceFailure::default().check().is_ok());
    }
    #[test]
    fn readback_success_failure_disconnect_and_deadline_are_bounded() {
        let (tx, rx) = mpsc::channel();
        tx.send(Ok(42)).unwrap();
        assert_eq!(wait_readback(rx, Duration::ZERO, || Ok(())).unwrap(), 42);
        let (tx, rx) = mpsc::channel::<Result<(), String>>();
        tx.send(Err("map failed".into())).unwrap();
        assert_eq!(
            wait_readback(rx, Duration::ZERO, || Ok(())).unwrap_err(),
            "map failed"
        );
        let (_tx, rx) = mpsc::channel::<Result<(), String>>();
        assert!(wait_readback(rx, Duration::ZERO, || Ok(()))
            .unwrap_err()
            .contains("timed out"));
        let (tx, rx) = mpsc::channel::<Result<(), String>>();
        drop(tx);
        assert!(wait_readback(rx, Duration::ZERO, || Ok(()))
            .unwrap_err()
            .contains("disconnected"));
        let (_tx, rx) = mpsc::channel::<Result<(), String>>();
        assert_eq!(
            wait_readback(rx, Duration::from_secs(10), || Err("injected loss".into())).unwrap_err(),
            "injected loss"
        );
    }
}
