# Presentation retry

Surface Timeout is recoverable and distinct from document invalidation. A completed render can submit fresh offscreen output without presenting it. The renderer retains a pending flag and the presentation instance index until a later successful presentation. `RetryPresentation` presents that output only: it does not rebuild UI layout, paint, image uploads or composition passes. Offscreen renderers are never pending.

The additive C ABI symbols `youi_presentation_pending` and `youi_retry_presentation` write a 0/1 `uint32_t`. ABI v1 and the 48-byte `youi_frame_stats` layout remain unchanged. Old native libraries still serve old clients; new hosts require the additive symbols and report a matching-library error if absent. Existing native callers can opt in explicitly.

Both hosts share the tested presentation scheduler: a timeout schedules the next attempt at least 16 ms after completion. Native input can wake the host before then; it cannot force an early retry of unchanged pixels. New dirty UI takes priority and supersedes the cached output. The Windows wait wakes for input or the retry deadline; Linux combines the deadline with its existing bounded event wait. A minimized/suspended host does not render or retry. Resume invalidates the document, including after same-size restoration. Resize discards cached pending output and requires a fresh render.

Lost/Outdated retains its existing single immediate reconfigure/reacquire policy. Other surface/device failures remain errors. Pending presentation is not hardware device-loss recovery. Logical injected Timeout→Success tests establish scheduler behavior, not real native-window or driver acceptance; those need the target OS and surface.

## Native renderer GPU regression

`renderer::presentation_tests::timeout_retries_cached_output_and_newest_frame_supersedes_pending`
injects queued `wgpu::SurfaceError` acquisition outcomes into a private test-only
surface target. Successful acquisition supplies a real GPU texture to the same
presentation encoding and queue-submission path used by native surfaces. Pixel
readback checks the destination, independently of the cached offscreen output.
The regression covers pending state after Timeout, exactly one acquisition per
Timeout retry, eventual success and idle no-op, unchanged frame statistics and
instances, presentation after scratch layers have been trimmed, replacement by
a newer frame with a different presentation index, Lost/Outdated's single
reconfigure/reacquire, and resize discarding stale pending output.

Run explicitly with a working GPU backend:

```sh
YOUI_BACKEND=gl cargo test -p youi-render --lib renderer::presentation_tests:: --locked -- --ignored --nocapture
```

`scripts/verify.ps1 -Layer Gpu` includes this test. Its successful GL/llvmpipe run
establishes renderer GPU-path behavior with injected acquisition results; it does
not establish real surface timeouts, native-window presentation, compositor
behavior, or physical-device acceptance.
