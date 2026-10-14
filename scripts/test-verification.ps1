# Orchestration tests only. Mocked commands are not build/GPU/window evidence.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = $PSScriptRoot
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('youi-verification-' + [Guid]::NewGuid().ToString('N'))
$repo = Join-Path $scratch 'repo with spaces'
$unrelated = Join-Path $scratch 'unrelated'
New-Item -ItemType Directory -Path (Join-Path $repo 'scripts'),$unrelated -Force | Out-Null
Copy-Item (Join-Path $source 'verify.ps1'),(Join-Path $source 'build.ps1'),(Join-Path $source 'sync-native.ps1') (Join-Path $repo 'scripts')
$nativeName = if ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) { 'youi_render.dll' }
    elseif ([Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Linux)) { 'libyoui_render.so' }
    else { 'libyoui_render.dylib' }
$nativeProjects = @('samples/YoUI.Showcase','tools/YoUI.Editor','docs/examples/HelloYoUI',
    'samples/YoUI.Linux.Sample','tests/YoUI.Tests','tests/YoUI.Component.Tests','tests/YoUI.Platform.Windows.Tests')
foreach ($configuration in 'Debug','Release') {
    $nativeDirectory = Join-Path $repo ('target/' + $configuration.ToLowerInvariant())
    New-Item -ItemType Directory -Path $nativeDirectory -Force | Out-Null
    Set-Content (Join-Path $nativeDirectory $nativeName) "synthetic $configuration native; never loaded"
    foreach ($project in $nativeProjects) {
        $directory = Join-Path $repo "$project/bin/$configuration/net10.0"
        $name = Split-Path $project -Leaf
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        Set-Content (Join-Path $directory "$name.deps.json") '{}'
        Set-Content (Join-Path $directory "$name.dll") 'synthetic managed assembly; never loaded'
        Set-Content (Join-Path $directory $nativeName) 'stale synthetic native copy'
    }
}
$global:YoUIVerificationCalls = [Collections.Generic.List[string]]::new()
$global:YoUIVerificationFailAt = 0
function Record-Command([string]$Name, [object[]]$Values) {
    $global:YoUIVerificationCalls.Add($Name + '|' + (($Values | ForEach-Object { '<' + $_ + '>' }) -join ''))
    if ((Get-Location).Path -ne $repo) { throw 'Command ran outside repository root' }
    $global:LASTEXITCODE = 0
    if ($global:YoUIVerificationCalls.Count -eq $global:YoUIVerificationFailAt) { $global:LASTEXITCODE = 23 }
}
function cargo {
    Record-Command 'cargo' $args
    if ($LASTEXITCODE -eq 0 -and $args[0] -in 'build','test','run','clippy') {
        $profile = if ($args -contains '--release') { 'release' } else { 'debug' }
        # Model Cargo relinking the cdylib after managed outputs were built.
        Set-Content (Join-Path $repo "target/$profile/$nativeName") ("synthetic relink " + [Guid]::NewGuid())
    }
}
function dotnet {
    Record-Command 'dotnet' $args
    if ($args[0] -eq 'run') {
        $configuration = $args[[Array]::IndexOf($args, '-c') + 1]
        $expectedHash = (Get-FileHash (Join-Path $repo ('target/' + $configuration.ToLowerInvariant() + '/' + $nativeName))).Hash
        foreach ($project in $nativeProjects) {
            $path = Join-Path $repo "$project/bin/$configuration/net10.0/$nativeName"
            if ((Get-FileHash $path).Hash -ne $expectedHash) { throw "Managed consumption used a stale native binary: $path" }
        }
    }
}
function git {
    $global:LASTEXITCODE = 0
    if (($args -join ' ') -eq 'rev-parse HEAD') { '0123456789abcdef0123456789abcdef01234567' }
}
function Require([bool]$Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Run-Verification([string]$Configuration, [string]$Layer) {
    $global:YoUIVerificationCalls.Clear()
    & (Join-Path $repo 'scripts/verify.ps1') -Configuration $Configuration -Layer $Layer
}
$releaseCommands = @{}
Push-Location $unrelated
try {
    $unrelatedNative = Join-Path $unrelated $nativeName
    Set-Content $unrelatedNative 'unrelated binary'
    $debugConsumer = Join-Path $repo "samples/YoUI.Showcase/bin/Debug/net10.0/$nativeName"
    $debugHash = (Get-FileHash $debugConsumer).Hash
    $snapshot = & (Join-Path $repo 'scripts/sync-native.ps1') -Configuration Release -Stage 'fixture-sync'
    Require ($snapshot.profile -eq 'release' -and $snapshot.consumers.Count -eq 7) 'Native helper selected wrong profile or consumers'
    foreach ($consumer in $snapshot.consumers) {
        Require ($consumer.updated -and $consumer.binaryHash -eq $snapshot.sourceBinaryHash) 'Native helper failed to replace stale consumer'
    }
    Require ((Get-FileHash $debugConsumer).Hash -eq $debugHash) 'Release synchronization modified Debug outputs'
    Require ((Get-Content $unrelatedNative) -eq 'unrelated binary') 'Native helper modified unrelated output'
    $second = & (Join-Path $repo 'scripts/sync-native.ps1') -Configuration Release
    Require (@($second.consumers | Where-Object updated).Count -eq 0) 'Identical native binaries were unnecessarily copied'
    $nativeSource = Join-Path $repo "target/release/$nativeName"
    $sourceContents = [IO.File]::ReadAllBytes($nativeSource)
    Remove-Item -LiteralPath $nativeSource
    $failed = $false
    try { & (Join-Path $repo 'scripts/sync-native.ps1') -Configuration Release } catch { $failed = $_.Exception.Message.Contains('Missing native library:') }
    Require $failed 'Native helper silently accepted missing source'
    [IO.File]::WriteAllBytes($nativeSource, $sourceContents)
    Write-Host 'PASS native helper profile, stale-copy replacement, hash equality, output scope and missing-source failure (synthetic files only)'
    foreach ($configuration in 'Debug','Release') {
        foreach ($layer in 'Logic','Gpu') {
            Run-Verification $configuration $layer
            $trace = $global:YoUIVerificationCalls -join "`n"
            $expected = if ($layer -eq 'Logic') { 15 } else { 30 }
            Require ($global:YoUIVerificationCalls.Count -eq $expected) "Unexpected $layer command count"
            foreach ($command in $global:YoUIVerificationCalls | Where-Object { $_ -like 'cargo|*' -and $_ -notlike '*<fmt>*' }) {
                Require ($command.Contains('<--locked>')) 'Cargo command omitted the lockfile guard'
                Require ($command.Contains("<--target-dir><$repo/target>")) 'Cargo command has the wrong target directory'
                Require ($command.Contains('<--release>') -eq ($configuration -eq 'Release')) 'Rust profile mismatch'
            }
            $managed = @($global:YoUIVerificationCalls | Where-Object { $_ -like 'dotnet|*' })
            $expectedProjects = @('samples/YoUI.Showcase/YoUI.Showcase.csproj','tools/YoUI.Editor/YoUI.Editor.csproj','samples/YoUI.Exporter.Sample/YoUI.Exporter.Sample.csproj','docs/examples/HelloYoUI/HelloYoUI.csproj','samples/YoUI.Linux.Sample/YoUI.Linux.Sample.csproj')
            if ($layer -eq 'Logic') { $expectedProjects += @('tests/YoUI.Tests','tests/YoUI.Editor.Tests','tests/YoUI.Component.Tests','tests/YoUI.Profiling.Tests','tests/YoUI.Platform.Linux.Tests','tests/YoUI.Platform.Windows.Tests') }
            else { $expectedProjects += @('tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tools/YoUI.Editor','samples/YoUI.Showcase','samples/YoUI.Showcase','samples/YoUI.Showcase','tests/YoUI.Component.Tests') }
            Require ($managed.Count -eq $expectedProjects.Count) 'Managed project count mismatch'
            for ($index = 0; $index -lt $managed.Count; $index++) {
                Require ($managed[$index].Contains("<-c><$configuration>")) 'Managed configuration mismatch'
                Require ($managed[$index].Contains('<' + $expectedProjects[$index] + '>')) 'Managed project order mismatch'
                if ($index -lt 5) { Require ($managed[$index].StartsWith('dotnet|<build>')) 'Expected build command' }
                else { Require ($managed[$index].StartsWith('dotnet|<run><--project>')) 'Expected run command' }
            }
            Require ($trace.Contains('<--gpu-resource-budget>') -eq ($layer -eq 'Gpu')) 'Managed resource-budget GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-renderer-capabilities>') -eq ($layer -eq 'Gpu')) 'Managed renderer capabilities GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-text-input>') -eq ($layer -eq 'Gpu')) 'Managed text-input GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-glyph-residency>') -eq ($layer -eq 'Gpu')) 'Managed glyph residency GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-offscreen-trim>') -eq ($layer -eq 'Gpu')) 'Managed offscreen trim GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-renderer-recovery>') -eq ($layer -eq 'Gpu')) 'Managed renderer recovery GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-presentation-retry>') -eq ($layer -eq 'Gpu')) 'Managed presentation retry GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-gallery-rendering>') -eq ($layer -eq 'Gpu')) 'Managed gallery rendering GPU invocation mismatch'
            Require ($trace.Contains('<renderer::presentation_tests::>') -eq ($layer -eq 'Gpu')) 'Native presentation GPU invocation mismatch'
            Require ($trace.Contains('<renderer::recovery_tests::>') -eq ($layer -eq 'Gpu')) 'Native renderer recovery GPU invocation mismatch'
            Require ($trace.Contains('<--test><image_alpha>') -eq ($layer -eq 'Gpu')) 'Scene image alpha GPU invocation mismatch'
            Require ($trace.Contains('<--gallery><--headless>') -eq ($layer -eq 'Gpu')) 'Gallery headless invocation mismatch'
            Require ($trace.Contains('<--gpu>') -eq ($layer -eq 'Gpu')) 'External component GPU invocation mismatch'
            Require ($trace.Contains('<--ignored>') -eq ($layer -eq 'Gpu')) 'GPU test invocation layer mismatch'
            foreach ($command in $global:YoUIVerificationCalls | Where-Object { $_ -like 'cargo|*<--ignored>*' }) {
                Require ($command.Contains('<--test-threads=1>')) 'GPU tests must serialize adapter/device initialization'
            }
            $residencyTest = 'renderer::text_residency_tests::cached_text_pixels_survive_residency_mapping_loss_with_live_image'
            Require ($trace.Contains("<$residencyTest>") -eq ($layer -eq 'Gpu')) 'Text residency test invocation layer mismatch'
            $reuseTest = 'renderer::text_residency_tests::released_image_regions_recover_space_with_live_text_and_images'
            Require ($trace.Contains("<$reuseTest>") -eq ($layer -eq 'Gpu')) 'Atlas free-region reuse test invocation layer mismatch'
            $purgeTest = 'renderer::text_residency_tests::glyph_purge_recovers_text_pressure_with_live_images'
            Require ($trace.Contains("<$purgeTest>") -eq ($layer -eq 'Gpu')) 'Glyph purge test invocation layer mismatch'
            if ($layer -eq 'Logic') {
                Require ($global:YoUIVerificationCalls[14] -eq "dotnet|<run><--project><tests/YoUI.Platform.Windows.Tests><-c><$configuration><-p:BuildInParallel=false>") 'Windows mouse-leave logic must follow Linux platform logic without native-window arguments'
            } else {
                $cargoOptions = "<--locked><--target-dir><$repo/target>"
                if ($configuration -eq 'Release') { $cargoOptions += '<--release>' }
                Require ($global:YoUIVerificationCalls[7] -eq "dotnet|<run><--project><tests/YoUI.Tests><-c><$configuration><-p:BuildInParallel=false><--><--gpu-renderer-capabilities>") 'Renderer capabilities must follow resource-budget checks in the existing GPU layer'
                Require ($global:YoUIVerificationCalls[10] -eq "dotnet|<run><--project><tests/YoUI.Tests><-c><$configuration><-p:BuildInParallel=false><--><--gpu-offscreen-trim>") 'Offscreen trim command must precede renderer recovery'
                Require ($global:YoUIVerificationCalls[11] -eq "dotnet|<run><--project><tests/YoUI.Tests><-c><$configuration><-p:BuildInParallel=false><--><--gpu-renderer-recovery>") 'Managed renderer recovery GPU command mismatch'
                Require ($global:YoUIVerificationCalls[12] -eq "dotnet|<run><--project><tests/YoUI.Tests><-c><$configuration><-p:BuildInParallel=false><--><--gpu-presentation-retry>") 'Managed presentation retry must follow renderer recovery'
                Require ($global:YoUIVerificationCalls[13] -eq "dotnet|<run><--project><tests/YoUI.Tests><-c><$configuration><-p:BuildInParallel=false><--><--gpu-gallery-rendering>") 'Gallery rendering must follow presentation retry before native GPU checks'
                Require ($global:YoUIVerificationCalls[14] -eq "cargo|<test><-p><youi-render><--lib><renderer::presentation_tests::>$cargoOptions<--><--ignored><--nocapture><--test-threads=1>") 'Native presentation GPU command must precede native recovery'
                Require ($global:YoUIVerificationCalls[15] -eq "cargo|<test><-p><youi-render><--lib><renderer::recovery_tests::>$cargoOptions<--><--ignored><--nocapture><--test-threads=1>") 'Native renderer recovery GPU command mismatch'
                Require ($global:YoUIVerificationCalls[16] -eq "cargo|<test><-p><youi-render><--test><gpu>$cargoOptions<--><--ignored><--nocapture><--test-threads=1>") 'GPU integration test command mismatch'
                Require ($global:YoUIVerificationCalls[17] -eq "cargo|<test><-p><youi-scene><--test><image_alpha>$cargoOptions<--><--ignored><--nocapture><--test-threads=1>") 'Scene image alpha GPU command must precede text residency checks'
                Require ($global:YoUIVerificationCalls[18] -eq "cargo|<test><-p><youi-render><--lib><$residencyTest>$cargoOptions<--><--exact><--ignored><--nocapture><--test-threads=1>") 'Text residency GPU unit test command mismatch'
                Require ($global:YoUIVerificationCalls[19] -eq "cargo|<test><-p><youi-render><--lib><$reuseTest>$cargoOptions<--><--exact><--ignored><--nocapture><--test-threads=1>") 'Atlas free-region reuse GPU unit test command mismatch'
                Require ($global:YoUIVerificationCalls[20] -eq "cargo|<test><-p><youi-render><--lib><$purgeTest>$cargoOptions<--><--exact><--ignored><--nocapture><--test-threads=1>") 'Glyph purge GPU unit test command mismatch'
                Require ($global:YoUIVerificationCalls[21] -eq "cargo|<test><-p><youi-render><--lib><renderer::text_filter_tests::>$cargoOptions<--><--ignored><--nocapture><--test-threads=1>") 'Text filter GPU unit test command mismatch'
                Require ($global:YoUIVerificationCalls[28] -eq "cargo|<run><-p><youi-scene><--example><textured_mesh>$cargoOptions<--><artifacts/textured-mesh.png>") 'Textured mesh example command mismatch'
            }
            Require (!$trace.Contains('<--smoke>')) 'A headless layer invoked native-window checks'
            Require (!$trace.Contains('<--native-mouse-leave>')) 'A headless layer invoked native mouse-leave checks'
            Require (!$trace.Contains('<--native-lifecycle>')) 'A headless layer invoked native lifecycle checks'
            Require ((Get-Location).Path -eq $unrelated) 'Working directory was not restored'
            $result = Get-ChildItem (Join-Path $repo 'artifacts/verification') -Filter result.json -Recurse |
                Sort-Object LastWriteTimeUtc | Select-Object -Last 1 | Get-Content -Raw | ConvertFrom-Json
            Require ($result.status -eq 'passed' -and $result.layer -eq $layer -and $result.configuration -eq $configuration) 'Incorrect evidence'
            Require ($result.nativeLibraries.Count -gt 0) 'Native binary provenance is missing'
            foreach ($snapshot in $result.nativeLibraries) {
                Require ($snapshot.configuration -eq $configuration -and $snapshot.profile -eq $configuration.ToLowerInvariant()) 'Native snapshot profile mismatch'
                Require ($snapshot.consumers.Count -eq $nativeProjects.Count) 'Native snapshot did not cover existing consumers'
                foreach ($consumer in $snapshot.consumers) { Require ($consumer.binaryHash -eq $snapshot.sourceBinaryHash) 'Native consumption hash mismatch' }
            }
            Require ($result.nativeLibraries[-1].stage -eq "$layer-complete") 'Layer completion omitted native consistency check'
            if ($configuration -eq 'Release') { $releaseCommands[$layer] = @($global:YoUIVerificationCalls) }
            Write-Host "PASS $configuration $layer commands, profile, evidence and independent working directory"
        }
    }
    foreach ($layer in 'Logic','Gpu') {
        $count = $releaseCommands[$layer].Count
        for ($step = 1; $step -le $count; $step++) {
            $global:YoUIVerificationFailAt = $step
            $failed = $false
            try { Run-Verification 'Release' $layer } catch { $failed = $true }
            Require $failed "Accepted failure at $layer command $step"
            Require ($global:YoUIVerificationCalls.Count -eq $step) 'Continued after a failed command'
            Require ($global:YoUIVerificationCalls[$step - 1] -eq $releaseCommands[$layer][$step - 1]) 'Failure was injected into an unexpected command'
            Require ((Get-Location).Path -eq $unrelated) 'Failed command leaked working directory'
            $failedResult = Get-ChildItem (Join-Path $repo 'artifacts/verification') -Filter result.json -Recurse |
                Sort-Object LastWriteTimeUtc | Select-Object -Last 1 | Get-Content -Raw | ConvertFrom-Json
            Require ($failedResult.status -eq 'failed' -and $failedResult.error) 'Failure evidence was not recorded'
            Write-Host "PASS $layer propagates failure at command $step"
        }
    }
    $global:YoUIVerificationFailAt = 0
    foreach ($parameters in @(@{Configuration='invalid'; Layer='Logic'}, @{Configuration='Debug'; Layer='invalid'}, @{Configuration='Debug'; Layer='Logic'; Unexpected='bad'})) {
        $global:YoUIVerificationCalls.Clear(); $failed = $false
        try { & (Join-Path $repo 'scripts/verify.ps1') @parameters } catch { $failed = $true }
        Require ($failed -and $global:YoUIVerificationCalls.Count -eq 0) 'Invalid arguments started verification'
    }
    $windowsHost = [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)
    if (!$windowsHost) {
        foreach ($layer in 'Window','All') {
            $failed = $false
            try { Run-Verification 'Release' $layer } catch { $failed = $true }
            Require ($failed -and $global:YoUIVerificationCalls.Count -eq 0) "$layer failed to reject non-Windows host"
        }
        $global:YoUIVerificationCalls.Clear(); $failed = $false
        try { & (Join-Path $repo 'scripts/verify.ps1') -Configuration Release } catch { $failed = $true }
        Require ($failed -and $global:YoUIVerificationCalls.Count -eq 0) 'Default gate silently became a partial check'

        # Exercise Windows orchestration on every host, only in the disposable mocked copy.
        # The real verifier and its non-Windows rejection above remain untouched.
        $copy = Join-Path $repo 'scripts/verify.ps1'
        $contents = Get-Content -Raw $copy
        $platformLine = '$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)'
        Require ($contents.Split(@($platformLine), [StringSplitOptions]::None).Count -eq 2) 'Cannot locate the unique platform probe for simulation'
        $contents.Replace($platformLine, '$onWindows = $true # Test-only Windows orchestration simulation') | Set-Content $copy
        Write-Host 'Testing simulated Windows orchestration with mocked commands; not native-window evidence.'
    }

    # Derive full acceptance from independently checked phases, not another stale total.
    $buildCommands = @($releaseCommands.Logic | Select-Object -First 6)
    Require (($buildCommands -join "`n") -eq (($releaseCommands.Gpu | Select-Object -First 6) -join "`n")) 'Logic and Gpu must share the same build prelude'
    $windowCommands = @(
        'dotnet|<run><--project><tests/YoUI.Platform.Windows.Tests><-c><Release><-p:BuildInParallel=false><--><--native-mouse-leave>',
        'dotnet|<run><--project><tests/YoUI.Platform.Windows.Tests><-c><Release><-p:BuildInParallel=false><--><--native-lifecycle>',
        'dotnet|<run><--project><tools/YoUI.Editor><-c><Release><--no-build><--><--smoke>',
        'dotnet|<run><--project><samples/YoUI.Showcase><-c><Release><--no-build><--><--smoke>',
        'dotnet|<run><--project><samples/YoUI.Showcase><-c><Release><--no-build><--><--gallery><--smoke>'
    )
    $expectedWindow = @($buildCommands) + $windowCommands
    $expectedAll = @($releaseCommands.Logic) + @($releaseCommands.Gpu | Select-Object -Skip $buildCommands.Count) + $windowCommands
    foreach ($layer in 'Window','All') {
        Run-Verification Release $layer
        $expectedCommands = if ($layer -eq 'All') { $expectedAll } else { $expectedWindow }
        Require (($global:YoUIVerificationCalls -join "`n") -eq ($expectedCommands -join "`n")) "$layer command sequence differs from the verified phases"
        Require ((Get-Location).Path -eq $unrelated) "$layer leaked working directory"
    }
    $global:YoUIVerificationCalls.Clear()
    & (Join-Path $repo 'scripts/verify.ps1') -Configuration Release
    Require (($global:YoUIVerificationCalls -join "`n") -eq ($expectedAll -join "`n")) 'Default gate is no longer full acceptance'
    foreach ($layer in 'Window','All') {
        $expectedCommands = if ($layer -eq 'All') { $expectedAll } else { $expectedWindow }
        $count = $expectedCommands.Count
        for ($step = 1; $step -le $count; $step++) {
            $global:YoUIVerificationFailAt = $step; $failed = $false
            try { Run-Verification Release $layer } catch { $failed = $true }
            Require ($failed -and $global:YoUIVerificationCalls.Count -eq $step) "$layer gate accepted failure at command $step"
            Require (($global:YoUIVerificationCalls -join "`n") -eq (($expectedCommands | Select-Object -First $step) -join "`n")) "$layer failure was injected into an unexpected command sequence"
            Require ((Get-Location).Path -eq $unrelated) "$layer failure leaked working directory"
            $result = Get-ChildItem (Join-Path $repo 'artifacts/verification') -Filter result.json -Recurse |
                Sort-Object LastWriteTimeUtc | Select-Object -Last 1 | Get-Content -Raw | ConvertFrom-Json
            Require ($result.status -eq 'failed' -and $result.error -and $result.layer -eq $layer) "$layer failure evidence was not recorded"
            Write-Host "PASS $layer propagates failure at command $step of $count"
        }
    }
    Write-Host 'PASS argument rejection and Windows-only layer guards'
} finally {
    Pop-Location
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (!$resolvedScratch.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or !(Split-Path $resolvedScratch -Leaf).StartsWith('youi-verification-')) { throw 'Unsafe scratch cleanup path' }
    Remove-Item -LiteralPath $resolvedScratch -Recurse -Force
    Remove-Variable YoUIVerificationCalls,YoUIVerificationFailAt -Scope Global
}
