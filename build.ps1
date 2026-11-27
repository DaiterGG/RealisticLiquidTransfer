[CmdletBinding()]
param(
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Casualties Unknown',
    [bool]$EnableDebugLogging = $true,
    [bool]$EnableDebugLaunch = $false,
    [bool]$EnableProfiling = $false,
    [string]$UnityEditorPath = $env:UNITY_EDITOR
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$resolvedGamePath = (Resolve-Path -LiteralPath $GamePath).Path
$projectPath = Join-Path $PSScriptRoot 'RealisticLiquidTransfer.csproj'
$unityProjectPath = Join-Path $PSScriptRoot 'unity-shader-project'
$shaderBundlePath = Join-Path $unityProjectPath 'AssetBundles\liquidshaders.bundle'
$embeddedShaderBundlePath = Join-Path $PSScriptRoot 'resources\liquidshaders.bundle'
$sourceAudioDirectory = Join-Path $PSScriptRoot 'resources\audio'
$nestedAudioDirectory = Join-Path $sourceAudioDirectory 'audio'
$unityAudioDirectory = Join-Path $unityProjectPath 'Assets\Audio'
$unityBuildLogPath = Join-Path $PSScriptRoot 'obj\unity-shader-build.log'
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
$containerSpriteDirectory = Join-Path $PSScriptRoot 'resources\sprites\containers'
$defaultContainerSprite = Join-Path $containerSpriteDirectory 'default.png'
$referenceContainerSprite = Join-Path $containerSpriteDirectory 'coffee.png'

if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    $unityEditorCandidates = @(
        'D:\unity\Editor\Unity.exe',
        (Join-Path $env:ProgramFiles 'Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe')
    )
    $UnityEditorPath = $unityEditorCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($UnityEditorPath) -or -not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw "Unity 2022.3.62f3 Editor was not found. Set UNITY_EDITOR or pass -UnityEditorPath."
}

$UnityEditorPath = (Resolve-Path -LiteralPath $UnityEditorPath).Path

if (Test-Path -LiteralPath $nestedAudioDirectory -PathType Container) {
    $sourceAudioDirectory = $nestedAudioDirectory
}
if (-not (Test-Path -LiteralPath $sourceAudioDirectory -PathType Container)) {
    throw "Container impact audio directory not found: $sourceAudioDirectory"
}

foreach ($requiredFile in @(
    $projectPath,
    (Join-Path $unityProjectPath 'Packages\manifest.json'),
    (Join-Path $unityProjectPath 'ProjectSettings\ProjectVersion.txt'),
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
    $defaultContainerSprite,
    $referenceContainerSprite
)) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw "Required file not found: $requiredFile"
    }
}

New-Item -ItemType Directory -Path (Split-Path -Parent $unityBuildLogPath) -Force | Out-Null
New-Item -ItemType Directory -Path $unityAudioDirectory -Force | Out-Null
Get-ChildItem -LiteralPath $sourceAudioDirectory -Force |
    Copy-Item -Destination $unityAudioDirectory -Recurse -Force

$unityArguments = @(
    '-batchmode',
    '-nographics',
    '-quit',
    '-projectPath', "`"$unityProjectPath`"",
    '-buildTarget', 'StandaloneWindows64',
    '-executeMethod', 'RealisticLiquidTransfer.Editor.BuildLiquidShaderBundle.Build',
    '-logFile', "`"$unityBuildLogPath`""
)
$unityBuildStartedAtUtc = [DateTime]::UtcNow
$unityProcess = Start-Process -FilePath $UnityEditorPath -ArgumentList $unityArguments -Wait -PassThru
$shaderErrors = Select-String -LiteralPath $unityBuildLogPath `
    -Pattern "Shader error in 'RealisticLiquidTransfer/LiquidSurface'"
if ($shaderErrors) {
    throw "Unity reported liquid shader compilation errors. See $unityBuildLogPath."
}

$bundleBuildValidated = Select-String -LiteralPath $unityBuildLogPath `
    -Pattern "Built URP liquid shader and container impact audio bundle with Unity " `
    -Quiet
$unityBuildLogUpdated = (Test-Path -LiteralPath $unityBuildLogPath -PathType Leaf) `
    -and ((Get-Item -LiteralPath $unityBuildLogPath).LastWriteTimeUtc -ge $unityBuildStartedAtUtc)
$shaderBundleExists = Test-Path -LiteralPath $shaderBundlePath -PathType Leaf
$shaderBundleUpdated = $shaderBundleExists `
    -and ((Get-Item -LiteralPath $shaderBundlePath).LastWriteTimeUtc -ge $unityBuildStartedAtUtc)

if ($unityProcess.ExitCode -ne 0) {
    $audioModuleDisabled = Select-String -LiteralPath $unityBuildLogPath `
        -Pattern "'AudioClip' is not supported because the module Audio is disabled in the build." `
        -Quiet

    if ($bundleBuildValidated -and $unityBuildLogUpdated -and $shaderBundleUpdated) {
            Write-Warning "Unity exited with code $($unityProcess.ExitCode) after logging a validated AssetBundle build; using the newly generated bundle."
        Copy-Item -LiteralPath $shaderBundlePath -Destination $embeddedShaderBundlePath -Force
    }
    elseif ($audioModuleDisabled -and (Test-Path -LiteralPath $embeddedShaderBundlePath -PathType Leaf)) {
        Write-Warning "Unity's Audio module is disabled; keeping the existing packaged shader/audio bundle."
    }
    else {
        throw "Unity shader AssetBundle build failed with exit code $($unityProcess.ExitCode). See $unityBuildLogPath."
    }
}
else {
    if (-not $shaderBundleExists) {
        throw "Unity exited successfully but did not produce the shader bundle: $shaderBundlePath"
    }

    Copy-Item -LiteralPath $shaderBundlePath -Destination $embeddedShaderBundlePath -Force
}

$dotnet = Get-Command 'dotnet' -ErrorAction Stop
& $dotnet.Source build $projectPath --configuration Release --nologo `
    "-p:GamePath=$resolvedGamePath" `
    "-p:EnableDebugLogging=$($EnableDebugLogging.ToString().ToLowerInvariant())" `
    "-p:EnableDebugLaunch=$($EnableDebugLaunch.ToString().ToLowerInvariant())" `
    "-p:EnableProfiling=$($EnableProfiling.ToString().ToLowerInvariant())"
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
    Write-Output "Debug logging enabled. Each run creates a timestamped log: $debugLogPattern"
}
if ($EnableProfiling) {
    Write-Output "Simulation stage profiling enabled."
}
