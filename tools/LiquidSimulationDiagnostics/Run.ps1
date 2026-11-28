[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'water-bottle-full-idle',
        'water-bottle-single-drop',
        'fast-drop-collision',
        'container-movement-no-inflow',
        'funnel-wall-single-drop',
        'syringe-full-capacity',
        'syringe-center-bottle-intake',
        'water-bottle-into-minibarrel',
        'water-bottle-source-into-empty-minibarrel',
        'upright-water-bottle-receiver-into-filled-minibarrel',
        'empty-water-bottle-into-filled-minibarrel')]
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
    $unityEditorCandidates = @(
        'D:\unity\Editor\Unity.exe',
        (Join-Path $env:ProgramFiles 'Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe')
    )
    $UnityEditorPath = $unityEditorCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($UnityEditorPath) -or -not (
        Test-Path -LiteralPath $UnityEditorPath -PathType Leaf)) {
    throw 'Unity Editor was not found. Set UNITY_EDITOR or pass -UnityEditorPath.'
}

$unityCli = Get-Command 'unity' -ErrorAction Stop
if (Test-Path -LiteralPath $generatedDirectory) {
    throw "Temporary Unity source directory already exists; inspect it before running: $generatedDirectory"
}

New-Item -ItemType Directory -Path $generatedDirectory | Out-Null
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$sourceFiles = @(
    'LiquidSimulation.cs',
    'LiquidContainerLayout.cs',
    'FunnelGeometry.cs',
    'ContainerShapeCollision.cs',
    'LiquidDensityCatalog.cs',
    'SimulationProfiler.cs',
    'ContainerBoundaryData.cs',
    'Generated\ContainerBoundaries.g.cs'
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
        'run',
        $unityProjectPath,
        '--editor-path',
        $UnityEditorPath,
        '--timeout',
        '300',
        '--log-file',
        $unityLogPath,
        '--no-tail',
        '--',
        '-nographics',
        '-executeMethod',
        'RealisticLiquidTransfer.LiquidSimulationDiagnosticsRunner.Run',
        '-scenario',
        $Scenario,
        '-diagnosticLog',
        $diagnosticLogPath,
        '-repositoryRoot',
        $repositoryRoot
    )
    & $unityCli.Source @unityArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Headless Unity diagnostic runner exited with code $LASTEXITCODE. See diagnostic log $diagnosticLogPath and Unity log $unityLogPath."
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
