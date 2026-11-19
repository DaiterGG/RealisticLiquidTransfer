[CmdletBinding()]
param(
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Casualties Unknown',
    [bool]$EnableDebugLogging = $true
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedGamePath = (Resolve-Path -LiteralPath $GamePath).Path
$projectPath = Join-Path $PSScriptRoot 'RealisticLiquidTransfer.csproj'
$gameAssembly = Join-Path $resolvedGamePath 'CasualtiesUnknown_Data\Managed\Assembly-CSharp.dll'
$pluginAssembly = Join-Path $resolvedGamePath 'BepInEx\core\BepInEx.dll'
$harmonyAssembly = Join-Path $resolvedGamePath 'BepInEx\core\0Harmony.dll'
$unityAssembly = Join-Path $resolvedGamePath 'CasualtiesUnknown_Data\Managed\UnityEngine.dll'
$unityCoreAssembly = Join-Path $resolvedGamePath 'CasualtiesUnknown_Data\Managed\UnityEngine.CoreModule.dll'
$unityInputAssembly = Join-Path $resolvedGamePath 'CasualtiesUnknown_Data\Managed\UnityEngine.InputLegacyModule.dll'
$unityImageAssembly = Join-Path $resolvedGamePath 'CasualtiesUnknown_Data\Managed\UnityEngine.ImageConversionModule.dll'
$unityUiAssembly = Join-Path $resolvedGamePath 'CasualtiesUnknown_Data\Managed\UnityEngine.UI.dll'
$unityUiModuleAssembly = Join-Path $resolvedGamePath 'CasualtiesUnknown_Data\Managed\UnityEngine.UIModule.dll'
$unityTextRenderingAssembly = Join-Path $resolvedGamePath 'CasualtiesUnknown_Data\Managed\UnityEngine.TextRenderingModule.dll'
$receivingContainerSprite = Join-Path $PSScriptRoot 'resources\coffe cup.png'
$sourceContainerSprite = Join-Path $PSScriptRoot 'resources\big bottle.png'

foreach ($requiredFile in @(
    $projectPath,
    $gameAssembly,
    $pluginAssembly,
    $harmonyAssembly,
    $unityAssembly,
    $unityCoreAssembly,
    $unityInputAssembly,
    $unityImageAssembly,
    $unityUiAssembly,
    $unityUiModuleAssembly,
    $unityTextRenderingAssembly,
    $receivingContainerSprite,
    $sourceContainerSprite
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required file not found: $requiredFile"
    }
}

$dotnet = Get-Command 'dotnet' -ErrorAction Stop
& $dotnet.Source build $projectPath --configuration Release --nologo `
    "-p:GamePath=$resolvedGamePath" `
    "-p:EnableDebugLogging=$($EnableDebugLogging.ToString().ToLowerInvariant())"
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

$builtDll = Join-Path $PSScriptRoot 'bin\Release\net48\RealisticLiquidTransfer.dll'
if (-not (Test-Path -LiteralPath $builtDll -PathType Leaf)) {
    throw "Build succeeded but the expected DLL was not produced: $builtDll"
}

$pluginsDirectory = Join-Path $resolvedGamePath 'BepInEx\plugins'
New-Item -ItemType Directory -Path $pluginsDirectory -Force | Out-Null
$destination = Join-Path $pluginsDirectory 'RealisticLiquidTransfer.dll'
Copy-Item -LiteralPath $builtDll -Destination $destination -Force

Write-Output "Built and installed: $destination"
if ($EnableDebugLogging) {
    $debugLogPattern = Join-Path $resolvedGamePath 'BepInEx\LogOutput\RealisticLiquidTransfer-debug-*.log'
    Write-Output "Runtime profiling enabled. Each run creates a timestamped log: $debugLogPattern"
}
