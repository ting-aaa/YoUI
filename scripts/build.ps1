param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if ($Configuration -eq 'Release') { cargo build --workspace --release --locked } else { cargo build --workspace --locked }
    if ($LASTEXITCODE -ne 0) { throw 'Rust build failed' }
    dotnet build samples/YoUI.Showcase/YoUI.Showcase.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
    dotnet build tools/YoUI.Editor/YoUI.Editor.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Editor build failed' }
    dotnet build samples/YoUI.Exporter.Sample/YoUI.Exporter.Sample.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Exporter plugin build failed' }
} finally { Pop-Location }
