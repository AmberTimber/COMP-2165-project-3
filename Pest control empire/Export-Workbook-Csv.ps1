param([string]$ProjectRoot = $PSScriptRoot)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$folder = Join-Path $ProjectRoot 'Student Content'
$workbookPath = Join-Path $folder 'Clicker Template.xlsx'
$sheetNames = @('Settings', 'Currencies', 'Buildings', 'Upgrades', 'Events', 'Visuals', 'Audio')
$mainNamespace = 'http://schemas.openxmlformats.org/spreadsheetml/2006/main'
$relationshipNamespace = 'http://schemas.openxmlformats.org/officeDocument/2006/relationships'
$packageNamespace = 'http://schemas.openxmlformats.org/package/2006/relationships'

function Read-ZipXml($zip, [string]$name) {
    $entry = $zip.GetEntry($name)
    if ($null -eq $entry) { throw "Workbook entry missing: $name" }
    $stream = $entry.Open()
    try {
        $reader = [System.IO.StreamReader]::new($stream)
        try { return [xml]$reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Column-Index([string]$reference) {
    $number = 0
    foreach ($character in $reference.ToUpperInvariant().ToCharArray()) {
        if ($character -lt 'A' -or $character -gt 'Z') { break }
        $number = $number * 26 + [int]$character - [int][char]'A' + 1
    }
    return $number - 1
}

if (-not (Test-Path -LiteralPath $workbookPath -PathType Leaf)) { throw "Workbook missing: $workbookPath" }
$zip = [System.IO.Compression.ZipFile]::OpenRead($workbookPath)
try {
    $workbook = Read-ZipXml $zip 'xl/workbook.xml'
    $relationships = Read-ZipXml $zip 'xl/_rels/workbook.xml.rels'
    $ns = [System.Xml.XmlNamespaceManager]::new($workbook.NameTable)
    $ns.AddNamespace('m', $mainNamespace)
    $ns.AddNamespace('r', $relationshipNamespace)
    $relNs = [System.Xml.XmlNamespaceManager]::new($relationships.NameTable)
    $relNs.AddNamespace('p', $packageNamespace)
    $paths = @{}
    foreach ($item in $relationships.SelectNodes('//p:Relationship', $relNs)) {
        $paths[$item.GetAttribute('Id')] = $item.GetAttribute('Target')
    }
    $sheets = @{}
    foreach ($item in $workbook.SelectNodes('//m:sheet', $ns)) {
        $sheets[$item.GetAttribute('name')] = $item.GetAttribute('id', $relationshipNamespace)
    }
    $shared = [System.Collections.Generic.List[string]]::new()
    if ($null -ne $zip.GetEntry('xl/sharedStrings.xml')) {
        $sharedXml = Read-ZipXml $zip 'xl/sharedStrings.xml'
        foreach ($item in $sharedXml.SelectNodes('//m:si', $ns)) {
            $shared.Add(($item.SelectNodes('.//m:t', $ns) | ForEach-Object { $_.InnerText }) -join '')
        }
    }

    foreach ($name in $sheetNames) {
        if (-not $sheets.ContainsKey($name)) { throw "Workbook sheet missing: $name" }
        $target = $paths[$sheets[$name]].Replace('\', '/').TrimStart('/')
        if (-not $target.StartsWith('xl/')) { $target = 'xl/' + $target }
        $sheet = Read-ZipXml $zip $target
        $rows = [System.Collections.Generic.List[object]]::new()
        foreach ($row in $sheet.SelectNodes('//m:sheetData/m:row', $ns)) {
            $rowIndex = [int]$row.GetAttribute('r') - 1
            while ($rows.Count -le $rowIndex) { $rows.Add([System.Collections.Generic.List[string]]::new()) }
            foreach ($cell in $row.SelectNodes('./m:c', $ns)) {
                $column = Column-Index $cell.GetAttribute('r')
                while ($rows[$rowIndex].Count -le $column) { $rows[$rowIndex].Add('') }
                $valueNode = $cell.SelectSingleNode('./m:v', $ns)
                $value = if ($null -eq $valueNode) { '' } else { $valueNode.InnerText }
                if ($cell.GetAttribute('t') -eq 's' -and $value -ne '') { $value = $shared[[int]$value] }
                elseif ($cell.GetAttribute('t') -eq 'inlineStr') {
                    $value = ($cell.SelectNodes('.//m:t', $ns) | ForEach-Object { $_.InnerText }) -join ''
                }
                $rows[$rowIndex][$column] = $value
            }
        }
        if ($rows.Count -lt 2 -or $rows[0].Count -eq 0) { throw "$name needs a header and at least one data row" }
        if ($name -in @('Buildings', 'Upgrades') -and -not $rows[0].Contains('tooltip')) {
            $existing = Join-Path $folder "$name.csv"
            $savedTooltips = @{}
            if (Test-Path -LiteralPath $existing) {
                foreach ($item in (Import-Csv -LiteralPath $existing)) {
                    if ($item.PSObject.Properties.Name -contains 'tooltip') { $savedTooltips[$item.id] = $item.tooltip }
                }
            }
            $idColumn = $rows[0].IndexOf('id')
            $descriptionColumn = $rows[0].IndexOf('description')
            if ($idColumn -lt 0 -or $descriptionColumn -lt 0) { throw "$name needs id and description columns" }
            $rows[0].Add('tooltip')
            for ($index = 1; $index -lt $rows.Count; $index++) {
                $row = $rows[$index]
                $id = if ($idColumn -lt $row.Count) { $row[$idColumn] } else { '' }
                $description = if ($descriptionColumn -lt $row.Count) { $row[$descriptionColumn] } else { '' }
                $row.Add($(if ($savedTooltips.ContainsKey($id)) { $savedTooltips[$id] } else { $description }))
            }
        }
        $width = $rows[0].Count
        $lines = [System.Collections.Generic.List[string]]::new()
        foreach ($row in $rows) {
            if (-not @($row | Where-Object { $_ -ne '' }).Count) { continue }
            $fields = for ($column = 0; $column -lt $width; $column++) {
                $value = if ($column -lt $row.Count) { $row[$column] } else { '' }
                '"' + $value.Replace('"', '""') + '"'
            }
            $lines.Add(($fields -join ','))
        }
        [System.IO.File]::WriteAllLines((Join-Path $folder "$name.csv"), $lines, [System.Text.UTF8Encoding]::new($false))
    }
}
finally { $zip.Dispose() }
Write-Host 'Exported seven workbook sheets to Student Content CSVs. Palette.csv was kept as-is.'
