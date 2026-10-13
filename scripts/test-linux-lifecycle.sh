#!/usr/bin/env bash
# Orchestration only: mocks do not prove SDL lifecycle correctness.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
mkdir -p "$scratch/repo with spaces/scripts" "$scratch/bin" "$scratch/unrelated"
cp "$root/scripts/verify-linux-lifecycle.sh" "$scratch/repo with spaces/scripts/"
cat > "$scratch/bin/mock" <<'MOCK'
#!/bin/bash
set -euo pipefail
name="${0##*/}"
case "$name" in
    uname) echo "${MOCK_OS:-Linux}"; exit 0 ;;
    getconf) echo "${MOCK_BITS:-64}"; exit 0 ;;
    git)
        if [[ "$*" == 'rev-parse HEAD' ]]; then echo 0123456789abcdef0123456789abcdef01234567
        elif [[ "${MOCK_DIRTY:-false}" == true ]]; then echo ' M example'; fi
        exit 0 ;;
esac
printf '%s|%s|' "$PWD" "$name" >> "$TRACE"
printf '<%s>' "$@" >> "$TRACE"
printf '\n' >> "$TRACE"
[[ ! -v DISPLAY && ! -v WAYLAND_DISPLAY && ! -v SDL_QUIT_ON_LAST_WINDOW_CLOSE ]]
if [[ "$name" == timeout ]]; then
    [[ "$1" == --signal=TERM && "$2" == --kill-after=5s && "$3" == 30s ]]
    [[ "${MOCK_TIMEOUT:-0}" == 0 ]] || exit "$MOCK_TIMEOUT"
    shift 3
    exec "$@"
fi
exit "${MOCK_EXIT:-0}"
MOCK
chmod +x "$scratch/bin/mock"
for command in git uname getconf timeout dotnet; do ln -s mock "$scratch/bin/$command"; done
export PATH="$scratch/bin:$PATH" TRACE="$scratch/trace"
export DISPLAY=':must-not-connect' WAYLAND_DISPLAY=must-not-connect SDL_QUIT_ON_LAST_WINDOW_CLOSE=1
run() {
    rm -f "$TRACE"
    local status=0
    (cd "$scratch/unrelated"; bash "$scratch/repo with spaces/scripts/verify-linux-lifecycle.sh" "$@") > "$scratch/output" 2>&1 || status=$?
    return "$status"
}
for configuration in Debug Release; do
    run "$configuration"
    [[ "$(wc -l < "$TRACE")" == 2 ]]
    grep -Fx "$scratch/repo with spaces|dotnet|<run><--project><tests/YoUI.Platform.Linux.Tests><-c><$configuration><--no-build><--><--native-init-failure>" "$TRACE" >/dev/null
    evidence="$(sed -n 's/^Verification evidence: //p' "$scratch/output")"
    for field in status=passed exit_code=0 layer=NoDisplayLifecycle dirty=false native_window_acceptance=not_run gpu_acceptance=not_run "configuration=$configuration"; do
        grep -Fx "$field" "$evidence" >/dev/null
    done
    [[ "$DISPLAY" == ':must-not-connect' && "$WAYLAND_DISPLAY" == must-not-connect && "$SDL_QUIT_ON_LAST_WINDOW_CLOSE" == 1 ]]
    echo "PASS $configuration isolated environment, bounded command, evidence and unrelated working directory"
done
export MOCK_DIRTY=true
run Debug
evidence="$(sed -n 's/^Verification evidence: //p' "$scratch/output")"
grep -Fx dirty=true "$evidence" >/dev/null
unset MOCK_DIRTY
for code in 1 23 124 137; do
    export MOCK_EXIT="$code"
    status=0; run Release || status=$?
    [[ "$status" == "$code" ]]
    evidence="$(sed -n 's/^Verification evidence: //p' "$scratch/output")"
    grep -Fx status=failed "$evidence" >/dev/null
    grep -Fx "exit_code=$code" "$evidence" >/dev/null
    ! grep -F 'PASS Linux' "$scratch/output"
    echo "PASS failed probe exit $code remains failed"
done
export MOCK_EXIT=0 MOCK_TIMEOUT=124
status=0; run Debug || status=$?
[[ "$status" == 124 && "$(wc -l < "$TRACE")" == 1 ]]
echo 'PASS timeout propagates without claiming a probe pass'
unset MOCK_TIMEOUT
for input in invalid 'Debug extra'; do
    if [[ "$input" == invalid ]]; then set -- invalid; else set -- Debug extra; fi
    status=0; run "$@" || status=$?
    [[ "$status" == 2 && ! -e "$TRACE" ]]
done
for host in Darwin Linux32; do
    if [[ "$host" == Darwin ]]; then export MOCK_OS=Darwin; else export MOCK_BITS=32; fi
    status=0; run Debug || status=$?
    [[ "$status" == 2 && ! -e "$TRACE" ]]
    unset MOCK_OS MOCK_BITS
done
echo 'PASS invalid arguments and unsupported hosts never start the probe'
