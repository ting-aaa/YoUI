#!/usr/bin/env bash
# Orchestration tests only: mocked tools never count as Rust/.NET/GPU evidence.
set -euo pipefail
root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
mkdir -p "$scratch/repo with spaces/scripts" "$scratch/bin" "$scratch/unrelated"
cp "$root/scripts/verify-linux.sh" "$scratch/repo with spaces/scripts/"
cat > "$scratch/bin/mock" <<'MOCK'
#!/bin/bash
set -euo pipefail
name="${0##*/}"
if [[ "$name" == git ]]; then
    [[ "$*" != 'rev-parse HEAD' ]] || echo 0123456789abcdef0123456789abcdef01234567
    exit 0
fi
if [[ "$name" == uname ]]; then echo "${MOCK_OS:-Linux}"; exit 0; fi
count=0
[[ ! -f "$COUNT" ]] || read -r count < "$COUNT"
count=$((count + 1))
printf '%s\n' "$count" > "$COUNT"
printf '%s|%s|' "$PWD" "$name" >> "$TRACE"
printf '<%s>' "$@" >> "$TRACE"
printf '\n' >> "$TRACE"
[[ "$count" != "${FAIL_AT:-0}" ]] || exit 23
MOCK
chmod +x "$scratch/bin/mock"
for command in cargo dotnet bash git uname; do ln -s mock "$scratch/bin/$command"; done
export PATH="$scratch/bin:$PATH" COUNT="$scratch/count" TRACE="$scratch/trace"
run() {
    rm -f "$COUNT" "$TRACE"
    local code=0
    (cd "$scratch/unrelated"; /bin/bash "$scratch/repo with spaces/scripts/verify-linux.sh" "$@") > "$scratch/output" 2>&1 || code=$?
    return "$code"
}
export FAIL_AT=0
for configuration in Debug Release; do
    run "$configuration"
    [[ "$(cat "$COUNT")" == 13 ]]
    [[ "$(grep -cF "$scratch/repo with spaces|" "$TRACE")" == 13 ]]
    grep -F '<test><--workspace><--locked><--target-dir>' "$TRACE" > /dev/null
    [[ "$(grep -cF "<--target-dir><$scratch/repo with spaces/target>" "$TRACE")" == 3 ]]
    [[ "$(grep -cF "<-c><$configuration>" "$TRACE")" == 8 ]]
    sed -n 's/^.*|dotnet|//p' "$TRACE" > "$scratch/managed-actual"
    printf '%s\n' \
        "<run><--project><tests/YoUI.Tests><-c><$configuration><-p:BuildInParallel=false>" \
        "<run><--project><tests/YoUI.Editor.Tests><-c><$configuration><-p:BuildInParallel=false>" \
        "<run><--project><tests/YoUI.Component.Tests><-c><$configuration><-p:BuildInParallel=false>" \
        "<run><--project><tests/YoUI.Profiling.Tests><-c><$configuration><-p:BuildInParallel=false>" \
        "<run><--project><tests/YoUI.Platform.Linux.Tests><-c><$configuration><-p:BuildInParallel=false>" \
        "<build><samples/YoUI.Showcase/YoUI.Showcase.csproj><-c><$configuration><-m:1>" \
        "<build><docs/examples/HelloYoUI/HelloYoUI.csproj><-c><$configuration><-m:1>" \
        "<build><samples/YoUI.Linux.Sample/YoUI.Linux.Sample.csproj><-c><$configuration><-m:1>" > "$scratch/managed-expected"
    cmp "$scratch/managed-expected" "$scratch/managed-actual"
    if [[ "$configuration" == Release ]]; then
        [[ "$(grep -cF '<--release>' "$TRACE")" == 3 ]]
    else
        ! grep -F '<--release>' "$TRACE"
    fi
    ! grep -E -- '--gpu|--ignored|--headless|--smoke|--benchmark' "$TRACE"
    grep -F "Linux $configuration compilation" "$scratch/output" > /dev/null
    grep -F 'layer=Logic' "$scratch/output" > /dev/null
    echo "PASS Linux $configuration commands, profile, layer and independent working directory"
done

# The shell entrypoint remains logic-only. Check that the additive capability
# probe is wired into the existing PowerShell GPU phase, without inventing a gate.
# test-verification.ps1 executes and fault-injects that full phase with mocked tools.
capability_flag='--gpu-renderer-capabilities'
[[ "$(grep -cF -- "$capability_flag" "$root/scripts/verify.ps1")" == 1 ]]
gpu_block="$(sed -n "/if (\$Layer -eq 'Gpu' -or \$Layer -eq 'All') {/,/^    }/p" "$root/scripts/verify.ps1")"
[[ "$(printf '%s\n' "$gpu_block" | grep -cF -- "$capability_flag")" == 1 ]]
printf '%s\n' "$gpu_block" | grep -F "'run','--project','tests/YoUI.Tests','-c',\$Configuration,'-p:BuildInParallel=false','--','$capability_flag'" > /dev/null
grep -F 'RendererCapabilitiesTests.RunLogic(Test, Require);' "$root/tests/YoUI.Tests/Program.cs" > /dev/null
grep -F 'args[0] == "--gpu-renderer-capabilities") { RendererCapabilitiesTests.RunGpu(); return; }' "$root/tests/YoUI.Tests/Program.cs" > /dev/null
echo 'PASS capability logic/GPU wiring preserves existing verification layers (source checks only)'
for step in {1..13}; do
    export FAIL_AT="$step"
    code=0; run Release || code=$?
    [[ "$code" == 23 ]]
    [[ "$(cat "$COUNT")" == "$step" ]]
    ! grep -F 'checks passed.' "$scratch/output"
    evidence="$(sed -n 's/^Verification evidence: //p' "$scratch/output")"
    grep -Fx 'status=failed' "$evidence" > /dev/null
    grep -Fx 'exit_code=23' "$evidence" > /dev/null
    echo "PASS Linux propagates failure at command $step"
done
export MOCK_OS=Darwin
code=0; run Debug || code=$?
[[ "$code" == 2 && ! -e "$COUNT" ]]
echo 'PASS Linux rejects unsupported host'
unset MOCK_OS
export FAIL_AT=0
for input in invalid 'Debug extra'; do
    # Deliberately pass separate invalid positional arguments in the second case.
    if [[ "$input" == invalid ]]; then set -- invalid; else set -- Debug extra; fi
    code=0; run "$@" || code=$?
    [[ "$code" == 2 && ! -e "$COUNT" ]]
    echo "PASS Linux rejects arguments: $input"
done
