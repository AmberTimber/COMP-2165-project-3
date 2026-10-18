$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
& (Join-Path $project 'Sync-Student-Content.ps1') -ProjectRoot $project
$version = (Get-Content -LiteralPath (Join-Path $project 'ProjectSettings\ProjectVersion.txt') | Select-String '^m_EditorVersion:').ToString().Split(':')[1].Trim()
$editor = $null
foreach ($root in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')) {
    Get-ChildItem $root -ErrorAction SilentlyContinue | ForEach-Object {
        $entry = Get-ItemProperty $_.PSPath -ErrorAction SilentlyContinue
        if ($entry.DisplayName -eq "Unity $version" -and (Test-Path -LiteralPath $entry.DisplayIcon)) { $editor = $entry.DisplayIcon }
    }
}
if (-not $editor) { throw "Unity $version is not installed. Install it with Unity Hub." }
$webgl = Join-Path (Split-Path -Parent $editor) 'Data\PlaybackEngines\WebGLSupport'
if (-not (Test-Path -LiteralPath $webgl)) { throw "Add WebGL Build Support to Unity $version in Unity Hub, then run this file again." }
$log = Join-Path $project 'Builds\build.log'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $log) | Out-Null
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
$projectBytes = [System.Text.Encoding]::UTF8.GetBytes([System.IO.Path]::GetFullPath($project).ToUpperInvariant())
$hash = [System.Security.Cryptography.SHA256]::Create()
try { $cacheId = [System.BitConverter]::ToString($hash.ComputeHash($projectBytes), 0, 6).Replace('-', '').ToLowerInvariant() }
finally { $hash.Dispose() }
$buildProject = [System.IO.Path]::GetFullPath((Join-Path $temporaryRoot ('COMP2165_Clicker_BuildCache_' + $cacheId)))
if (-not $buildProject.StartsWith($temporaryRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build cache path is outside the temporary directory.'
}
$buildMutex = [System.Threading.Mutex]::new($false, ('Local\COMP2165_Clicker_Build_' + $cacheId))
$mutexHeld = $false
try {
    $mutexHeld = $buildMutex.WaitOne(0)
    if (-not $mutexHeld) { throw 'A WebGL build is already running for this project.' }
    New-Item -ItemType Directory -Force -Path $buildProject | Out-Null
    foreach ($folder in @('Assets', 'Packages', 'ProjectSettings', 'Student Content')) {
        $source = Join-Path $project $folder
        $destination = [System.IO.Path]::GetFullPath((Join-Path $buildProject $folder))
        if (-not $destination.StartsWith($buildProject + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe build cache path: $destination"
        }
        New-Item -ItemType Directory -Force -Path $destination | Out-Null
        & robocopy.exe $source $destination /MIR /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Could not sync $folder into the build cache (robocopy exit $LASTEXITCODE)." }
    }
    foreach ($launcher in @('Play WebGL.bat', 'Play-WebGL.ps1')) {
        Copy-Item -LiteralPath (Join-Path $project $launcher) -Destination $buildProject -Force
    }
    Write-Host 'Building WebGL from a cached copy. Save Unity edits first; the editor can stay open.'
    Write-Host 'The first build may take a while. Later builds reuse Unity imports and WebGL compilation files.'
    $arguments = @('-batchmode', '-nographics', '-quit', '-projectPath', ('"' + $buildProject + '"'), '-executeMethod', 'COMP2165.Clicker.Editor.ClickerProjectBuilder.BuildWebGL', '-logFile', ('"' + $log + '"'))
    $unityProcess = Start-Process -FilePath $editor -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
    if ($unityProcess.ExitCode -ne 0) { throw "Build failed. See $log" }
    $builtZip = Join-Path $buildProject 'Builds\COMP2165_Clicker_itchio.zip'
    if (-not (Test-Path -LiteralPath $builtZip)) { throw "Build did not create a ZIP. See $log" }
    $builtFolder = Join-Path $buildProject 'Builds\WebGL'
    if (-not (Test-Path -LiteralPath (Join-Path $builtFolder 'Play WebGL.bat'))) { throw "Build did not include the local play launcher. See $log" }
    $outputFolder = Join-Path $project 'Builds\WebGL'
    New-Item -ItemType Directory -Force -Path $outputFolder | Out-Null
    Copy-Item -Path (Join-Path $builtFolder '*') -Destination $outputFolder -Recurse -Force
    $zip = Join-Path $project 'Builds\COMP2165_Clicker_itchio.zip'
    Copy-Item -LiteralPath $builtZip -Destination $zip -Force
    Write-Host "Ready for itch.io: $zip"
}
finally {
    if ($mutexHeld) { $buildMutex.ReleaseMutex() }
    $buildMutex.Dispose()
}
