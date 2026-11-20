[CmdletBinding()]
param(
    [string]$CsvPath,
    [string]$OutputPath,
    [string]$ModIdentifier = 'RealisticLiquidTransfer',
    [ValidateRange(1, 100)]
    [int]$TopCount = 15
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$defaultGamePath = 'D:\SteamLibrary\steamapps\common\Casualties Unknown'
if ([string]::IsNullOrWhiteSpace($CsvPath)) {
    $inputFile = Get-ChildItem -LiteralPath $defaultGamePath -File -Filter 'MonoProfilerOutput*.csv' |
        Where-Object { $_.Name -match '^MonoProfilerOutput_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.csv$' } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
    if (-not $inputFile) {
        throw "No MonoProfilerOutput*.csv file found in '$defaultGamePath'. Pass -CsvPath to select a capture."
    }

    $resolvedCsvPath = $inputFile.FullName
}
else {
    $resolvedCsvPath = (Resolve-Path -LiteralPath $CsvPath -ErrorAction Stop).Path
    if (Test-Path -LiteralPath $resolvedCsvPath -PathType Container) {
        throw "CsvPath must name a profiler CSV file, not a directory: $resolvedCsvPath"
    }
}

$rows = @(Import-Csv -LiteralPath $resolvedCsvPath)
if ($rows.Count -eq 0) {
    throw "Profiler CSV is empty: $resolvedCsvPath"
}

$requiredColumns = @(
    'Thread',
    'Call count',
    'Method name',
    'Total runtime (ns)',
    'Self runtime (ns)',
    'Total allocation (bytes)'
)
$availableColumns = @($rows[0].PSObject.Properties.Name)
$missingColumns = @($requiredColumns | Where-Object { $availableColumns -notcontains $_ })
if ($missingColumns.Count -gt 0) {
    throw "Profiler CSV is missing required columns: $($missingColumns -join ', ')"
}

$modRows = @(
    $rows | Where-Object {
        $_.'Method name'.IndexOf($ModIdentifier, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    }
)
if ($modRows.Count -eq 0) {
    throw "No methods containing '$ModIdentifier' were found in '$resolvedCsvPath'."
}

$threadDetails = foreach ($row in $modRows) {
    $method = [string]$row.'Method name'
    $calls = [long]0
    $totalRuntimeNs = [long]0
    $selfRuntimeNs = [long]0
    $allocationBytes = [long]0
    $numericColumns = @(
        @{ Name = 'Call count'; Value = [string]$row.'Call count'; Target = [ref]$calls },
        @{ Name = 'Total runtime (ns)'; Value = [string]$row.'Total runtime (ns)'; Target = [ref]$totalRuntimeNs },
        @{ Name = 'Self runtime (ns)'; Value = [string]$row.'Self runtime (ns)'; Target = [ref]$selfRuntimeNs },
        @{ Name = 'Total allocation (bytes)'; Value = [string]$row.'Total allocation (bytes)'; Target = [ref]$allocationBytes }
    )
    foreach ($column in $numericColumns) {
        if (-not [long]::TryParse(
                $column.Value,
                [System.Globalization.NumberStyles]::Integer,
                [System.Globalization.CultureInfo]::InvariantCulture,
                $column.Target)) {
            throw "Invalid $($column.Name) value '$($column.Value)' for method '$method'."
        }
    }

    [pscustomobject]@{
        Thread = [string]$row.Thread
        Method = $method
        Calls = $calls
        TotalRuntimeMs = $totalRuntimeNs / 1000000.0
        SelfRuntimeMs = $selfRuntimeNs / 1000000.0
        TotalAllocationBytes = $allocationBytes
    }
}

$methodSummaries = foreach ($group in ($threadDetails | Group-Object -Property Method)) {
    $calls = [long](($group.Group | Measure-Object -Property Calls -Sum).Sum)
    $totalRuntimeMs = [double](($group.Group | Measure-Object -Property TotalRuntimeMs -Sum).Sum)
    $selfRuntimeMs = [double](($group.Group | Measure-Object -Property SelfRuntimeMs -Sum).Sum)
    $allocationBytes = [long](($group.Group | Measure-Object -Property TotalAllocationBytes -Sum).Sum)
    $threads = @($group.Group | ForEach-Object { $_.Thread } | Sort-Object -Unique)

    [pscustomobject]@{
        Method = $group.Name
        Calls = $calls
        ThreadCount = $threads.Count
        TotalRuntimeMs = $totalRuntimeMs
        SelfRuntimeMs = $selfRuntimeMs
        AverageTotalUsPerCall = if ($calls -gt 0) { $totalRuntimeMs * 1000.0 / $calls } else { 0.0 }
        AverageSelfUsPerCall = if ($calls -gt 0) { $selfRuntimeMs * 1000.0 / $calls } else { 0.0 }
        TotalAllocationBytes = $allocationBytes
        AllocationBytesPerCall = if ($calls -gt 0) { $allocationBytes / [double]$calls } else { 0.0 }
    }
}

$totalModSelfRuntimeMs = [double](($methodSummaries | Measure-Object -Property SelfRuntimeMs -Sum).Sum)
$methodSummaries = @(
    $methodSummaries | ForEach-Object {
        $selfSharePercent = if ($totalModSelfRuntimeMs -gt 0) {
            100.0 * $_.SelfRuntimeMs / $totalModSelfRuntimeMs
        }
        else {
            0.0
        }

        [pscustomobject]@{
            Method = $_.Method
            Calls = $_.Calls
            ThreadCount = $_.ThreadCount
            TotalRuntimeMs = $_.TotalRuntimeMs
            SelfRuntimeMs = $_.SelfRuntimeMs
            SelfSharePercent = $selfSharePercent
            AverageTotalUsPerCall = $_.AverageTotalUsPerCall
            AverageSelfUsPerCall = $_.AverageSelfUsPerCall
            TotalAllocationBytes = $_.TotalAllocationBytes
            AllocationBytesPerCall = $_.AllocationBytesPerCall
        }
    }
)

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $sourceDirectory = Split-Path -Parent $resolvedCsvPath
    $sourceName = [System.IO.Path]::GetFileNameWithoutExtension($resolvedCsvPath)
    $reportPath = Join-Path $sourceDirectory "$sourceName-$ModIdentifier-report.md"
}
else {
    $reportPath = [System.IO.Path]::GetFullPath($OutputPath)
}

$reportDirectory = Split-Path -Parent $reportPath
if (-not (Test-Path -LiteralPath $reportDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}
$reportBasePath = Join-Path $reportDirectory ([System.IO.Path]::GetFileNameWithoutExtension($reportPath))
$methodCsvPath = "$reportBasePath-methods.csv"
$threadCsvPath = "$reportBasePath-threads.csv"

$methodColumns = @(
    'Method',
    'Calls',
    'ThreadCount',
    'SelfRuntimeMs',
    'SelfSharePercent',
    'TotalRuntimeMs',
    'AverageSelfUsPerCall',
    'AverageTotalUsPerCall',
    'TotalAllocationBytes',
    'AllocationBytesPerCall'
)
$methodSummaries |
    Sort-Object -Property SelfRuntimeMs -Descending |
    Select-Object $methodColumns |
    Export-Csv -LiteralPath $methodCsvPath -NoTypeInformation -Encoding UTF8
$threadDetails |
    Sort-Object -Property SelfRuntimeMs -Descending |
    Export-Csv -LiteralPath $threadCsvPath -NoTypeInformation -Encoding UTF8

$topMethods = @($methodSummaries | Sort-Object -Property SelfRuntimeMs -Descending | Select-Object -First $TopCount)
$reportLines = [System.Collections.Generic.List[string]]::new()
$reportLines.Add("# Mono profiler report: $ModIdentifier")
$reportLines.Add('')
$reportLines.Add("- Capture: ``$resolvedCsvPath``")
$reportLines.Add("- CSV rows: $($rows.Count)")
$reportLines.Add("- Matching mod rows: $($modRows.Count)")
$reportLines.Add("- Unique mod methods: $($methodSummaries.Count)")
$reportLines.Add("- Sum of exclusive self runtime: $($totalModSelfRuntimeMs.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture)) ms")
$reportLines.Add("- Detailed files: ``$methodCsvPath`` and ``$threadCsvPath``")
$reportLines.Add('')
$reportLines.Add('## Hottest methods by self runtime')
$reportLines.Add('')
$reportLines.Add('| Rank | Method | Calls | Threads | Self ms | Share of mod self time | Inclusive ms | Avg self us/call | Allocated bytes |')
$reportLines.Add('|---:|---|---:|---:|---:|---:|---:|---:|---:|')

$rank = 0
foreach ($method in $topMethods) {
    $rank++
    $safeMethodName = $method.Method.Replace('|', '\|')
    $selfMs = $method.SelfRuntimeMs.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture)
    $selfShare = $method.SelfSharePercent.ToString('F2', [System.Globalization.CultureInfo]::InvariantCulture)
    $inclusiveMs = $method.TotalRuntimeMs.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture)
    $averageSelfUs = $method.AverageSelfUsPerCall.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture)
    $reportLine = @(
        "| $rank | $safeMethodName | $($method.Calls) | $($method.ThreadCount) | $selfMs | "
        "$selfShare% | $inclusiveMs | $averageSelfUs | $($method.TotalAllocationBytes) |"
    ) -join ''
    $reportLines.Add($reportLine)
}

$reportLines.Add('')
$reportLines.Add('## All methods')
$reportLines.Add('')
$reportLines.Add('Sorted by exclusive self runtime. Inclusive runtime includes called methods and overlaps between parent/child rows; do not sum inclusive values across methods.')
$reportLines.Add('')
$reportLines.Add('| Method | Calls | Threads | Self ms | Self share | Inclusive ms | Avg self us/call | Avg inclusive us/call | Allocation bytes | Bytes/call |')
$reportLines.Add('|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|')

foreach ($method in ($methodSummaries | Sort-Object -Property SelfRuntimeMs -Descending)) {
    $safeMethodName = $method.Method.Replace('|', '\|')
    $selfMs = $method.SelfRuntimeMs.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture)
    $selfShare = $method.SelfSharePercent.ToString('F2', [System.Globalization.CultureInfo]::InvariantCulture)
    $inclusiveMs = $method.TotalRuntimeMs.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture)
    $averageSelfUs = $method.AverageSelfUsPerCall.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture)
    $averageTotalUs = $method.AverageTotalUsPerCall.ToString('F3', [System.Globalization.CultureInfo]::InvariantCulture)
    $bytesPerCall = $method.AllocationBytesPerCall.ToString('F2', [System.Globalization.CultureInfo]::InvariantCulture)
    $reportLine = @(
        "| $safeMethodName | $($method.Calls) | $($method.ThreadCount) | $selfMs | "
        "$selfShare% | $inclusiveMs | $averageSelfUs | $averageTotalUs | "
        "$($method.TotalAllocationBytes) | $bytesPerCall |"
    ) -join ''
    $reportLines.Add($reportLine)
}

$reportLines.Add('')
$reportLines.Add('## Reading this report')
$reportLines.Add('')
$reportLines.Add('- Use **Self ms** to find work performed directly by a method; this avoids charging child-method time to every caller.')
$reportLines.Add('- **Inclusive ms** includes time in methods it calls. It helps show expensive call paths but overlaps across rows.')
$reportLines.Add('- Allocation values are shown per method as reported by Mono; allocation totals across parent and child methods may overlap.')
$reportLines.Add('- Runtime and allocation values are summed across profiler threads. Summed CPU time can exceed elapsed capture time when threads run concurrently.')
$reportLines.Add('- This CSV reports accumulated calls, not per-frame percentiles or worst-frame duration.')

Set-Content -LiteralPath $reportPath -Value $reportLines -Encoding UTF8

Write-Output "Report: $reportPath"
Write-Output "Method details: $methodCsvPath"
Write-Output "Per-thread entries: $threadCsvPath"
Write-Output "Filtered $($modRows.Count) rows into $($methodSummaries.Count) unique mod methods."
