# Alpha rendering failure and explicit rebuild contract

YoUI does not transparently recover a device or replay application assets. The host
owns the retry decision, original image bytes, window lifetime and render loop.
This increment defines a bounded stop/rebuild path; it is not hardware device-loss
acceptance and does not advance the project to Beta.

## Error policy

- Invalid commands, stale/foreign image IDs, atlas exhaustion and resource-admission
  failures return an operation error. Fix the cause before submitting again. They do
  not submit the rejected frame: the last submitted output and successful frame
  statistics remain available on a healthy device. Preparation may still warm CPU
  caches, upload glyphs or grow admitted scratch allocations; failure is not a full
  resource transaction. Purge/trim do not repair a lost device.
- Surface `Lost` or `Outdated`: reconfigure once and acquire once more. A second
  failure returns to the host, with no hidden loop. Surface `Timeout`, including
  after that retry, skips presentation but still submits the offscreen frame.
  A successful `Render` therefore does not promise that a window presented a frame.
  Surface OOM/other failures return immediately and are not automatically retried.
- An observed wgpu device-loss callback latches its first diagnostic. Normal explicit
  device destruction is ignored. `Render`, `Resize`, image upload and readback check
  the latch before GPU work and poll without waiting. Once latched they keep failing;
  cleanup, CPU text measurement/layout and resource accounting remain available.
  Loss is asynchronous: a successful submission is not a completion guarantee, and
  output preservation or readback after actual device loss is not promised.
- Readback polls for at most 10 seconds of application-level waiting and handles a
  missing/disconnected/error callback. A native driver call that itself hangs cannot
  be bounded here; use an external process watchdog for that case. The timeout is
  a reported error, not an automatic renderer replacement.
- C ABI layouts and codes are unchanged: `0` success, `-1` operation error, `-2`
  contained panic. C# now throws `NativeRendererException : InvalidOperationException`
  with `NativeStatus` and the original diagnostic. Existing catches keep working.
  `-1` is intentionally broad; do not parse backend text to infer safe automatic
  retries. A panic can poison the global native registry: after `-2`, stop the
  process and restart rather than attempting in-process recovery. Other unexpected
  failures should also leave the frame loop and be reported for a host decision.

The bundled desktop hosts already propagate render exceptions out of their loops;
Windows callback exceptions stop the loop and are rethrown by `PumpEvents`. They do
not replace their owned renderer in place. This contract supplies the lower-level
building blocks for a custom host; it is not a new DesktopWindow recovery API.

## Explicit custom-host rebuild

Run this sequence on the creating UI/render thread, between updates, after the
original failure has unwound. Bound any reconstruction attempts outside the loop.

1. Stop scheduling frames. Record the exception/status before any cleanup changes
   the thread-local native diagnostic. For panic/registry poisoning, restart the
   process instead of following the remaining in-process steps.
2. Keep the host's native window/display alive. Dispose the old renderer before
   destroying/replacing a borrowed HWND, Xlib window or display. `Dispose` does not
   wait for device recovery. Do not swallow a failing disposal and resume rendering.
3. Construct a new renderer with the desired dimensions, original resource budget
   and valid host handles (or offscreen). Construction may fail; stay stopped and
   report that failure. There is no unbounded recreate loop.
4. Upload original CPU image bytes again. Replace every control/application asset
   reference, including custom paint closures and Rust scene image IDs. New native
   image IDs are process-global and never reused; cached old commands are rejected
   even if a fresh renderer uploads an identical image. Exhaustion is an error,
   never wrapping reuse. No ownership transfer or implicit image replay occurs.
5. Call `document.RebindTextMeasurer(newProvider)` on each affected document. This
   replaces only the borrowed provider, clears the document's cached command arena
   and requests full layout, paint and semantics, even for the same provider object.
   It rejects null, a foreign thread and calls during layout/paint/semantic updates
   or input dispatch. TextBox normalizes stored selection/caret to the current
   provider before paint and editing/copy/cut, including input before the first
   rebuilt frame and same-instance provider changes. If the initial normalization
   query fails, stored selection and text/history are unchanged. After successful
   normalization, a later paint/edit layout query may fail with the corrected
   selection retained; candidate-text layout failure does not change text or
   undo/redo history. Undo/redo use current-provider stops.
   Neither provider is disposed by the document. For a DPI adapter, create/rebind
   the correctly scaled provider, not an unscaled renderer. Custom controls with
   their own provider/layout/image caches must refresh those caches explicitly.
6. Rebuild every separate DrawList/scene cache and submit a fresh frame. A copied
   DrawList is not automatically rewritten. TextLayout snapshots are CPU values and
   may survive disposal, but obtain new ones when provider/font/scale changes.
   Resume scheduling only after successful reconstruction and submission.

`NativeRenderer.IsDisposed` describes disposal, not device health. `NativeImage.IsDisposed`
is true after either image disposal or owner disposal. Drawing helpers reject such
images before appending commands. Disposing an image after its owner is safe and
idempotent; it never accesses the destroyed native renderer. Already recorded image
commands remain plain IDs and are checked by native submission.

## Verification

- Logic: deterministic surface acquisition policy, concurrent first-loss latch,
  normal destruction, callback disconnect/error/deadline; managed provider rebind
  invalidation, measurement, TextBox geometry, ownership, wrong-thread and reentrancy.
- GPU opt-in: `--gpu-renderer-recovery` covers a managed custom-host stop after an
  invalid-resource error, explicit dispose/recreate/reupload/rebind, stale commands
  and exact output preservation. Native `renderer::recovery_tests::` injects only
  callback state on a healthy device, then checks bounded rejection and clean
  reconstruction with correct replacement pixels. No public fault-injection ABI.
- Both GPU tests are in `scripts/verify.ps1 -Configuration Release -Layer Gpu`.
  Report adapter type separately. Software Vulkan results do not demonstrate physical
  GPU loss, driver restart, native-window presentation or platform acceptance.
