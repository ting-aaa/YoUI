param([ValidateSet('Debug','Release')][string]$Configuration = 'Release', [string]$Open = '', [string]$Plugin = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    & "$PSScriptRoot/build.ps1" -Configuration $Configuration
    $editorArgs = @('run', '--project', 'tools/YoUI.Editor', '-c', $Configuration, '--no-build', '--')
    if ($Open) { $editorArgs += @('--open', $Open) }
    if ($Plugin) { $editorArgs += @('--plugin', $Plugin) }
    & dotnet @editorArgs
    if ($LASTEXITCODE -ne 0) { throw 'Editor exited with an error' }
} finally { Pop-Location }
