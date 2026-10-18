param([string]$ProjectRoot = $PSScriptRoot)

Add-Type -AssemblyName System.Drawing
$atlasPath = Join-Path $ProjectRoot 'Assets/Clicker/SpriteSheets/cookie_clicker_icons.png'
$mapPath = Join-Path $ProjectRoot 'Assets/Clicker/SpriteSheets/IconMap.csv'
$iconDirectory = Join-Path $ProjectRoot 'Assets/StreamingAssets/Media/icons'
$sheetDirectory = Join-Path $ProjectRoot 'Assets/StreamingAssets/Media/sheets'
$previewDirectory = Join-Path $ProjectRoot 'Assets/Clicker/UIPreview'
$previewKeys = @('clickable', 'golden_cookie', 'cursor', 'upgrade_click', 'event_quality')
$tileSize = 48
$columns = 35
$rows = 44

New-Item -ItemType Directory -Force -Path $iconDirectory, $previewDirectory, $sheetDirectory | Out-Null
Copy-Item -LiteralPath $atlasPath -Destination (Join-Path $sheetDirectory 'cookie_clicker_icons.png') -Force
$atlas = [System.Drawing.Bitmap]::new($atlasPath)
try {
    if ($atlas.Width -ne ($columns * $tileSize) -or $atlas.Height -ne ($rows * $tileSize)) {
        throw "Expected a 1680x2112 atlas; got $($atlas.Width)x$($atlas.Height)."
    }
    foreach ($entry in (Import-Csv -LiteralPath $mapPath)) {
        $column = [int]$entry.column
        $row = [int]$entry.row
        if ($entry.key -notmatch '^[a-z0-9_]+$' -or $column -lt 0 -or $column -ge $columns -or $row -lt 0 -or $row -ge $rows) {
            throw "Invalid icon mapping: $($entry.key),$column,$row"
        }
        $rect = [System.Drawing.Rectangle]::new($column * $tileSize, $row * $tileSize, $tileSize, $tileSize)
        $icon = $atlas.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $path = Join-Path $iconDirectory "$($entry.key).png"
            $icon.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
            if ($entry.key -in $previewKeys) {
                $icon.Save((Join-Path $previewDirectory "$($entry.key).png"), [System.Drawing.Imaging.ImageFormat]::Png)
            }
        } finally {
            $icon.Dispose()
        }
    }
} finally {
    $atlas.Dispose()
}
Write-Output "Exported $((Import-Csv -LiteralPath $mapPath).Count) icons from $atlasPath"
