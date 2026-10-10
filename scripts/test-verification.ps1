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
Push-Location $unrelated
try {
    foreach ($configuration in 'Debug','Release') {
        foreach ($layer in 'Logic','Gpu') {
            Run-Verification $configuration $layer
            $trace = $global:YoUIVerificationCalls -join "`n"
            $expected = if ($layer -eq 'Logic') { 10 } else { 11 }
            Require ($global:YoUIVerificationCalls.Count -eq $expected) "Unexpected $layer command count"
            foreach ($command in $global:YoUIVerificationCalls | Where-Object { $_ -like 'cargo|*' -and $_ -notlike '*<fmt>*' }) {
                Require ($command.Contains("<--target-dir><$repo/target>")) 'Cargo command has the wrong target directory'
                Require ($command.Contains('<--release>') -eq ($configuration -eq 'Release')) 'Rust profile mismatch'
            }
            $managed = @($global:YoUIVerificationCalls | Where-Object { $_ -like 'dotnet|*' })
            $expectedProjects = @('samples/YoUI.Showcase/YoUI.Showcase.csproj','tools/YoUI.Editor/YoUI.Editor.csproj','samples/YoUI.Exporter.Sample/YoUI.Exporter.Sample.csproj','docs/examples/HelloYoUI/HelloYoUI.csproj')
            if ($layer -eq 'Logic') { $expectedProjects += @('tests/YoUI.Tests','tests/YoUI.Editor.Tests') }
            else { $expectedProjects += @('tools/YoUI.Editor','samples/YoUI.Showcase','samples/YoUI.Showcase') }
            Require ($managed.Count -eq $expectedProjects.Count) 'Managed project count mismatch'
            for ($index = 0; $index -lt $managed.Count; $index++) {
                Require ($managed[$index].Contains("<-c><$configuration>")) 'Managed configuration mismatch'
                Require ($managed[$index].Contains('<' + $expectedProjects[$index] + '>')) 'Managed project order mismatch'
                if ($index -lt 4) { Require ($managed[$index].StartsWith('dotnet|<build>')) 'Expected build command' }
                else { Require ($managed[$index].StartsWith('dotnet|<run><--project>')) 'Expected run command' }
            }
            Require ($trace.Contains('<--ignored>') -eq ($layer -eq 'Gpu')) 'GPU test invocation layer mismatch'
            Require (!$trace.Contains('<--smoke>')) 'A headless layer invoked native-window checks'
            Require ((Get-Location).Path -eq $unrelated) 'Working directory was not restored'
            $result = Get-ChildItem (Join-Path $repo 'artifacts/verification') -Filter result.json -Recurse |
                Sort-Object LastWriteTimeUtc | Select-Object -Last 1 | Get-Content -Raw | ConvertFrom-Json
            Require ($result.status -eq 'passed' -and $result.layer -eq $layer -and $result.configuration -eq $configuration) 'Incorrect evidence'
            Write-Host "PASS $configuration $layer commands, profile, evidence and independent working directory"
        }
    }
    foreach ($layer in 'Logic','Gpu') {
        $count = if ($layer -eq 'Logic') { 10 } else { 11 }
        for ($step = 1; $step -le $count; $step++) {
            $global:YoUIVerificationFailAt = $step
            $failed = $false
            try { Run-Verification 'Release' $layer } catch { $failed = $true }
            Require $failed "Accepted failure at $layer command $step"
            Require ($global:YoUIVerificationCalls.Count -eq $step) 'Continued after a failed command'
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
    foreach ($layer in 'Window','All') {
        $failed = $false
        try { Run-Verification 'Release' $layer } catch { $failed = $true }
        if ($windowsHost) {
            Require (!$failed) "$layer orchestration failed"
            $smoke = @($global:YoUIVerificationCalls | Where-Object { $_.Contains('<--smoke>') })
            Require ($smoke.Count -eq 2) "$layer omitted native-window checks"
        } else {
            Require ($failed -and $global:YoUIVerificationCalls.Count -eq 0) "$layer failed to reject non-Windows host"
        }
    }
    $global:YoUIVerificationCalls.Clear(); $failed = $false
    try { & (Join-Path $repo 'scripts/verify.ps1') -Configuration Release } catch { $failed = $true }
    if ($windowsHost) {
        Require (!$failed -and $global:YoUIVerificationCalls.Count -eq 18) 'Default gate is no longer full acceptance'
        for ($step = 1; $step -le 18; $step++) {
            $global:YoUIVerificationFailAt = $step; $failed = $false
            try { Run-Verification Release All } catch { $failed = $true }
            Require ($failed -and $global:YoUIVerificationCalls.Count -eq $step) "Full gate accepted failure at command $step"
        }
    } else {
        Require ($failed -and $global:YoUIVerificationCalls.Count -eq 0) 'Default gate silently became a partial check'
    }
    Write-Host 'PASS argument rejection and Windows-only layer guards'
} finally {
    Pop-Location
    Remove-Item -Recurse -Force $scratch
    Remove-Variable YoUIVerificationCalls,YoUIVerificationFailAt -Scope Global
}
