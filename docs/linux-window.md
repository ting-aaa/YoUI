# Experimental Linux X11 window host

`YoUI.Platform.Linux` is a small, single-window SDL2 host for 64-bit Linux/X11.
It gives the independent wgpu renderer the SDL-owned Xlib display and window,
and translates native SDL input into YoUI events. The renderer does not depend
on controls, the document tree, or the application loop.

This remains an **experimental native-window path**. A historical
[GitHub-hosted Release run](https://github.com/ting-aaa/YoUI/actions/runs/38223249352)
at `59154396a30abb1a131c490a4bb708c6817a1a5f` passed the original 23 checks with
Xvfb, Openbox, synthetic XTest input, SDL 2.30 and software Vulkan (llvmpipe).
That record supports only that commit/configuration and the original checks;
it did not test window-manager minimize/restore. It does not carry forward to
the merged text-fix base `41d896eb9bee7bda281c5ef7d2c56740100dce89` or the new
`--require-wm` checks, whose native execution is **NOT RUN**.

The implementation environment still denies AF_UNIX socket creation, so native
X11 interaction, presentation and capture have **not been executed there**.
No bypass or new hosted dispatch is authorized by this change. Fake runner
tests and a headless Vulkan pass must not be presented as native-window
acceptance. Each new native claim needs its own successful `accept_x11.py`
evidence; see [CI evidence and scope](x11-ci.md).

## Requirements and limits

- A 64-bit Linux process and SDL2 2.24.0 or newer, discoverable as
  `libSDL2-2.0.so.0`.
- An existing, accessible X11 `DISPLAY`, `libX11`, and a Vulkan-capable loader
  and device. Mesa software Vulkan is useful but remains software evidence.
- The repository-pinned Rust and .NET SDKs. Python 3.11+ and `uv` run the
  dependency-free acceptance script; `xdotool` supplies XTest OS input.
- Use a dedicated test display with enough space around a 640×400 window and
  its 720×440 resized client. 1280×800 or larger is convenient. The runner moves
  the real display pointer and keyboard focus. Avoid running it on a desktop
  while someone is using it, and close unrelated test applications.
- A window manager is not required by default. The runner sends the
  `WM_DELETE_WINDOW` protocol itself, uses a small native window to receive focus, and tests
  X11 unmap/map. Unmap/map is hidden-window coverage, **not** proof of a window
  manager's minimize/restore behavior. Optional `--require-wm` additionally
  requires a window manager that exposes ICCCM `WM_STATE` and handles the
  minimize/restore requests. The Openbox CI supervisor requires this mode.
- One active YoUI SDL host per process. Construction, event pumping, rendering,
  and disposal belong to the creating thread. The host does not share another
  owner's initialized SDL subsystem or global event queue. It leaves the
  desktop screensaver enabled and bounds client dimensions to 8192 pixels.
- The host disables SDL's synthetic last-window `SDL_QUIT` so one native
  close request invokes `Closing` once. Independent `SDL_QUIT` requests still
  invoke `Closing`, including immediately after a veto. This requires the
  [close hint added in SDL 2.0.22](https://wiki.libsdl.org/SDL2/SDL_HINT_QUIT_ON_LAST_WINDOW_CLOSE)
  and SDL 2.24.0's support for restoring an absent (null) hint value;
  older versions fail before video/window initialization.
  `SDL_QUIT_ON_LAST_WINDOW_CLOSE` must be unset or disabled (`0`/`false`).
  Existing disabled hints are borrowed unchanged. Otherwise the host writes
  only at default priority and restores its previous default value on disposal
  or construction failure. Explicit enabled environment/application hints cause
  a clear construction failure rather than being overridden. Do not change
  SDL global hints or initialize another SDL owner while the host is active.
- One X11 pixel is one layout unit. Automatic DPI scaling, native Wayland,
  multi-window operation, touch, accessibility integration, clipboard
  interoperability, full keyboard-layout coverage, and IME composition/commit
  behavior are outside this acceptance claim. ASCII typing is tested; do not
  infer Unicode input or IME acceptance from it.

The host forces SDL's X11 video driver. Running under an XWayland server would
exercise that X11 route, not a native Wayland implementation. Nothing in the
runner starts a display, changes its security settings, or circumvents an
unavailable or denied display connection.

### ABI references

The LP64 interop layout and lifetime contracts are grounded in these upstream
definitions, with the runtime version checked before the host uses them:

- SDL2 [`SDL_GetWindowWMInfo`](https://wiki.libsdl.org/SDL2/SDL_GetWindowWMInfo)
  and [`SDL_SysWMinfo`](https://wiki.libsdl.org/SDL2/SDL_SysWMinfo).
- SDL 2.32.4 [`SDL_events.h`](https://raw.githubusercontent.com/libsdl-org/SDL/release-2.32.4/include/SDL_events.h),
  including the event union and precise mouse-wheel fields introduced in 2.0.18.
- SDL2 [`SDL_VideoInit`](https://wiki.libsdl.org/SDL2/SDL_VideoInit) and
  [`SDL_CaptureMouse`](https://wiki.libsdl.org/SDL2/SDL_CaptureMouse).
- raw-window-handle 0.6.2
  [`XlibWindowHandle`](https://docs.rs/raw-window-handle/0.6.2/raw_window_handle/struct.XlibWindowHandle.html).

## Run the sample

Build the native library before the matching managed configuration:

```sh
cargo build --workspace --locked --target-dir target
dotnet build samples/YoUI.Linux.Sample/YoUI.Linux.Sample.csproj -c Debug -m:1
YOUI_BACKEND=vulkan dotnet samples/YoUI.Linux.Sample/bin/Debug/net10.0/YoUI.Linux.Sample.dll
```

For Release, add `--release` to Cargo and select `-c Release` and the
`bin/Release` sample path. The sample imports the native-copy target, so its
`libyoui_render.so` must come from the same Cargo profile. The sample displays
a clickable counter button and an editable textbox. A normal run accepts its
first close request.

## Native X11 acceptance

On a permitted Linux host with the dependencies above and an existing display:

```sh
uv run scripts/accept_x11.py --configuration Debug --backend vulkan
uv run scripts/accept_x11.py --configuration Release --backend vulkan
```

`--configuration` and `--backend` are required. Only Vulkan is accepted by this
runner. Each invocation builds Cargo first and then the Linux sample, using that exact configuration;
it does not rely on an old sample binary. Do not run another build against the
same checkout while acceptance is in progress.

To additionally exercise actual window-manager minimize/restore on the selected
display, add `--require-wm`, for example:

```sh
uv run scripts/accept_x11.py --configuration Release --backend vulkan --require-wm
```

This is a bounded, opt-in extension of native X11 acceptance, not a broader
Alpha requirement. It does not replace or change the default unmap/remap path.
No native run of this extension is recorded yet.

The default evidence path is a new, uniquely named directory under
`artifacts/x11-acceptance/`. Set `--output /path/to/new-directory` to choose one.
An existing directory, even an empty one, is rejected. The sample receives its
own new `sample/` child directory. Missing `DISPLAY` or an unopenable display
fails before any build or window tests, with a failed report rather than a
silently skipped success.

The script launches the actual managed sample process. It does not call
`UiDocument.Dispatch`, inject SDL events, or substitute a logical harness for
OS interaction. It checks:

1. Fresh native window, rendered frame, Vulkan/SDL identity, creating-thread
   guard, and duplicate-host guard.
2. Two button clicks through the OS pointer path.
3. Pointer focus, ASCII typing, Ctrl+A replacement, cursor navigation and
   Backspace, then Tab and Shift+Tab traversal.
4. Both directions of OS mouse wheel input.
5. A press dragged outside the client and released without activating the
   button; then native focus loss during another press, requiring pointer
   cancellation and no accidental click on release.
6. Native resize followed by rendering at the new dimensions.
7. X11 unmap, stable rendered-frame count while hidden, and rendering after
   remap.
8. `XGetImage` capture of the live, mapped X11 client, requiring nonuniform
   native pixels. The window is raised before this capture.
9. A first `WM_DELETE_WINDOW` request that is vetoed, continued interaction and
   rendering, then a second request that is accepted with exit code zero.
10. Disposal followed by construction and rendering of a second native host.

With `--require-wm`, after the existing unmap/remap checks and before capture,
the runner additionally:

- Observes ICCCM `WM_STATE` `NormalState` with X map state `IsViewable` on the
  sample's exact client window through the bounded Xlib worker.
- Requests `xdotool windowminimize`, then independently observes `IconicState`
  with `IsUnmapped`. It requires the host to suspend and lose input focus, and
  its rendered-frame count to remain stable while minimized.
- Requests `xdotool windowmap`, then observes `NormalState` with `IsViewable`
  again. After restoring focus, it requires rendering to resume and a new OS
  pointer click to activate the button.

The observed state sequence must be `NormalState → IconicState → NormalState`;
SDL suspension or an unmapped client alone does not establish iconification.
Missing, malformed or nontransitioning WM state fails this mode rather than
silently falling back to unmap/remap coverage.

Closing uses an Xlib client message rather than `xdotool windowclose`, which
can destroy the native window and bypass the application's close veto.
Keyboard and mouse actions use xdotool's actual XTest path without its
`--window` key-event shortcut. Pointer coordinates come from the sample's
current control geometry.

## Evidence and bounds

`result.json` retains the Git SHA and dirty status before and after, toolchain
pin hashes, tool version output, OS identity, selected environment settings,
command logs and deadlines, per-check state snapshots, and native/managed
binary SHA-256 hashes. The managed sample's native copy must match the selected
Cargo profile. Toolchain pins and binary hashes are checked again at the end.
`requireWindowManager` records whether the additional mode was requested;
`adapter.windowManager` retains the required flag and observed client XID,
ICCCM state and map-state transitions. The named WM checks separately record
suspension, stable frames, resumed rendering and post-restore input. A report
from the default mode cannot stand in for this evidence.

The sample's files are separate pieces of evidence:

- `sample/state.json`: final native-window/input state, counters and control
  rectangles. State is written atomically throughout the run.
- `sample/native-window.png`: the actual X11 client's pixels obtained through
  Xlib while it is mapped. The report includes dimensions, SHA-256 and the
  observed distinct-color count, capped at 256. This is the surface-presentation
  artifact. Inspect it visually before claiming correct appearance.
- `sample/frame.png`: renderer readback after accepted close, while the native
  handles remain alive. This is **not a desktop/window screenshot**. Its PNG
  structure, checksums and resized dimensions are checked, and it is hashed.
- `sample/lifecycle.json`: successful second-host creation and rendered frames
  after the first host was disposed.

Adapter classification comes from the renderer's reported device type: `Cpu`
is software, integrated/discrete types are hardware-reported, and other types
remain unclassified. This is not independent hardware certification or a
hardware-performance claim. A software Vulkan result does not establish real
GPU acceptance.

Build commands default to a 900-second bound each. Native-window interaction
has a 120-second total bound and individual command/state waits default to
15 seconds. These are configurable with `--build-timeout-seconds`,
`--window-timeout-seconds`, and `--event-timeout-seconds`. The sample itself has
a two-minute acceptance watchdog, so increasing the runner's window timeout
does not extend that watchdog.

All Xlib calls run in an owned subprocess, including display open, focus,
coordinate and WM/map-state queries, `XSync`, pixel capture, and display close.
The parent uses nonblocking stdin/stdout pipes for requests and responses; it makes no direct
Xlib calls. Display probing before builds uses the lesser of the event and
window bounds. Once the sample starts, each operation is limited by both the
event bound and the remaining shared window runtime. A hung open, capture or
close cannot keep the parent waiting indefinitely: it kills the worker's
process group, reaps it, and records failure. `xlib-worker.log` and
`xlibOperations` in the report preserve worker diagnostics and operation status.

Other subprocesses also have their own process groups; timeouts, interruption
and failed window checks terminate the owned groups and retain logs. Forced
termination has a separate bounded reap wait, with any cleanup failure
recorded. An outstanding button press is released during cleanup.

## Runner and no-display tests

The existing SDL no-display lifecycle regressions run separately from window
acceptance. Install SDL2 2.24.0 or newer (`libsdl2-2.0-0` on Ubuntu), then run:

```sh
bash scripts/verify-linux.sh Debug
bash scripts/verify-linux-lifecycle.sh Debug
# Repeat both commands with Release for that configuration.
```

The lifecycle entrypoint uses the already-built matching configuration. It
clears display variables and the close-hint override only in its child process,
bounds the probe to 30 seconds plus a 5-second forced-cleanup allowance, and
records source/configuration/status/exit code under `artifacts/verification/`.
Missing dependencies or builds and timeouts fail rather than skip. It checks
failed SDL video initialization, singleton/hint cleanup, close-veto contracts,
and disposal from logical callbacks without creating a window or renderer.
Its success is neither native-window nor GPU evidence. Both Ubuntu logic CI
configurations run this probe; `scripts/test-linux-lifecycle.sh` tests only its
orchestration and does not substitute for the real SDL checks.

The separate Python runner tests are:

```sh
uv run scripts/test_accept_x11.py
```

These standard-library tests cover CLI requirements, new-output ownership,
Debug/Release build ordering and profile selection, metadata, failed builds,
missing/unopenable displays, process-group termination, state/adapter/PNG
validation, and a fake OS-input sequence. Real subprocess fixtures deliberately
block forever at simulated Xlib open/capture/close operations to verify timeout,
kill/reap behavior and failure reporting without touching a display. They do not start SDL, open an X
display, render a native surface, or establish native acceptance. Keep their
results separate from `scripts/verify.ps1` logical/headless GPU evidence and
from successful native-window runner artifacts.

For an unwritable default uv cache, select a writable cache directory through
`UV_CACHE_DIR`; this changes only the Python tool cache, not display access.

## GitHub-hosted virtual display workflow

The [manual Xvfb/Openbox CI workflow](x11-ci.md) prepares a standard Ubuntu
runner with authenticated local X11 and software Vulkan. It is separate from
this existing-display runner. The historical original-check pass above does
not validate later revisions or the new required WM mode. Publication and each
new dispatch require their own approval; a workflow definition, mocked tests
and static checks are not a hosted native-window pass.
