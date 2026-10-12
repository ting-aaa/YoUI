param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Debug',
    [ValidateSet('All','Logic','Gpu','Window')][string]$Layer = 'All'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($args.Count -ne 0) { throw "Unexpected arguments: $args" }
$projectRoot = Split-Path $PSScriptRoot -Parent
$onWindows = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
if (($Layer -eq 'Window' -or $Layer -eq 'All') -and !$onWindows) {
    throw 'Full/window acceptance requires a Windows desktop. Use -Layer Logic or -Layer Gpu for narrower checks.'
}
function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed with exit code ${LASTEXITCODE}: $Arguments" }
}
$profile = @()
if ($Configuration -eq 'Release') { $profile = @('--release') }
$cargoOptions = @('--locked', '--target-dir', "$projectRoot/target") + $profile
Push-Location $projectRoot
$evidence = [ordered]@{
    sha = $null
    dirty = $null
    configuration = $Configuration
    layer = $Layer
    platform = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    startedUtc = [DateTime]::UtcNow.ToString('o')
    completedUtc = $null
    status = 'running'
    error = $null
}
$evidenceDirectory = Join-Path $projectRoot ('artifacts/verification/' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $evidence.sha = (& git rev-parse HEAD)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the verification commit' }
    $dirtyPaths = & git status --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Cannot identify working-tree changes' }
    $evidence.dirty = @($dirtyPaths).Count -gt 0
    Write-Host ("BEGIN sha={0} dirty={1} configuration={2} platform={3} layer={4}" -f $evidence.sha,$evidence.dirty,$Configuration,$evidence.platform,$Layer)
    & "$PSScriptRoot/build.ps1" -Configuration $Configuration
    if ($Layer -eq 'Logic' -or $Layer -eq 'All') {
        Invoke-Checked 'cargo' @('fmt','--all','--','--check')
        Invoke-Checked 'cargo' (@('clippy','--workspace','--all-targets') + $cargoOptions + @('--','-D','warnings'))
        Invoke-Checked 'cargo' (@('test','--workspace') + $cargoOptions)
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Tests','-c',$Configuration,'-p:BuildInParallel=false')
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Editor.Tests','-c',$Configuration,'-p:BuildInParallel=false')
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Component.Tests','-c',$Configuration,'-p:BuildInParallel=false')
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Profiling.Tests','-c',$Configuration,'-p:BuildInParallel=false')
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Platform.Linux.Tests','-c',$Configuration,'-p:BuildInParallel=false')
    }
    if ($Layer -eq 'Gpu' -or $Layer -eq 'All') {
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Tests','-c',$Configuration,'-p:BuildInParallel=false','--','--gpu-resource-budget')
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Tests','-c',$Configuration,'-p:BuildInParallel=false','--','--gpu-text-input')
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Tests','-c',$Configuration,'-p:BuildInParallel=false','--','--gpu-glyph-residency')
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Tests','-c',$Configuration,'-p:BuildInParallel=false','--','--gpu-offscreen-trim')
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Tests','-c',$Configuration,'-p:BuildInParallel=false','--','--gpu-renderer-recovery')
        Invoke-Checked 'cargo' (@('test','-p','youi-render','--lib','renderer::recovery_tests::') + $cargoOptions + @('--','--ignored','--nocapture'))
        Invoke-Checked 'cargo' (@('test','-p','youi-render','--test','gpu') + $cargoOptions + @('--','--ignored','--nocapture'))
        Invoke-Checked 'cargo' (@('test','-p','youi-render','--lib','renderer::text_residency_tests::cached_text_pixels_survive_residency_mapping_loss_with_live_image') + $cargoOptions + @('--','--exact','--ignored','--nocapture'))
        Invoke-Checked 'cargo' (@('test','-p','youi-render','--lib','renderer::text_residency_tests::released_image_regions_recover_space_with_live_text_and_images') + $cargoOptions + @('--','--exact','--ignored','--nocapture'))
        Invoke-Checked 'cargo' (@('test','-p','youi-render','--lib','renderer::text_residency_tests::glyph_purge_recovers_text_pressure_with_live_images') + $cargoOptions + @('--','--exact','--ignored','--nocapture'))
        Invoke-Checked 'cargo' (@('test','-p','youi-render','--lib','renderer::text_filter_tests::') + $cargoOptions + @('--','--ignored','--nocapture'))
        Invoke-Checked 'dotnet' @('run','--project','tools/YoUI.Editor','-c',$Configuration,'--no-build','--','--headless')
        Invoke-Checked 'dotnet' @('run','--project','samples/YoUI.Showcase','-c',$Configuration,'--no-build','--','--headless')
        Invoke-Checked 'dotnet' @('run','--project','samples/YoUI.Showcase','-c',$Configuration,'--no-build','--','--gallery','--headless')
        Invoke-Checked 'dotnet' @('run','--project','samples/YoUI.Showcase','-c',$Configuration,'--no-build','--','--benchmark')
        Invoke-Checked 'cargo' (@('run','-p','youi-render','--example','render_scene') + $cargoOptions + @('--','artifacts/render-scene.png'))
        Invoke-Checked 'cargo' (@('run','-p','youi-scene','--example','scene_graph') + $cargoOptions + @('--','artifacts/scene-graph.png'))
        Invoke-Checked 'cargo' (@('run','-p','youi-scene','--example','textured_mesh') + $cargoOptions + @('--','artifacts/textured-mesh.png'))
        Invoke-Checked 'dotnet' @('run','--project','tests/YoUI.Component.Tests','-c',$Configuration,'-p:BuildInParallel=false','--','--gpu')
    }
    if ($Layer -eq 'Window' -or $Layer -eq 'All') {
        Invoke-Checked 'dotnet' @('run','--project','tools/YoUI.Editor','-c',$Configuration,'--no-build','--','--smoke')
        Invoke-Checked 'dotnet' @('run','--project','samples/YoUI.Showcase','-c',$Configuration,'--no-build','--','--smoke')
        Invoke-Checked 'dotnet' @('run','--project','samples/YoUI.Showcase','-c',$Configuration,'--no-build','--','--gallery','--smoke')
    }
    $evidence.status = 'passed'
} catch {
    $evidence.status = 'failed'
    $evidence.error = $_.Exception.Message
    throw
} finally {
    $evidence.completedUtc = [DateTime]::UtcNow.ToString('o')
    try {
        $evidence | ConvertTo-Json | Set-Content (Join-Path $evidenceDirectory 'result.json') -Encoding utf8
    } finally { Pop-Location }
}
Write-Host "PASS layer=$Layer configuration=$Configuration; unselected layers were not run. Evidence: $evidenceDirectory"
