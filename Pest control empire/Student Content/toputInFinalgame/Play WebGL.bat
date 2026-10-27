@echo off
setlocal
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Play-WebGL.ps1"
if errorlevel 1 (
    echo Could not start the local game. Read the error above.
    pause
)
