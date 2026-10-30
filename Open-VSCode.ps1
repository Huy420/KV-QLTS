$ErrorActionPreference = 'Stop'
$sdk = Join-Path $PSScriptRoot '.tools\dotnet'
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'dotnet.exe'))) { throw 'Local SDK missing. Install .NET SDK 10 first.' }
$env:DOTNET_ROOT = $sdk
$env:PATH = $sdk + ';' + $env:PATH
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools\cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools\packages'
$code = Get-Command code -ErrorAction SilentlyContinue
if (-not $code) { throw 'VS Code command code was not found on PATH.' }
& $code.Source --new-window $PSScriptRoot
Write-Host 'VS Code opened. This script does not start the web application.'
Write-Host 'To start the application: powershell -ExecutionPolicy Bypass -File .\Start.ps1'
