[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [ValidatePattern('^0\.2\.0-beta\.[1-9][0-9]*$')][string]$Version = '0.2.0-beta.1',
    [switch]$VerifyWindow,
    [switch]$AllowNetworkRestore
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (![Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows) -or
    [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'The Beta native package workflow requires a Windows x64 process.'
}
$projectRoot = Split-Path $PSScriptRoot -Parent
$runId = '{0}-{1}-{2}' -f $Version, $Configuration, [Guid]::NewGuid().ToString('N')
$runRoot = Join-Path $projectRoot "artifacts/beta-packages/$runId"
$feed = Join-Path $runRoot 'feed'
$buildArtifacts = Join-Path $runRoot 'build'
$buildCache = Join-Path $runRoot 'build-packages'
$nativePath = Join-Path $projectRoot "target/$($Configuration.ToLowerInvariant())/youi_render.dll"
$packageIds = @('YoUI', 'YoUI.Platform.Windows', 'YoUI.Editor.Core')
$manifestPath = Join-Path $runRoot 'manifest.json'
$manifest = [ordered]@{
    status = 'running'; startedUtc = [DateTime]::UtcNow.ToString('o'); version = $Version; configuration = $Configuration
    rid = 'win-x64'; packageFeed = $feed; verifyWindow = [bool]$VerifyWindow; nativePath = $nativePath
    packages = @(); consumers = @(); rejectionChecks = @(); error = $null
}
New-Item -ItemType Directory -Path $runRoot, $feed, $buildArtifacts, $buildCache | Out-Null
function Save-Manifest { $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8 }
function Invoke-Checked([string]$File, [string[]]$Arguments) {
    Write-Host ('> {0} {1}' -f $File, ($Arguments -join ' '))
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE." }
}
function Get-SourceSnapshot {
    $files = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate source files.' }
    $entries = @(
        foreach ($file in $files | Sort-Object -Unique) {
            if ($file -match '^(?:\.project-memory/|artifacts/)' -or $file -notmatch '\.(?:cs|csproj|props|targets|rs|toml|lock|ps1|json|md)$') { continue }
            $path = Join-Path $projectRoot $file
            if (Test-Path -LiteralPath $path -PathType Leaf) { [ordered]@{ path = $file; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash } }
        }
    )
    $canonical = ($entries | ForEach-Object { '{0}  {1}' -f $_.sha256, $_.path }) -join "`n"
    [ordered]@{ sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($canonical))); files = $entries }
}
function Assert-Rejected([string]$Name, [string[]]$Properties, [string]$ExpectedError) {
    $destination = Join-Path $runRoot "rejected-$Name"
    $arguments = @('pack', (Join-Path $projectRoot 'managed/YoUI/YoUI.csproj'), '--configuration', $Configuration,
        '--no-build', '--no-restore', '--output', $destination, '-m:1') + $buildProperties + $Properties
    $result = @(& dotnet @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $result | ForEach-Object { Write-Host $_ }
    $result | Set-Content -LiteralPath (Join-Path $runRoot "rejected-$Name.log") -Encoding utf8
    if ($exitCode -eq 0 -or ($result -join "`n") -notmatch [Regex]::Escape($ExpectedError)) { throw "Expected package rejection did not occur: $Name" }
    if ((Test-Path -LiteralPath $destination) -and @(Get-ChildItem -LiteralPath $destination -Filter '*.nupkg').Count -gt 0) { throw "Rejected pack produced a package: $Name" }
    $manifest.rejectionChecks += [ordered]@{ name = $Name; exitCode = $exitCode; expectedError = $ExpectedError; passed = $true }
}
Start-Transcript -LiteralPath (Join-Path $runRoot 'pack.log') | Out-Null
Push-Location $projectRoot
try {
    $manifest.sourceSha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify source commit.' }
    $manifest.sourceStatusBefore = @(& git status --porcelain=v1)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify working-tree state.' }
    $manifest.sourceDirty = $manifest.sourceStatusBefore.Count -gt 0
    & git diff --binary HEAD -- . ':!.project-memory' | Set-Content -LiteralPath (Join-Path $runRoot 'source.patch') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record tracked patch.' }
    $before = Get-SourceSnapshot
    $manifest.sourceSnapshotBefore = $before.sha256
    $before | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'source-before.json') -Encoding utf8
    $manifest.untrackedFiles = @(& git -c core.quotepath=false ls-files --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record untracked paths.' }
    $manifest.dotnet = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'The pinned .NET SDK is required.' }
    $requiredSdk = (Get-Content -LiteralPath (Join-Path $projectRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
    if ($manifest.dotnet -ne $requiredSdk) { throw "Use the pinned .NET SDK $requiredSdk; got $($manifest.dotnet)." }
    $manifest.rust = (& rustc --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Rust compiler is required.' }
    $manifest.rustDetails = (& rustc -vV) -join "`n"
    if ($LASTEXITCODE -ne 0 -or $manifest.rustDetails -notmatch 'host: x86_64-pc-windows-msvc') { throw 'Use the Windows x64 MSVC Rust toolchain.' }
    Save-Manifest

    $cargoArguments = @('build', '-p', 'youi-render', '--locked', '--target-dir', (Join-Path $projectRoot 'target'))
    if (!$AllowNetworkRestore) { $cargoArguments += '--offline' }
    if ($Configuration -eq 'Release') { $cargoArguments += '--release' }
    Invoke-Checked 'cargo' $cargoArguments
    if (!(Test-Path -LiteralPath $nativePath -PathType Leaf)) { throw "Matching native library missing: $nativePath" }
    $nativeBytes = [IO.File]::ReadAllBytes($nativePath)
    if ($nativeBytes.Length -lt 64 -or [BitConverter]::ToUInt16($nativeBytes, 0) -ne 0x5A4D) { throw 'Native library has no DOS/PE header.' }
    $peOffset = [BitConverter]::ToInt32($nativeBytes, 0x3c)
    if ($peOffset -lt 0 -or $peOffset + 6 -gt $nativeBytes.Length -or [BitConverter]::ToUInt32($nativeBytes, $peOffset) -ne 0x00004550 -or [BitConverter]::ToUInt16($nativeBytes, $peOffset + 4) -ne 0x8664) { throw 'Native library is not an x64 PE binary.' }
    $nativeHash = (Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash
    $manifest.nativeSha256 = $nativeHash
    $buildConfig = Join-Path $runRoot 'Build.NuGet.Config'
    '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>' | Set-Content -LiteralPath $buildConfig -Encoding utf8
    $buildProperties = @("-p:Version=$Version", "-p:PackageVersion=$Version", '-p:NuGetAudit=false', '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$buildArtifacts")
    foreach ($id in $packageIds) {
        $project = Join-Path $projectRoot "managed/$id/$id.csproj"
        Invoke-Checked 'dotnet' (@('restore', $project, '--configfile', $buildConfig, '--packages', $buildCache, "-p:Configuration=$Configuration") + $buildProperties)
        Invoke-Checked 'dotnet' (@('pack', $project, '--configuration', $Configuration, '--no-restore', '--output', $feed, '-m:1', '--disable-build-servers') + $buildProperties)
    }
    Assert-Rejected 'native-profile' @("-p:NativeProfile=$(if ($Configuration -eq 'Debug') { 'release' } else { 'debug' })") 'YoUI Beta NativeProfile must match Configuration'
    Assert-Rejected 'missing-native' @("-p:YoUIPackNativeLibraryPath=$(Join-Path $runRoot 'missing/youi_render.dll')") 'Missing matching YoUI native library'

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (@(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg').Count -ne $packageIds.Count) { throw 'Package feed contains an unexpected number of packages.' }
    foreach ($id in $packageIds) {
        $packagePath = Join-Path $feed "$id.$Version.nupkg"
        $archive = [IO.Compression.ZipFile]::OpenRead($packagePath)
        try {
            $files = @($archive.Entries | ForEach-Object { $_.FullName })
            $allowed = '^(?:_rels/\.rels|\[Content_Types\]\.xml|package/services/metadata/core-properties/[^/]+\.psmdcp|README\.md|' + [Regex]::Escape($id) + '\.nuspec|lib/net10\.0/' + [Regex]::Escape($id) + '\.dll)$'
            foreach ($file in $files) {
                if ($file -match $allowed -or ($id -eq 'YoUI' -and $file -eq 'runtimes/win-x64/native/youi_render.dll')) { continue }
                throw "Unexpected payload in ${id}: $file"
            }
            if (@($files | Sort-Object -Unique).Count -ne $files.Count) { throw "Duplicate package entries: $id" }
            $reader = [IO.StreamReader]::new($archive.GetEntry("$id.nuspec").Open())
            try { [xml]$nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($nuspec.SelectSingleNode('//*[local-name()="version"]').InnerText -ne $Version) { throw "Package version mismatch: $id" }
            $dependencies = @($nuspec.SelectNodes('//*[local-name()="dependency"]'))
            if ($dependencies.Count -ne $(if ($id -eq 'YoUI') { 0 } else { 1 })) { throw "Unexpected dependency count: $id" }
            foreach ($dependency in $dependencies) {
                if ($dependency.id -ne 'YoUI' -or $dependency.version -ne $Version) { throw "Unexpected package dependency in ${id}: $($dependency.OuterXml)" }
            }
            if ($id -eq 'YoUI') {
                $stream = $archive.GetEntry('runtimes/win-x64/native/youi_render.dll').Open()
                $sha = [Security.Cryptography.SHA256]::Create()
                try { $packedNativeHash = [Convert]::ToHexString($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
                if ($packedNativeHash -ne $nativeHash) { throw 'Packed DLL differs from the matching native build.' }
            }
            $manifest.packages += [ordered]@{ id = $id; version = $Version; path = $packagePath; sha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash; files = $files }
        } finally { $archive.Dispose() }
    }
    Save-Manifest

    foreach ($mode in @('runtime-only', 'full')) {
        $runtimeOnly = $mode -eq 'runtime-only'
        $consumerRoot = Join-Path $runRoot "consumer-$mode"
        $consumerCache = Join-Path $runRoot "packages-$mode"
        $evidence = Join-Path $runRoot "evidence-$mode"
        New-Item -ItemType Directory -Path $consumerRoot, $consumerCache, $evidence | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'beta-package-consumer/PackageConsumer.csproj'), (Join-Path $PSScriptRoot 'beta-package-consumer/Program.cs') -Destination $consumerRoot
        # Stop repository or user-directory MSBuild/global.json inheritance in this copied project.
        '<Project />' | Set-Content -LiteralPath (Join-Path $consumerRoot 'Directory.Build.props') -Encoding utf8
        '<Project />' | Set-Content -LiteralPath (Join-Path $consumerRoot 'Directory.Build.targets') -Encoding utf8
        Copy-Item -LiteralPath (Join-Path $projectRoot 'global.json') -Destination $consumerRoot
        $escapedSource = [Security.SecurityElement]::Escape($feed)
        $consumerConfig = Join-Path $consumerRoot 'NuGet.Config'
        "<?xml version=`"1.0`" encoding=`"utf-8`"?><configuration><packageSources><clear /><add key=`"this-run-only`" value=`"$escapedSource`" /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders></configuration>" | Set-Content -LiteralPath $consumerConfig -Encoding utf8
        $consumerProject = Join-Path $consumerRoot 'PackageConsumer.csproj'
        $consumerProperties = @("-p:YoUIPackageVersion=$Version", "-p:RuntimeOnly=$($runtimeOnly.ToString().ToLowerInvariant())", '-p:NuGetAudit=false')
        Invoke-Checked 'dotnet' (@('restore', $consumerProject, '--configfile', $consumerConfig, '--packages', $consumerCache) + $consumerProperties)
        $assets = Get-Content -LiteralPath (Join-Path $consumerRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json
        $resolved = @($assets.libraries.PSObject.Properties.Name | Sort-Object)
        $expected = @($(if ($runtimeOnly) { @('YoUI') } else { $packageIds }) | ForEach-Object { "$_/$Version" } | Sort-Object)
        if (@(Compare-Object $resolved $expected).Count -ne 0 -or @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -ne 'package' }).Count -ne 0) { throw "Consumer has an unexpected dependency closure: $($resolved -join ', ')" }
        $sources = @($assets.project.restore.sources.PSObject.Properties.Name)
        if ($sources.Count -ne 1 -or [IO.Path]::GetFullPath($sources[0]) -ne [IO.Path]::GetFullPath($feed)) { throw 'Consumer restored from a source other than this run feed.' }
        $packageFolders = @($assets.packageFolders.PSObject.Properties.Name)
        if ($packageFolders.Count -ne 1 -or [IO.Path]::GetFullPath($packageFolders[0]).TrimEnd('\', '/') -ne [IO.Path]::GetFullPath($consumerCache).TrimEnd('\', '/')) { throw 'Consumer used an unexpected package cache.' }
        Invoke-Checked 'dotnet' (@('build', $consumerProject, '--configuration', $Configuration, '--no-restore', '--disable-build-servers', '-m:1') + $consumerProperties)
        $consumerOutput = Join-Path $consumerRoot "bin/$Configuration/net10.0/win-x64"
        $consumedNative = Join-Path $consumerOutput 'youi_render.dll'
        if ((Get-FileHash -LiteralPath $consumedNative -Algorithm SHA256).Hash -ne $nativeHash) { throw 'Consumer output DLL differs from source and package.' }
        $windowFlag = (!$runtimeOnly -and $VerifyWindow).ToString().ToLowerInvariant()
        Push-Location $consumerOutput
        try { Invoke-Checked 'dotnet' @((Join-Path $consumerOutput 'PackageConsumer.dll'), $evidence, $Version, $Configuration, $nativeHash, $windowFlag) }
        finally { Pop-Location }
        $reportPath = Join-Path $evidence 'validation.json'
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.status -ne 'passed' -or $report.nativeSha256 -ne $nativeHash) { throw 'Consumer reported an inconsistent result.' }
        $manifest.consumers += [ordered]@{ mode = $mode; resolvedPackages = $resolved; packageCache = $consumerCache; restoreSources = $sources; project = $consumerProject; validation = $reportPath; adapter = $report.adapter; backend = $report.backend; nativeWindowVerified = $report.nativeWindowVerified; checks = $report.checks.Count; screenshot = $report.screenshot; screenshotSha256 = $report.screenshotSha256 }
        Save-Manifest
    }
    if ((Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash -ne $nativeHash) { throw 'Native library changed while packaging/validating.' }
    $after = Get-SourceSnapshot
    $manifest.sourceSnapshotAfter = $after.sha256
    $manifest.sourceChangedDuringRun = $after.sha256 -ne $before.sha256
    $after | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'source-after.json') -Encoding utf8
    $manifest.sourceStatusAfter = @(& git status --porcelain=v1)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot record final working-tree state.' }
    $manifest.status = 'passed'
    Write-Host "PASS local Beta packages and isolated consumers: $runRoot"
    Write-Host 'Inspect generated PNGs before claiming visual acceptance. No package was published.'
} catch {
    $manifest.status = 'failed'; $manifest.error = $_.Exception.ToString()
    throw
} finally {
    $manifest.finishedUtc = [DateTime]::UtcNow.ToString('o'); Save-Manifest
    Pop-Location
    Stop-Transcript | Out-Null
}
