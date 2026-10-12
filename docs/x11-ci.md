# Experimental virtual X11 CI acceptance

The manual-only [`x11-acceptance.yml`](../.github/workflows/x11-acceptance.yml)
prepares a **future standard GitHub-hosted Ubuntu 24.04 x64 run**. It has not
been dispatched or proven in the implementation environment. That environment
denied AF_UNIX socket creation; this work does not retry that operation, use TCP,
change its permissions, or treat mocks/headless rendering as native acceptance.
The SDL2 X11 host remains experimental until real acceptance evidence exists.

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

The original `accept_x11.py` performs matching native/managed builds, launches
the real Linux sample, drives XTest and Xlib input, checks capture/focus/text/
resize/unmap-remap/close/reopen behavior, and captures the mapped client with
`XGetImage`. Its failure, malformed/missing evidence, an unexpected adapter,
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
  per-check states, toolchain and binary hashes, timing and failures.
- `x11-ci/native/sample/native-window.png`: actual mapped X11 client pixels.
  Inspect the image visually; a nonuniform capture is not a visual golden test.
- `x11-ci/native/sample/frame.png`: renderer readback, **not** presentation.
- Native state/lifecycle JSON and command/Xlib logs remain alongside those files.

A passing run supports virtual X11 presentation and synthetic XTest input with
Openbox and software Vulkan for that commit/configuration only. It does not
establish physical keyboard/mouse input, hardware GPU behavior/performance,
window-manager minimize/restore (the runner tests unmap/remap), native Wayland,
IME, accessibility, Windows or macOS acceptance. Existing CPU logic CI and
headless software-GPU verification remain separate evidence layers.

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
command and the workflow are prepared infrastructure, not a record of execution.
