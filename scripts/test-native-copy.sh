#!/usr/bin/env bash
# Regression checks against real Cargo output; build native + managed projects first.
set -euo pipefail

configuration="${1:-Debug}"
if [[ $# -gt 1 || ( "$configuration" != Debug && "$configuration" != Release ) ]]; then
    echo "Usage: $0 [Debug|Release]" >&2
    exit 2
fi
if [[ "$(uname -s)" != Linux ]]; then
    echo "These native library copy regression checks require Linux." >&2
    exit 2
fi

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
profile=debug
if [[ "$configuration" == Release ]]; then profile=release; fi
native="$root/target/$profile/libyoui_render.so"
[[ -s "$native" ]] || { echo "Build the $profile Rust library first: $native" >&2; exit 1; }
scratch="$(mktemp -d)"
trap 'rm -rf -- "$scratch"' EXIT
# SDK selection follows the working directory, not the project file.
# Preserve the repository pin while exercising an unrelated directory.
cp "$root/global.json" "$scratch/global.json"

# An unrelated working directory catches project-relative source path regressions.
cd "$scratch"
for project in samples/YoUI.Showcase tools/YoUI.Editor docs/examples/HelloYoUI; do
    name="${project##*/}"
    project_file="$root/$project/$name.csproj"
    cmp "$native" "$root/$project/bin/$configuration/net9.0/libyoui_render.so"
    dotnet msbuild "$project_file" -nologo -t:CopyNative \
        -p:Configuration="$configuration" -p:OutDir="$scratch/$name/"
    cmp "$native" "$scratch/$name/libyoui_render.so"

    # A missing profile must fail explicitly, never silently skip native copying.
    missing_profile="youi-missing-${scratch##*/}"
    [[ ! -e "$root/target/$missing_profile" ]]
    if dotnet msbuild "$project_file" -nologo -t:CopyNative \
        -p:Configuration="$configuration" -p:NativeProfile="$missing_profile" \
        -p:OutDir="$scratch/missing/" > "$scratch/missing.log" 2>&1; then
        echo "FAIL: $name accepted a missing native library" >&2
        exit 1
    fi
    grep -F 'Missing native library:' "$scratch/missing.log" > /dev/null
    [[ ! -e "$scratch/missing/libyoui_render.so" ]]
    printf 'PASS %s: matching %s library, independent working directory, missing-library error\n' "$name" "$profile"
done
