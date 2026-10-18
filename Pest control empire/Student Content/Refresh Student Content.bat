@echo off
setlocal
set "SCRIPT=%~dp0..\Sync-Student-Content.ps1"
if not exist "%SCRIPT%" (
    echo Refresh script not found: %SCRIPT%
    if not defined CLICKER_NO_PAUSE pause
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
set "RESULT=%ERRORLEVEL%"
if "%RESULT%"=="0" (echo Student media and CSVs are ready.) else (echo Refresh failed. Read the error above.)
if not defined CLICKER_NO_PAUSE pause
exit /b %RESULT%
