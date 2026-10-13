//! Injected acquisition errors, real GPU encoding/submission/readback. These tests
//! do not create a native surface or establish OS/compositor presentation evidence.
use super::*;
use std::{
    collections::VecDeque,
    sync::{
        atomic::{AtomicUsize, Ordering},
        Arc, Mutex,
    },
};

pub(super) struct InjectedFrame {
    pub texture: wgpu::Texture,
    presented: Arc<AtomicUsize>,
}
impl InjectedFrame {
    pub fn present(self) {
        self.presented.fetch_add(1, Ordering::SeqCst);
    }
}

pub(super) struct InjectedSurface {
    texture: wgpu::Texture,
    outcomes: Mutex<VecDeque<Result<(), wgpu::SurfaceError>>>,
    acquired: AtomicUsize,
    configured: AtomicUsize,
    presented: Arc<AtomicUsize>,
}
impl InjectedSurface {
    fn new(renderer: &Renderer, outcomes: Vec<Result<(), wgpu::SurfaceError>>) -> Self {
        Self {
            texture: renderer.device.create_texture(&wgpu::TextureDescriptor {
                label: Some("injected presentation destination"),
                size: wgpu::Extent3d {
                    width: renderer.width,
                    height: renderer.height,
                    depth_or_array_layers: 1,
                },
                mip_level_count: 1,
                sample_count: 1,
                dimension: wgpu::TextureDimension::D2,
                format: wgpu::TextureFormat::Rgba8UnormSrgb,
                usage: wgpu::TextureUsages::RENDER_ATTACHMENT | wgpu::TextureUsages::COPY_SRC,
                view_formats: &[],
            }),
            outcomes: Mutex::new(outcomes.into()),
            acquired: AtomicUsize::new(0),
            configured: AtomicUsize::new(0),
            presented: Arc::new(AtomicUsize::new(0)),
        }
    }
    pub fn acquire(&self) -> Result<PresentFrame, wgpu::SurfaceError> {
        self.acquired.fetch_add(1, Ordering::SeqCst);
        self.outcomes
            .lock()
            .unwrap()
            .pop_front()
            .expect("unexpected/unbounded surface acquisition")?;
        Ok(PresentFrame::Injected(InjectedFrame {
            texture: self.texture.clone(),
            presented: self.presented.clone(),
        }))
    }
    pub fn configure(&self) {
        self.configured.fetch_add(1, Ordering::SeqCst);
    }
    fn counts(&self) -> (usize, usize, usize) {
        (
            self.acquired.load(Ordering::SeqCst),
            self.configured.load(Ordering::SeqCst),
            self.presented.load(Ordering::SeqCst),
        )
    }
}

fn surface(renderer: &Renderer) -> &InjectedSurface {
    renderer.injected_surface.as_ref().unwrap()
}

// Read the presentation destination independently of the renderer's output, so a
// no-op retry, wrong instance index, or omitted queue submission fails on pixels.
fn presented_pixels(renderer: &Renderer) -> Result<Vec<u8>, String> {
    let stride = (renderer.width * 4).div_ceil(256) * 256;
    let buffer = renderer.device.create_buffer(&wgpu::BufferDescriptor {
        label: Some("injected presentation readback"),
        size: u64::from(stride) * u64::from(renderer.height),
        usage: wgpu::BufferUsages::COPY_DST | wgpu::BufferUsages::MAP_READ,
        mapped_at_creation: false,
    });
    let mut encoder = renderer.device.create_command_encoder(&Default::default());
    encoder.copy_texture_to_buffer(
        surface(renderer).texture.as_image_copy(),
        wgpu::TexelCopyBufferInfo {
            buffer: &buffer,
            layout: wgpu::TexelCopyBufferLayout {
                offset: 0,
                bytes_per_row: Some(stride),
                rows_per_image: Some(renderer.height),
            },
        },
        wgpu::Extent3d {
            width: renderer.width,
            height: renderer.height,
            depth_or_array_layers: 1,
        },
    );
    renderer.queue.submit(Some(encoder.finish()));
    let (tx, rx) = std::sync::mpsc::channel();
    buffer
        .slice(..)
        .map_async(wgpu::MapMode::Read, move |result| {
            let _ = tx.send(result.map_err(|error| error.to_string()));
        });
    wait_readback(rx, std::time::Duration::from_secs(10), || {
        renderer.check_device()
    })?;
    let mapped = buffer.slice(..).get_mapped_range();
    let pixels = mapped
        .chunks_exact(stride as usize)
        .flat_map(|row| row[..renderer.width as usize * 4].iter().copied())
        .collect();
    drop(mapped);
    buffer.unmap();
    Ok(pixels)
}

#[test]
#[ignore = "requires a GPU adapter; injected surface outcomes, no native window"]
fn timeout_retries_cached_output_and_newest_frame_supersedes_pending() -> Result<(), String> {
    use wgpu::SurfaceError::{Lost, Outdated, Timeout};
    let mut renderer = unsafe { Renderer::new(32, 24, 0)? };
    println!(
        "Injected presentation GPU adapter: {}",
        renderer.adapter_name
    );
    let rect = Rect::new(0., 0., 32., 24.);
    let red = Command::rect(rect, [1., 0., 0., 1.], 0.);
    let green = Command::rect(rect, [0., 1., 0., 1.], 0.);
    renderer.injected_surface = Some(InjectedSurface::new(
        &renderer,
        vec![Err(Timeout), Err(Timeout), Err(Timeout), Ok(())],
    ));
    assert!(!renderer.retry_presentation()?);
    assert_eq!(surface(&renderer).counts(), (0, 0, 0));
    let commands = vec![
        Command {
            kind: 4,
            color: [1.; 4],
            ..Default::default()
        },
        red,
        Command {
            kind: 5,
            ..Default::default()
        },
    ];
    renderer.render(&commands, &[])?;
    drop(commands); // Retry cannot depend on the caller retaining a command list.
    assert!(renderer.presentation_pending());
    assert_eq!(surface(&renderer).counts(), (1, 0, 0));
    assert_eq!(renderer.stats.layers, 1);
    let expected = renderer.read_pixels()?;
    assert!(expected
        .chunks_exact(4)
        .all(|pixel| pixel == [255, 0, 0, 255]));
    let stats = format!("{:?}", renderer.stats);
    let instances = bytemuck::cast_slice::<_, u8>(&renderer.instances).to_vec();
    renderer.trim_offscreen_layers(); // Presentation must use cached output, not rebuild layers.
    for count in 2..=3 {
        assert!(renderer.retry_presentation()?);
        assert!(renderer.presentation_pending());
        assert_eq!(surface(&renderer).counts(), (count, 0, 0));
        assert_eq!(format!("{:?}", renderer.stats), stats);
        assert_eq!(
            bytemuck::cast_slice::<_, u8>(&renderer.instances),
            instances
        );
        assert!(renderer.layers.is_empty());
    }
    assert!(!renderer.retry_presentation()?);
    assert!(!renderer.presentation_pending());
    assert_eq!(surface(&renderer).counts(), (4, 0, 1));
    assert_eq!(presented_pixels(&renderer)?, expected);
    assert_eq!(renderer.read_pixels()?, expected);
    assert_eq!(format!("{:?}", renderer.stats), stats);
    assert_eq!(
        bytemuck::cast_slice::<_, u8>(&renderer.instances),
        instances
    );
    assert!(renderer.layers.is_empty());
    assert!(!renderer.retry_presentation()?);
    assert_eq!(surface(&renderer).counts(), (4, 0, 1));

    // A new frame with a different present index replaces pending output, even
    // when that new frame also times out. Successful render clears it as well.
    renderer.injected_surface = Some(InjectedSurface::new(
        &renderer,
        vec![Err(Timeout), Err(Timeout), Ok(()), Err(Timeout), Ok(())],
    ));
    renderer.render(&[red], &[])?;
    assert_eq!(renderer.pending_present, Some(1));
    renderer.render(&[red, green], &[])?;
    assert_eq!(renderer.pending_present, Some(2));
    assert!(!renderer.retry_presentation()?);
    assert_eq!(surface(&renderer).counts(), (3, 0, 1));
    assert!(presented_pixels(&renderer)?
        .chunks_exact(4)
        .all(|p| p == [0, 255, 0, 255]));
    renderer.render(&[red], &[])?;
    assert!(renderer.presentation_pending());
    renderer.render(&[green], &[])?;
    assert!(!renderer.presentation_pending());
    assert!(!renderer.retry_presentation()?);
    assert_eq!(surface(&renderer).counts(), (5, 0, 2));
    assert!(presented_pixels(&renderer)?
        .chunks_exact(4)
        .all(|p| p == [0, 255, 0, 255]));

    // The renderer still uses the shared, exactly-once Lost/Outdated recovery.
    for error in [Lost, Outdated] {
        renderer.injected_surface = Some(InjectedSurface::new(
            &renderer,
            vec![Err(error), Err(Timeout), Ok(())],
        ));
        renderer.render(&[red], &[])?;
        assert!(renderer.presentation_pending());
        assert_eq!(surface(&renderer).counts(), (2, 1, 0));
        assert!(!renderer.retry_presentation()?);
        assert_eq!(surface(&renderer).counts(), (3, 1, 1));
        assert_eq!(presented_pixels(&renderer)?, expected);
    }

    renderer.injected_surface = Some(InjectedSurface::new(&renderer, vec![Err(Timeout)]));
    renderer.render(&[red], &[])?;
    assert!(renderer.presentation_pending());
    renderer.resize(16, 12)?;
    assert!(!renderer.presentation_pending());
    assert!(!renderer.retry_presentation()?); // No acquire with an obsolete output/index.
    assert_eq!(surface(&renderer).counts(), (1, 0, 0));
    Ok(())
}
