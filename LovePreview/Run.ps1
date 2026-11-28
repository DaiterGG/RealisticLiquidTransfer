param(
    [string]$LoveExecutable
)

if (-not $LoveExecutable) {
    $bundledLove = Join-Path (Split-Path -Parent $PSScriptRoot) "love-11.5-win64\love.exe"
    if (Test-Path -LiteralPath $bundledLove -PathType Leaf) {
        $LoveExecutable = $bundledLove
    } else {
        $installedLove = Get-Command love.exe -ErrorAction SilentlyContinue
        if ($installedLove) {
            $LoveExecutable = $installedLove.Source
        } else {
            throw "Could not find love.exe. Install LÖVE or pass -LoveExecutable."
        }
    }
}

if (-not (Test-Path -LiteralPath $LoveExecutable -PathType Leaf)) {
    throw "LÖVE executable not found: $LoveExecutable"
}

& $LoveExecutable $PSScriptRoot
exit $LASTEXITCODE
