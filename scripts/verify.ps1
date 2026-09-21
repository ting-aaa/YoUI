param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & "$PSScriptRoot/build.ps1" -Configuration $Configuration
    cargo fmt --all -- --check
    if ($LASTEXITCODE -ne 0) { throw 'Rust formatting failed' }
    cargo clippy --workspace --all-targets --locked -- -D warnings
    if ($LASTEXITCODE -ne 0) { throw 'Rust lint failed' }
    cargo test --workspace --locked
    if ($LASTEXITCODE -ne 0) { throw 'Native logic tests failed' }
    cargo test -p youi-render --test gpu --locked -- --ignored --nocapture
    if ($LASTEXITCODE -ne 0) { throw 'GPU reference test failed (a real adapter is required)' }
    dotnet run --project tests/YoUI.Tests -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Managed logic tests failed' }
    dotnet run --project tests/YoUI.Editor.Tests -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Editor, asset, code generation or plugin checks failed' }
    dotnet run --project tools/YoUI.Editor -c $Configuration --no-build -- --headless
    if ($LASTEXITCODE -ne 0) { throw 'Editor GPU rendering failed' }
    dotnet run --project tools/YoUI.Editor -c $Configuration --no-build -- --smoke
    if ($LASTEXITCODE -ne 0) { throw 'Editor native input checks failed' }
    dotnet run --project samples/YoUI.Showcase -c $Configuration --no-build -- --headless
    if ($LASTEXITCODE -ne 0) { throw 'Cross-language render/export failed' }
    dotnet run --project samples/YoUI.Showcase -c $Configuration --no-build -- --smoke
    if ($LASTEXITCODE -ne 0) { throw 'Native window interaction checks failed' }
    dotnet run --project samples/YoUI.Showcase -c $Configuration --no-build -- --benchmark
    if ($LASTEXITCODE -ne 0) { throw 'Performance probe failed' }
    cargo run -p youi-render --example render_scene --locked -- artifacts/render-scene.png
    if ($LASTEXITCODE -ne 0) { throw 'Independent renderer example failed' }
    cargo run -p youi-scene --example scene_graph --locked -- artifacts/scene-graph.png
    if ($LASTEXITCODE -ne 0) { throw 'Independent scene example failed' }
    Write-Host 'YoUI verification passed. Screenshots and metrics: artifacts/'
} finally { Pop-Location }
