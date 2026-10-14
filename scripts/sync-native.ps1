param(
    [Parameter(Mandatory)][ValidateSet('Debug','Release')][string]$Configuration,
    [string]$Stage = 'manual',
    [switch]$AsJson
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($args.Count -ne 0) { throw "Unexpected arguments: $args" }
$projectRoot = Split-Path $PSScriptRoot -Parent
$profile = $Configuration.ToLowerInvariant()
$nativeName = if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) { 'youi_render.dll' }
    elseif ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)) { 'libyoui_render.so' }
    elseif ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::OSX)) { 'libyoui_render.dylib' }
    else { throw 'Unsupported native library host' }
$source = Join-Path $projectRoot "target/$profile/$nativeName"
if (!(Test-Path -LiteralPath $source -PathType Leaf) -or (Get-Item -LiteralPath $source).Length -eq 0) {
    throw "Missing native library: $source. Build the matching $Configuration Rust library first."
}
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
# Only executable projects importing NativeLibrary.targets consume this library.
# Enumerate existing outputs for the requested configuration; never create output
# folders or touch another profile, arbitrary bin folders, or package assets.
$projects = @('samples/YoUI.Showcase','tools/YoUI.Editor','docs/examples/HelloYoUI',
    'samples/YoUI.Linux.Sample','tests/YoUI.Tests','tests/YoUI.Component.Tests',
    'tests/YoUI.Platform.Windows.Tests')
$consumers = @(
    foreach ($project in $projects) {
        $projectName = Split-Path $project -Leaf
        $configurationDirectory = Join-Path $projectRoot "$project/bin/$Configuration"
        if (!(Test-Path -LiteralPath $configurationDirectory -PathType Container)) { continue }
        foreach ($manifest in Get-ChildItem -LiteralPath $configurationDirectory -Recurse -File -Filter "$projectName.deps.json" | Sort-Object FullName) {
            $outputDirectory = $manifest.DirectoryName
            if (!(Test-Path -LiteralPath (Join-Path $outputDirectory "$projectName.dll") -PathType Leaf)) { continue }
            $destination = Join-Path $outputDirectory $nativeName
            $previousHash = if (Test-Path -LiteralPath $destination -PathType Leaf) { (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash } else { $null }
            if ($previousHash -ne $sourceHash) { Copy-Item -LiteralPath $source -Destination $destination -Force }
            $consumerHash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
            if ($consumerHash -ne $sourceHash) { throw "Native library hash mismatch after copy: $destination" }
            [ordered]@{ path = $destination; binaryHash = $consumerHash; updated = $previousHash -ne $sourceHash }
        }
    }
)
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $sourceHash) {
    throw "Native library changed while synchronizing: $source. Finish Cargo builds before verification."
}
$snapshot = [ordered]@{
    stage = $Stage
    recordedUtc = [DateTime]::UtcNow.ToString('o')
    configuration = $Configuration
    profile = $profile
    source = $source
    sourceBinaryHash = $sourceHash
    consumers = $consumers
}
if ($AsJson) { $snapshot | ConvertTo-Json -Depth 5 -Compress } else { $snapshot }
