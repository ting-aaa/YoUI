# Presentation retry

Surface Timeout is recoverable and distinct from document invalidation. A completed render can submit fresh offscreen output without presenting it. The renderer retains a pending flag and the presentation instance index until a later successful presentation. `RetryPresentation` presents that output only: it does not rebuild UI layout, paint, image uploads or composition passes. Offscreen renderers are never pending.

The additive C ABI symbols `youi_presentation_pending` and `youi_retry_presentation` write a 0/1 `uint32_t`. ABI v1 and the 48-byte `youi_frame_stats` layout remain unchanged. Old native libraries still serve old clients; new hosts require the additive symbols and report a matching-library error if absent. Existing native callers can opt in explicitly.

Both hosts share the tested presentation scheduler: a timeout schedules the next attempt at least 16 ms after completion. Native input can wake the host before then; it cannot force an early retry of unchanged pixels. New dirty UI takes priority and supersedes the cached output. The Windows wait wakes for input or the retry deadline; Linux combines the deadline with its existing bounded event wait. A minimized/suspended host does not render or retry. Resume invalidates the document, including after same-size restoration. Resize discards cached pending output and requires a fresh render.

Lost/Outdated retains its existing single immediate reconfigure/reacquire policy. Other surface/device failures remain errors. Pending presentation is not hardware device-loss recovery. Logical injected Timeout→Success tests establish scheduler behavior, not real native-window or driver acceptance; those need the target OS and surface.
