#!/usr/bin/env bash
# Real SDL no-display regressions only; not native-window or GPU acceptance.
# Run verify-linux.sh first to build this configuration's test executable.
set -euo pipefail
configuration="${1:-Debug}"
if [[ $# -gt 1 || ( "$configuration" != Debug && "$configuration" != Release ) ]]; then
    echo "Usage: $0 [Debug|Release]" >&2
    exit 2
fi
if [[ "$(uname -s)" != Linux || "$(getconf LONG_BIT)" != 64 ]]; then
    echo 'This probe requires 64-bit Linux and SDL2 2.24.0 or newer.' >&2
    exit 2
fi
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
sha="$(git rev-parse HEAD)"
changes="$(git status --porcelain)"
dirty=false
[[ -z "$changes" ]] || dirty=true
mkdir -p "$root/artifacts/verification"
evidence="$(mktemp -d "$root/artifacts/verification/linux-lifecycle-XXXXXXXX")/result.txt"
printf 'sha=%s\ndirty=%s\nconfiguration=%s\nplatform=%s\nlayer=NoDisplayLifecycle\nnative_window_acceptance=not_run\ngpu_acceptance=not_run\nstarted_utc=%s\n' \
    "$sha" "$dirty" "$configuration" "$(uname -srm)" "$(date -u +%FT%TZ)" > "$evidence"
finish() {
    local status=$?
    trap - EXIT
    if [[ $status -eq 0 ]]; then
        printf 'status=passed\n' >> "$evidence"
    else
        printf 'status=failed\n' >> "$evidence"
    fi
    printf 'exit_code=%s\ncompleted_utc=%s\n' "$status" "$(date -u +%FT%TZ)" >> "$evidence"
    printf 'Verification evidence: %s\n' "$evidence"
    exit "$status"
}
trap finish EXIT
printf 'BEGIN sha=%s dirty=%s configuration=%s layer=NoDisplayLifecycle\n' "$sha" "$dirty" "$configuration"
# Isolate only the child process. Never connect to a caller's display, and do not
# let an inherited SDL close-hint override invalidate the absent-hint fixture.
# GNU timeout also bounds a regression that leaves DesktopWindow.Run spinning.
env -u DISPLAY -u WAYLAND_DISPLAY -u SDL_QUIT_ON_LAST_WINDOW_CLOSE \
    timeout --signal=TERM --kill-after=5s 30s \
    dotnet run --project tests/YoUI.Platform.Linux.Tests -c "$configuration" --no-build -- --native-init-failure
printf 'PASS Linux %s no-display lifecycle; no native window or GPU acceptance.\n' "$configuration"
