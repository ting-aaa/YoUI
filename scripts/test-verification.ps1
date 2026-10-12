# Orchestration tests only. Mocked commands are not build/GPU/window evidence.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$source = $PSScriptRoot
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('youi-verification-' + [Guid]::NewGuid().ToString('N'))
$repo = Join-Path $scratch 'repo with spaces'
$unrelated = Join-Path $scratch 'unrelated'
New-Item -ItemType Directory -Path (Join-Path $repo 'scripts'),$unrelated -Force | Out-Null
Copy-Item (Join-Path $source 'verify.ps1'),(Join-Path $source 'build.ps1') (Join-Path $repo 'scripts')
$global:YoUIVerificationCalls = [Collections.Generic.List[string]]::new()
$global:YoUIVerificationFailAt = 0
function Record-Command([string]$Name, [object[]]$Values) {
    $global:YoUIVerificationCalls.Add($Name + '|' + (($Values | ForEach-Object { '<' + $_ + '>' }) -join ''))
    if ((Get-Location).Path -ne $repo) { throw 'Command ran outside repository root' }
    $global:LASTEXITCODE = 0
    if ($global:YoUIVerificationCalls.Count -eq $global:YoUIVerificationFailAt) { $global:LASTEXITCODE = 23 }
}
function cargo { Record-Command 'cargo' $args }
function dotnet { Record-Command 'dotnet' $args }
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
    foreach ($configuration in 'Debug','Release') {
        foreach ($layer in 'Logic','Gpu') {
            Run-Verification $configuration $layer
            $trace = $global:YoUIVerificationCalls -join "`n"
            $expected = if ($layer -eq 'Logic') { 14 } else { 25 }
            Require ($global:YoUIVerificationCalls.Count -eq $expected) "Unexpected $layer command count"
            foreach ($command in $global:YoUIVerificationCalls | Where-Object { $_ -like 'cargo|*' -and $_ -notlike '*<fmt>*' }) {
                Require ($command.Contains('<--locked>')) 'Cargo command omitted the lockfile guard'
                Require ($command.Contains("<--target-dir><$repo/target>")) 'Cargo command has the wrong target directory'
                Require ($command.Contains('<--release>') -eq ($configuration -eq 'Release')) 'Rust profile mismatch'
            }
            $managed = @($global:YoUIVerificationCalls | Where-Object { $_ -like 'dotnet|*' })
            $expectedProjects = @('samples/YoUI.Showcase/YoUI.Showcase.csproj','tools/YoUI.Editor/YoUI.Editor.csproj','samples/YoUI.Exporter.Sample/YoUI.Exporter.Sample.csproj','docs/examples/HelloYoUI/HelloYoUI.csproj','samples/YoUI.Linux.Sample/YoUI.Linux.Sample.csproj')
            if ($layer -eq 'Logic') { $expectedProjects += @('tests/YoUI.Tests','tests/YoUI.Editor.Tests','tests/YoUI.Component.Tests','tests/YoUI.Profiling.Tests','tests/YoUI.Platform.Linux.Tests') }
            else { $expectedProjects += @('tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tests/YoUI.Tests','tools/YoUI.Editor','samples/YoUI.Showcase','samples/YoUI.Showcase','samples/YoUI.Showcase','tests/YoUI.Component.Tests') }
            Require ($managed.Count -eq $expectedProjects.Count) 'Managed project count mismatch'
            for ($index = 0; $index -lt $managed.Count; $index++) {
                Require ($managed[$index].Contains("<-c><$configuration>")) 'Managed configuration mismatch'
                Require ($managed[$index].Contains('<' + $expectedProjects[$index] + '>')) 'Managed project order mismatch'
                if ($index -lt 5) { Require ($managed[$index].StartsWith('dotnet|<build>')) 'Expected build command' }
                else { Require ($managed[$index].StartsWith('dotnet|<run><--project>')) 'Expected run command' }
            }
            Require ($trace.Contains('<--gpu-resource-budget>') -eq ($layer -eq 'Gpu')) 'Managed resource-budget GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-text-input>') -eq ($layer -eq 'Gpu')) 'Managed text-input GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-glyph-residency>') -eq ($layer -eq 'Gpu')) 'Managed glyph residency GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-offscreen-trim>') -eq ($layer -eq 'Gpu')) 'Managed offscreen trim GPU invocation mismatch'
            Require ($trace.Contains('<--gpu-renderer-recovery>') -eq ($layer -eq 'Gpu')) 'Managed renderer recovery GPU invocation mismatch'
            Require ($trace.Contains('<renderer::recovery_tests::>') -eq ($layer -eq 'Gpu')) 'Native renderer recovery GPU invocation mismatch'
            Require ($trace.Contains('<--gallery><--headless>') -eq ($layer -eq 'Gpu')) 'Gallery headless invocation mismatch'
            Require ($trace.Contains('<--gpu>') -eq ($layer -eq 'Gpu')) 'External component GPU invocation mismatch'
            Require ($trace.Contains('<--ignored>') -eq ($layer -eq 'Gpu')) 'GPU test invocation layer mismatch'
            $residencyTest = 'renderer::text_residency_tests::cached_text_pixels_survive_residency_mapping_loss_with_live_image'
            Require ($trace.Contains("<$residencyTest>") -eq ($layer -eq 'Gpu')) 'Text residency test invocation layer mismatch'
            $reuseTest = 'renderer::text_residency_tests::released_image_regions_recover_space_with_live_text_and_images'
            Require ($trace.Contains("<$reuseTest>") -eq ($layer -eq 'Gpu')) 'Atlas free-region reuse test invocation layer mismatch'
            $purgeTest = 'renderer::text_residency_tests::glyph_purge_recovers_text_pressure_with_live_images'
            Require ($trace.Contains("<$purgeTest>") -eq ($layer -eq 'Gpu')) 'Glyph purge test invocation layer mismatch'
            if ($layer -eq 'Gpu') {
                $cargoOptions = "<--locked><--target-dir><$repo/target>"
                if ($configuration -eq 'Release') { $cargoOptions += '<--release>' }
                Require ($global:YoUIVerificationCalls[9] -eq "dotnet|<run><--project><tests/YoUI.Tests><-c><$configuration><-p:BuildInParallel=false><--><--gpu-offscreen-trim>") 'Offscreen trim command must precede renderer recovery'
                Require ($global:YoUIVerificationCalls[10] -eq "dotnet|<run><--project><tests/YoUI.Tests><-c><$configuration><-p:BuildInParallel=false><--><--gpu-renderer-recovery>") 'Managed renderer recovery GPU command mismatch'
                Require ($global:YoUIVerificationCalls[11] -eq "cargo|<test><-p><youi-render><--lib><renderer::recovery_tests::>$cargoOptions<--><--ignored><--nocapture>") 'Native renderer recovery GPU command mismatch'
                Require ($global:YoUIVerificationCalls[12] -eq "cargo|<test><-p><youi-render><--test><gpu>$cargoOptions<--><--ignored><--nocapture>") 'GPU integration test command mismatch'
                Require ($global:YoUIVerificationCalls[13] -eq "cargo|<test><-p><youi-render><--lib><$residencyTest>$cargoOptions<--><--exact><--ignored><--nocapture>") 'Text residency GPU unit test command mismatch'
                Require ($global:YoUIVerificationCalls[14] -eq "cargo|<test><-p><youi-render><--lib><$reuseTest>$cargoOptions<--><--exact><--ignored><--nocapture>") 'Atlas free-region reuse GPU unit test command mismatch'
                Require ($global:YoUIVerificationCalls[15] -eq "cargo|<test><-p><youi-render><--lib><$purgeTest>$cargoOptions<--><--exact><--ignored><--nocapture>") 'Glyph purge GPU unit test command mismatch'
                Require ($global:YoUIVerificationCalls[16] -eq "cargo|<test><-p><youi-render><--lib><renderer::text_filter_tests::>$cargoOptions<--><--ignored><--nocapture>") 'Text filter GPU unit test command mismatch'
                Require ($global:YoUIVerificationCalls[23] -eq "cargo|<run><-p><youi-scene><--example><textured_mesh>$cargoOptions<--><artifacts/textured-mesh.png>") 'Textured mesh example command mismatch'
            }
            Require (!$trace.Contains('<--smoke>')) 'A headless layer invoked native-window checks'
            Require ((Get-Location).Path -eq $unrelated) 'Working directory was not restored'
            $result = Get-ChildItem (Join-Path $repo 'artifacts/verification') -Filter result.json -Recurse |
                Sort-Object LastWriteTimeUtc | Select-Object -Last 1 | Get-Content -Raw | ConvertFrom-Json
            Require ($result.status -eq 'passed' -and $result.layer -eq $layer -and $result.configuration -eq $configuration) 'Incorrect evidence'
            if ($configuration -eq 'Release') { $releaseCommands[$layer] = @($global:YoUIVerificationCalls) }
            Write-Host "PASS $configuration $layer commands, profile, evidence and independent working directory"
        }
    }
    foreach ($layer in 'Logic','Gpu') {
        $count = if ($layer -eq 'Logic') { 14 } else { 25 }
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
    $smokeCommands = @(
        'dotnet|<run><--project><tools/YoUI.Editor><-c><Release><--no-build><--><--smoke>',
        'dotnet|<run><--project><samples/YoUI.Showcase><-c><Release><--no-build><--><--smoke>',
        'dotnet|<run><--project><samples/YoUI.Showcase><-c><Release><--no-build><--><--gallery><--smoke>'
    )
    $expectedWindow = @($buildCommands) + $smokeCommands
    $expectedAll = @($releaseCommands.Logic) + @($releaseCommands.Gpu | Select-Object -Skip $buildCommands.Count) + $smokeCommands
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
        $count = if ($layer -eq 'All') { $expectedAll.Count } else { $expectedWindow.Count }
        for ($step = 1; $step -le $count; $step++) {
            $global:YoUIVerificationFailAt = $step; $failed = $false
            try { Run-Verification Release $layer } catch { $failed = $true }
            Require ($failed -and $global:YoUIVerificationCalls.Count -eq $step) "$layer gate accepted failure at command $step"
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
    Remove-Item -Recurse -Force $scratch
    Remove-Variable YoUIVerificationCalls,YoUIVerificationFailAt -Scope Global
}
