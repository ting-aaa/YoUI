#!/usr/bin/env bash
# CPU logic and host-native compilation only; not GPU or native-window acceptance.
set -euo pipefail

configuration="${1:-Debug}"
if [[ $# -gt 1 || ( "$configuration" != Debug && "$configuration" != Release ) ]]; then
    echo "Usage: $0 [Debug|Release]" >&2
    exit 2
fi
if [[ "$(uname -s)" != Linux ]]; then
    echo "This script verifies Linux compilation and CPU logic only." >&2
    exit 2
fi

root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
cargo_profile=()
if [[ "$configuration" == Release ]]; then cargo_profile=(--release); fi

# Keep Cargo's output in the directory consumed by NativeLibrary.targets.
cargo build --workspace --locked --target-dir "$root/target" "${cargo_profile[@]}"
cargo fmt --all -- --check
cargo clippy --workspace --all-targets --locked --target-dir "$root/target" "${cargo_profile[@]}" -- -D warnings
cargo test --workspace --locked --target-dir "$root/target" "${cargo_profile[@]}"

dotnet run --project tests/YoUI.Tests -c "$configuration" -p:BuildInParallel=false
dotnet run --project tests/YoUI.Editor.Tests -c "$configuration" -p:BuildInParallel=false
dotnet build samples/YoUI.Showcase/YoUI.Showcase.csproj -c "$configuration" -m:1
dotnet build docs/examples/HelloYoUI/HelloYoUI.csproj -c "$configuration" -m:1
bash scripts/test-native-copy.sh "$configuration"

printf '\nLinux %s compilation, native library copy and CPU logic checks passed.\n' "$configuration"
printf 'GPU rendering and Windows window/input acceptance were not run.\n'
