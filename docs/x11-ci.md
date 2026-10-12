# Experimental virtual X11 CI acceptance

The manual-only [`x11-acceptance.yml`](../.github/workflows/x11-acceptance.yml)
uses a standard GitHub-hosted Ubuntu 24.04 x64 runner. A historical
[Release run](https://github.com/ting-aaa/YoUI/actions/runs/38223249352) at commit
`59154396a30abb1a131c490a4bb708c6817a1a5f` passed the original 23 checks with
Xvfb/Openbox, synthetic XTest input, SDL 2.30 and software Vulkan (llvmpipe).
It establishes only that commit/configuration's original virtual-X11 checks;
those checks used unmap/remap, not actual WM minimize/restore.

The merged text-fix base `41d896eb9bee7bda281c5ef7d2c56740100dce89` does not inherit
that native pass. The new required WM mode and its native checks are **NOT RUN**.
The SDL2 X11 host remains experimental. The implementation environment denied
AF_UNIX socket creation; this work does not retry that operation, use TCP,
change permissions, or treat mocks/headless rendering as native acceptance.
No new hosted dispatch is authorized by this change.

## Publication, dispatch, and cost boundary

The workflow has only `workflow_dispatch`; no push, pull-request, schedule or
workflow-run trigger. GitHub requires the workflow file to exist on the default
branch before it can be dispatched. Publishing/merging the file and authorizing
a specific dispatch are separate decisions. Nothing about preparing this file
starts a remote job. After publication and explicit run approval, select Debug
or Release and a reviewed ref. Each dispatch runs one configuration, never a
matrix. Both configurations need their own successful records before claiming
both passed.

The job uses `ubuntu-24.04`, not a larger, GPU, self-hosted or paid custom runner.
It has a 45-minute overall limit and stores artifacts for **3 days**. Standard
runner minutes and artifact storage are subject to the repository's plan and
GitHub billing; this document makes no guarantee of free usage. No billing
settings, account permissions, repository secrets or persistent credentials are
created. Permissions are `contents: read`; checkout disables credential
persistence. No publishing, pull request, merge or deployment is performed.

Official references: [manual dispatch requirements](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow),
[GitHub Actions billing](https://docs.github.com/en/billing/concepts/product-billing/github-actions).

## Dependencies and provenance

Ubuntu's configured official package repositories supply Xvfb, xauth, X11
utilities, xdotool/XTest, Openbox, SDL2, the Vulkan loader, Mesa Vulkan drivers,
and vulkan-tools. The selected lavapipe ICD is discovered from the installed
`mesa-vulkan-drivers` package, allowing the distro's suffixed and unsuffixed
manifest filenames; zero or multiple matches fail. Both Vulkan ICD selection
variables point at this file. Its SHA-256 is retained, and the final renderer
must independently report Vulkan with CPU/software classification.

Rust and .NET use the repository pins. Official actions reuse the SHA pins from
the existing logic workflow. Python runs via a pinned PyPI uv package installed
in a disposable virtual environment. No shell installer is downloaded/executed.
Ubuntu package versions and runner images may change; package inventory, install
logs, runner image version, GitHub run/attempt, toolchain files/versions and exact
commit are retained. The job rejects dirty sources and SHA mismatches before
starting the display, and verifies provenance again after acceptance.

Official references: [Ubuntu Mesa package](https://packages.ubuntu.com/noble/mesa-vulkan-drivers),
[uv 0.8.22 package](https://pypi.org/project/uv/0.8.22/),
[X server options](https://manpages.ubuntu.com/manpages/noble/man1/Xserver.1.html),
[Openbox](https://manpages.ubuntu.com/manpages/noble/man1/openbox.1.html).

## Readiness, teardown, and fail-closed behavior

[`accept_x11_ci.py`](../scripts/accept_x11_ci.py) owns a dedicated `:97` Xvfb
server with a 1280×960 24-bit screen, private temporary Xauthority cookie,
private runtime directory, and `-nolisten tcp`. It does not reuse a desktop,
remove display lockfiles, choose another transport, or disable authentication.
A display collision or denied socket fails. Authentication files are deleted
at exit and excluded from evidence uploads.

`xdpyinfo` must successfully query the live server within the display readiness
budget. An owned Openbox process must publish a valid `_NET_SUPPORTING_WM_CHECK`
window that points to itself and the expected name. Openbox's root
`_OPENBOX_PID` property must identify the owned process.
Each readiness stage has a 20-second probe budget; subprocess timeouts use the
remaining budget. Forced cleanup has an additional bounded reap wait. Merely
starting either process does not establish readiness. Both owned processes must
still be alive immediately before and after actual acceptance.

`accept_x11.py` performs matching native/managed builds, launches
the real Linux sample, drives XTest and Xlib input, checks capture/focus/text/
resize/unmap-remap/close/reopen behavior, and captures the mapped client with
`XGetImage`. The supervisor now also enables `--require-wm` mode: it requires the
sample client's independently observed ICCCM `WM_STATE` sequence
`NormalState → IconicState → NormalState`, paired with X map states
`IsViewable → IsUnmapped → IsViewable`. The runner requests minimization with
`xdotool windowminimize` and restoration with `xdotool windowmap`; all state
queries use the bounded Xlib worker. Host suspension/focus loss, stable
minimized frame count, resumed rendering/focus, observed pointer readiness, and
a post-restore OS button click must also pass. Existing unmap/remap checks remain separate and unchanged.

The supervisor rejects reports without the required WM mode, exact-client state
transitions or any of those named WM checks. Openbox readiness alone, SDL
suspension or unmap/remap alone cannot satisfy them. This is a bounded extension
of this acceptance path, not a broader Alpha requirement.

Native runner failure, malformed/missing evidence, an unexpected adapter,
wrong or dirty commit, missing OS checks, or a mismatched presentation artifact
fails the supervisor. The wrapper cannot manufacture a passed native result.

Native builds have 900-second bounds each; native interaction is bounded to
120 seconds, with 15-second individual events. The workflow additionally bounds
acceptance to 33 minutes. SIGTERM enters the runner's cleanup path; interruption
and normal failures terminate and reap owned display/window-manager groups.
A cleanup error revokes a pass. An immediate OS/runner hard kill cannot promise
Python cleanup or artifact upload; the disposable GitHub runner is the final
isolation boundary. No leftover remote process is being monitored locally.

## Evidence interpretation

The always-run upload step retains setup logs and partial or complete runtime
artifacts, including after a failed readiness/build/window check:

- `x11-ci-setup/`: run and package/toolchain metadata, orchestration test output,
  and acceptance console output. These are not native-window results.
- `x11-ci/result.json`: supervisor status, exact source, owned process exit
  status, readiness diagnostics, selected ICD hash, cleanup errors and the
  native result's hash.
- `x11-ci/native/result.json`: actual acceptance report, adapter identity,
  per-check states, toolchain and binary hashes, timing and failures. New reports
  must set `requireWindowManager: true`; `adapter.windowManager` must retain
  the required flag and all three observed ICCCM/map-state transitions for the
  same client, alongside the five passed WM checks.
- `x11-ci/native/sample/native-window.png`: actual mapped X11 client pixels.
  Inspect the image visually; a nonuniform capture is not a visual golden test.
- `x11-ci/native/sample/frame.png`: renderer readback, **not** presentation.
- Native state/lifecycle JSON and command/Xlib logs remain alongside those files.

A passing run supports virtual X11 presentation and synthetic XTest input with
Openbox and software Vulkan for that commit/configuration only. A future pass
of the required WM mode would additionally support that environment's observed
minimize/restore and post-restore input sequence. The historical 23-check run
provides no such WM-state evidence. The subsequent Release run
[38238087429](https://github.com/ting-aaa/YoUI/actions/runs/38238087429), at
`d9ea357326b7b1adb14838f85552497549bffec5`, observed the real WM-state transitions
and resumed rendering but **FAILED** the restored click assertion. The revised
activation/target-readiness path at `bc7ed454820cd9205d7e98a6a338e564fcdc28be`
was **NOT RUN** when introduced. The later run on its merge reached activation
but failed during positioning before readiness or clicking; see the idempotent
pointer-positioning section below for that result and the current correction.

Neither result establishes physical keyboard/mouse input, hardware GPU
behavior/performance, native Wayland, IME, accessibility, Windows or macOS
acceptance. Existing CPU logic CI and headless software-GPU verification remain
separate evidence layers. Debug and later source revisions need their own
native records; Release evidence is not transferable to them.

## Local tests without a display

```sh
uv run scripts/test_accept_x11.py
uv run scripts/test_accept_x11_ci.py
```

Tests use mocked display/WM/acceptance results and real sleeping child processes
for bounded timeout and kill/reap checks. They do not open X11 or exercise SDL,
XTest, native presentation or real GPUs. No fixture screenshot is acceptance
evidence. Static workflow linting likewise does not establish a hosted CI pass.

On a separately authorized Linux host that permits local X11 sockets, the manual
entry point is below. Do not use it to work around an environment restriction.
The output directory must not already exist and the source checkout must be clean.

```sh
uv run scripts/accept_x11_ci.py --configuration Release \
  --expected-sha "$(git rev-parse HEAD)" \
  --icd /usr/share/vulkan/icd.d/lvp_icd.x86_64.json \
  --output artifacts/my-new-x11-ci-run
```

Use the actual distro-installed lavapipe manifest path, not a guessed one. This
command and the updated workflow describe how to obtain new evidence; they
are not a record that the new WM mode or current source revision has executed.

## Restored-input contract and diagnosis

The WM input contract is **restore, explicitly activate through the WM, then
interact**. It does not assert automatic activation on `windowmap`, or require
an application to raise itself. After observing the restored Normal/IsViewable
state, the runner saves a snapshot and desktop capture *before* requesting
`xdotool windowactivate --sync`. Unlike direct `windowfocus`/XSetInputFocus,
this uses the WM's `_NET_ACTIVE_WINDOW` protocol. A successful command exit is
not sufficient evidence of readiness.

After activation and moving to the button, another diagnostic capture is saved.
Before a single real XTest click, the runner requires sampled readiness across
at least 100 ms: the same client is Normal/IsViewable, owns native keyboard
focus, is the WM active window, has sample-reported focus, and occurs in the
root-to-topmost-child pointer path at the expected button-center coordinates.
No mouse button may be held. This accounts for reparenting WM frames and
rejects an occluding sibling even when client-relative coordinates look right.
No retry click, event injection, focus-clickthrough override, or forced product
focus behavior is used. The click must increment clicks, PointerDown, and
PointerUp exactly once and retain sample focus. WM transitions and the existing
minimized suspension/stable-frame requirements remain mandatory.

`inputDiagnostics` preserves sample state, WM/map state, native focus, WM active
XID, the harness focus-sink XID, client root origin, pointer path/root coordinates,
and button mask. Named `sample/wm-restored-*-desktop.png` files capture the
visible X11 root including occlusion, unlike the later client-only capture.
These are diagnostic observations, not atomic server snapshots or visual golden
tests. Root captures accept low-color or blank pixels; only the separate client
presentation acceptance capture requires at least eight distinct colors. Pre-activation and pre-click observations remain distinguishable. On a
restore/activation/readiness/click error, the runner attempts the same diagnostics
before teardown, without replacing the original failure. Capture errors are
recorded; no screenshot is promised if the worker, server, sample, or shared
runtime budget is already exhausted. Diagnostic RPC and capture operations use
the existing subprocess deadlines and kill/reap behavior.

The saved failed run had no new PreviewInput PointerDown/PointerUp, which is
upstream of UI hit-testing. Its final state lost focus while rendering remained
unsuspended. That supports investigating input delivery/activation, but does not
prove an occluder, wrong stacking, SDL suppression, or a YoUI defect. Its focus
observation preceded click-command startup by roughly 7.8 ms; SDL 2.30.0's X11
backend ordinarily suppresses ButtonPress within 10 ms of its last focus event.
Those command timestamps are not SDL event timestamps, so this is a plausible
race rather than a confirmed explanation. The 100 ms sampled-readiness interval
avoids deliberately clicking immediately after focus. A further authorized
native run is required to establish whether these harness corrections resolve
the original failure. No Linux host or control behavior was changed.

Primary references: [EWMH active-window protocol](https://specifications.freedesktop.org/wm/latest-single/),
[Xlib pointer queries and coordinate translation](https://www.x.org/releases/X11R7.5/doc/libX11/libX11.html),
[SDL 2.30.0 X11 focus-click filtering](https://raw.githubusercontent.com/libsdl-org/SDL/release-2.30.0/src/video/x11/SDL_x11events.c),
[xdotool version-matched input/activation behavior](https://raw.githubusercontent.com/jordansissel/xdotool/v3.20160805.1/xdotool.pod).

### Idempotent pointer positioning

The subsequent native run [38243514555](https://github.com/ting-aaa/YoUI/actions/runs/38243514555)
on merge `2aea51ebc1156eb19b299ab424c4b1fc52074c59` passed 23 checks,
including restored rendering and focus, but timed out in the pointer-positioning
command before the readiness check and restored click. Its failure snapshot
already showed the pointer at the requested client-relative button center.
The version-matched [xdotool mousemove implementation](https://raw.githubusercontent.com/jordansissel/xdotool/v3.20160805.1/cmd_mousemove.c)
waits for departure from the original position with `--sync`, even if an ordinary
move already starts at its target. This is a harness liveness failure; it does
not establish whether the original restored-click failure is fixed.

Element positioning now issues one `mousemove --window` without `--sync`, then
uses bounded Xlib observations to require exact client-relative target coordinates,
a viewable client, and its presence in the topmost pointer hierarchy. It neither
requires a PointerMove event nor moves away and back to manufacture one. Missing
arrival, occlusion, command failure, or observation failure cannot pass. The
non-WM mode does not require WM_STATE or an active-window property for positioning.
The restored path still separately requires all focus/activation/button-release
preconditions sampled across 100 ms and exactly one real click/down/up sequence.
No retry click or product input change is introduced. This correction has only
no-display regression evidence until a new authorized native run completes.
