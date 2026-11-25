[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Scenario,
    [string]$UnityEditorPath = $env:UNITY_EDITOR
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$unityProjectPath = Join-Path $repositoryRoot 'unity-shader-project'
$generatedDirectory = Join-Path $unityProjectPath 'Assets\Editor\HeadlessLiquidDiagnostics'
$outputDirectory = Join-Path $repositoryRoot 'obj\liquid-simulation-diagnostics'
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$diagnosticLogPath = Join-Path $outputDirectory "$Scenario-$timestamp.log"
$unityLogPath = Join-Path $outputDirectory "$Scenario-$timestamp-unity.log"

if ([string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    $UnityEditorPath = 'D:\unity\Editor\Unity.exe'
}

if (-not (Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw 'Unity Editor was not found. Set UNITY_EDITOR or pass -UnityEditorPath.'
}

if (Test-Path -LiteralPath $generatedDirectory) {
    throw "Temporary Unity source directory already exists; inspect it before running: $generatedDirectory"
}

New-Item -ItemType Directory -Path $generatedDirectory | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sourceFiles = @(
    'LiquidSimulation.cs',
    'ContainerShapeCollision.cs',
    'LiquidDensityCatalog.cs',
    'SimulationProfiler.cs'
)
$generatedFiles = @()
try {
    foreach ($sourceFile in $sourceFiles) {
        $destination = Join-Path $generatedDirectory (Split-Path -Leaf $sourceFile)
        $source = Get-Content -LiteralPath (Join-Path $repositoryRoot $sourceFile) -Raw
        $source = [regex]::Replace(
            $source,
            '(?m)^namespace RealisticLiquidTransfer;',
            "namespace RealisticLiquidTransfer`r`n{",
            1)
        $source = $source.TrimEnd() + "`r`n}"
        [System.IO.File]::WriteAllText(
            $destination,
            $source,
            [System.Text.UTF8Encoding]::new($false))
        $generatedFiles += $destination
    }

    $runnerDestination = Join-Path $generatedDirectory 'LiquidSimulationDiagnosticsRunner.cs'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Runner.cs') -Destination $runnerDestination -Force
    $generatedFiles += $runnerDestination

    $unityArguments = @(
        '-batchmode',
        '-nographics',
        '-projectPath',
        $unityProjectPath,
        '-logFile',
        $unityLogPath,
        '-executeMethod',
        'RealisticLiquidTransfer.LiquidSimulationDiagnosticsRunner.Run',
        '-scenario',
        $Scenario,
        '-diagnosticLog',
        $diagnosticLogPath,
        '-quit'
    )
    & $UnityEditorPath @unityArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Headless Unity diagnostic runner exited with code $LASTEXITCODE. See $unityLogPath."
    }

    if (-not (Test-Path -LiteralPath $diagnosticLogPath -PathType Leaf)) {
        throw "The scenario did not create its diagnostic log: $diagnosticLogPath"
    }

    Write-Output "Diagnostic log: $diagnosticLogPath"
    Write-Output "Unity editor log: $unityLogPath"
}
finally {
    foreach ($generatedFile in $generatedFiles) {
        if (Test-Path -LiteralPath $generatedFile -PathType Leaf) {
            Remove-Item -LiteralPath $generatedFile -Force
        }
        if (Test-Path -LiteralPath "$generatedFile.meta" -PathType Leaf) {
            Remove-Item -LiteralPath "$generatedFile.meta" -Force
        }
    }
    if (Test-Path -LiteralPath "$generatedDirectory.meta" -PathType Leaf) {
        Remove-Item -LiteralPath "$generatedDirectory.meta" -Force
    }
    if (Test-Path -LiteralPath $generatedDirectory -PathType Container) {
        if (@(Get-ChildItem -LiteralPath $generatedDirectory -Force).Count -eq 0) {
            Remove-Item -LiteralPath $generatedDirectory -Force
        }
    }
}
