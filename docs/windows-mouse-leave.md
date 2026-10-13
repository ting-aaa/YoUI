# Windows mouse-leave acceptance

Issue #27 is handled in the Windows host. The first in-client `WM_MOUSEMOVE`
arms `TrackMouseEvent(TME_LEAVE)`. `WM_MOUSELEAVE` disarms tracking and sends an
outside logical move to clear hover when this HWND does not own capture. A
captured drag continues receiving actual coordinates; leave does not cancel
capture or inject a fake drag position. Re-entry arms tracking again. Outside
captured moves do not re-arm tracking, avoiding immediate-leave message loops.

A failed registration throws a `Win32Exception` with the native error. The
existing callback error boundary captures it and `PumpEvents` reports the
failure, rather than silently leaving stale hover or marking tracking armed.

## Logical checks

After building the matching native library, run:

```powershell
dotnet run --project tests/YoUI.Platform.Windows.Tests -c Debug
```

This cross-platform test covers tracking re-arm, outside capture movement,
registration failure, struct layout, edge-button hover invalidation and logical
capture/release behavior. It does not demonstrate native Windows delivery.
It is also included in `scripts/verify.ps1 -Layer Logic`.

## Interactive Win32 checks

On an unlocked Windows desktop with a working renderer, run:

```powershell
./scripts/verify.ps1 -Configuration Debug -Layer Window
# Or, after building native code for the same configuration:
dotnet run --project tests/YoUI.Platform.Windows.Tests -c Debug -- --native-mouse-leave
```

Repeat with `Release` for release evidence. Do not touch the mouse while the
probe runs. It temporarily creates a topmost test window at desktop (100,100),
moves the cursor and restores its original position in cleanup. The desktop
must have room for the 320-by-200 logical-pixel window and its frame.

The probe requires three successful edge exits/re-entries, capture-preserving
movement outside, release outside without activation, a fresh click, and
another exit after capture. `SetCursorPos` causes real Windows movement and
leave notifications; the test never posts or directly dispatches
`WM_MOUSELEAVE`. Press/release messages are deterministic `SendMessage`
injections, so this is not evidence of hardware button delivery. Any missing
expected event fails within three seconds. Unsupported platforms fail rather
than recording a pass.

Record configuration, commit, desktop environment and the output separately
from logical checks. Adding this entry or compiling it is not native-window
acceptance; actual Windows execution is required.

References: [TrackMouseEvent](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackmouseevent),
[WM_MOUSELEAVE](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mouseleave).
