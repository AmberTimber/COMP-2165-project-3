param([switch]$NoBrowser)
$ErrorActionPreference = 'Stop'

$buildRoot = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $buildRoot 'index.html'))) {
    throw 'Game files were not found beside this launcher. Extract the whole submission ZIP first.'
}

$root = [System.IO.Path]::GetFullPath($buildRoot).TrimEnd('\')
$server = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$server.Start()
$port = ([System.Net.IPEndPoint]$server.LocalEndpoint).Port
$address = "http://localhost:$port/"
Write-Host "Playing Cookie Works at $address"
Write-Host 'Keep this window open while playing. Close it to stop the local server.'
if (-not $NoBrowser) { Start-Process $address }

function Send-Response {
    param($Stream, [int]$Status, [string]$StatusText, [string]$FilePath, [bool]$HeadOnly)
    $length = 0
    $type = 'text/plain; charset=utf-8'
    $encoding = ''
    if ($FilePath) {
        $length = (Get-Item -LiteralPath $FilePath).Length
        $extension = [System.IO.Path]::GetExtension($FilePath).ToLowerInvariant()
        if ($extension -eq '.br' -or $extension -eq '.gz') {
            $compression = if ($extension -eq '.br') { 'br' } else { 'gzip' }
            $encoding = "Content-Encoding: $compression`r`n"
            $extension = [System.IO.Path]::GetExtension([System.IO.Path]::GetFileNameWithoutExtension($FilePath)).ToLowerInvariant()
        }
        switch ($extension) {
            '.html' { $type = 'text/html; charset=utf-8' }
            '.js'   { $type = 'application/javascript; charset=utf-8' }
            '.css'  { $type = 'text/css; charset=utf-8' }
            '.wasm' { $type = 'application/wasm' }
            '.data' { $type = 'application/octet-stream' }
            '.json' { $type = 'application/json; charset=utf-8' }
            '.csv'  { $type = 'text/csv; charset=utf-8' }
            '.png'  { $type = 'image/png' }
            '.jpg'  { $type = 'image/jpeg' }
            '.jpeg' { $type = 'image/jpeg' }
            '.ico'  { $type = 'image/x-icon' }
            '.ogg'  { $type = 'audio/ogg' }
            '.mp3'  { $type = 'audio/mpeg' }
            '.wav'  { $type = 'audio/wav' }
            '.webp' { $type = 'image/webp' }
        }
    }
    $header = "HTTP/1.1 $Status $StatusText`r`nContent-Type: $type`r`nContent-Length: $length`r`n$encoding" + "Connection: close`r`nCache-Control: no-cache`r`n`r`n"
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($header)
    $Stream.Write($bytes, 0, $bytes.Length)
    if ($FilePath -and -not $HeadOnly) {
        $file = [System.IO.File]::OpenRead($FilePath)
        try { $file.CopyTo($Stream) } finally { $file.Dispose() }
    }
}

try {
    while ($true) {
        $client = $server.AcceptTcpClient()
        try {
            $client.ReceiveTimeout = 10000
            $client.SendTimeout = 30000
            $stream = $client.GetStream()
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)
            $request = $reader.ReadLine()
            if (-not $request) { continue }
            while ($true) {
                $line = $reader.ReadLine()
                if (-not $line) { break }
            }
            if ($request -notmatch '^(GET|HEAD)\s+(\S+)\s+HTTP/') {
                Send-Response $stream 405 'Method Not Allowed' $null $false
                continue
            }
            $headOnly = $Matches[1] -eq 'HEAD'
            $urlPath = ($Matches[2] -split '\?')[0]
            $relative = [System.Uri]::UnescapeDataString($urlPath).TrimStart('/') -replace '/', '\'
            if (-not $relative) { $relative = 'index.html' }
            $filePath = [System.IO.Path]::GetFullPath((Join-Path $root $relative))
            if (-not $filePath.StartsWith($root + '\', [System.StringComparison]::OrdinalIgnoreCase) -or -not [System.IO.File]::Exists($filePath)) {
                Send-Response $stream 404 'Not Found' $null $headOnly
                continue
            }
            Send-Response $stream 200 'OK' $filePath $headOnly
        }
        catch [System.IO.IOException] {
            # Browsers can close a request during page load or refresh.
        }
        finally {
            $client.Close()
        }
    }
}
finally {
    $server.Stop()
}
