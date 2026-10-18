param([string]$ProjectRoot = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
$mediaSource = Join-Path $ProjectRoot 'Student Content\Media'
$mediaTarget = Join-Path $ProjectRoot 'Assets\StreamingAssets\Media'
$dataSource = Join-Path $ProjectRoot 'Student Content'
$dataTarget = Join-Path $ProjectRoot 'Assets\StreamingAssets\Data'
$csvNames = @('Settings', 'Currencies', 'Buildings', 'Upgrades', 'Events', 'Visuals', 'Audio', 'Palette')

if (-not (Test-Path -LiteralPath $mediaSource -PathType Container)) { throw "Student media folder is missing: $mediaSource" }
foreach ($name in $csvNames) {
    $csv = Join-Path $dataSource "$name.csv"
    if (-not (Test-Path -LiteralPath $csv -PathType Leaf)) { throw "Student CSV is missing: $csv" }
}
New-Item -ItemType Directory -Force -Path $mediaTarget, $dataTarget | Out-Null

$count = 0
$root = (Resolve-Path -LiteralPath $mediaSource).Path
$allowed = @('.png', '.jpg', '.jpeg', '.mp3', '.ogg', '.wav')
foreach ($file in (Get-ChildItem -LiteralPath $mediaSource -File -Recurse)) {
    if ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Media links are not supported: $($file.FullName)" }
    if ($file.Extension.ToLowerInvariant() -notin $allowed) { continue }
    $relative = $file.FullName.Substring($root.TrimEnd('\', '/').Length + 1)
    $destination = Join-Path $mediaTarget $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
    $count++
}

foreach ($name in $csvNames) {
    Copy-Item -LiteralPath (Join-Path $dataSource "$name.csv") -Destination (Join-Path $dataTarget "$name.csv") -Force
}
Write-Host "Synced $count student media files and $($csvNames.Count) CSVs."
