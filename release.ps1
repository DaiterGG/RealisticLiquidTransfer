[CmdletBinding()]
param(
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Casualties Unknown'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$buildScript = Join-Path $PSScriptRoot 'build.ps1'
& $buildScript -GamePath $GamePath -EnableDebugLogging:$false
