param([ValidateSet('Debug','Release')][string]$Configuration = 'Debug')
$ErrorActionPreference = 'Stop'
& "$PSScriptRoot/build.ps1" -Configuration $Configuration
$projectRoot = Split-Path $PSScriptRoot -Parent
& "$projectRoot/samples/YoUI.Showcase/bin/$Configuration/net9.0/YoUI.Showcase.exe"
if ($LASTEXITCODE -ne 0) { throw 'Showcase failed' }
