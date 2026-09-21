param([string[]]$Backends = @('dx12','vulkan','gl'), [string]$Output = 'artifacts/backends', [int]$TimeoutSeconds = 45)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & "$PSScriptRoot/build.ps1" -Configuration Release
    cargo build --workspace --release --examples --locked
    if ($LASTEXITCODE -ne 0) { throw 'Native examples build failed' }
    uv run "$PSScriptRoot/verify_backends.py" --backends @Backends --output $Output --timeout $TimeoutSeconds
    if ($LASTEXITCODE -ne 0) { throw "Some backend checks failed or timed out; inspect $Output/results.json" }
} finally { Pop-Location }
