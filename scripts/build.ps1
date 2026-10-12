param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
if ($args.Count -ne 0) { throw "Unexpected arguments: $args" }
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    if ($Configuration -eq 'Release') { cargo build --workspace --release --locked --target-dir "$projectRoot/target" } else { cargo build --workspace --locked --target-dir "$projectRoot/target" }
    if ($LASTEXITCODE -ne 0) { throw 'Rust build failed' }
    dotnet build samples/YoUI.Showcase/YoUI.Showcase.csproj -c $Configuration -m:1
    if ($LASTEXITCODE -ne 0) { throw 'C# build failed' }
    dotnet build tools/YoUI.Editor/YoUI.Editor.csproj -c $Configuration -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Editor build failed' }
    dotnet build samples/YoUI.Exporter.Sample/YoUI.Exporter.Sample.csproj -c $Configuration -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Exporter plugin build failed' }
    dotnet build docs/examples/HelloYoUI/HelloYoUI.csproj -c $Configuration -m:1
    if ($LASTEXITCODE -ne 0) { throw 'HelloYoUI build failed' }
    dotnet build samples/YoUI.Linux.Sample/YoUI.Linux.Sample.csproj -c $Configuration -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Linux sample build failed' }
} finally { Pop-Location }
