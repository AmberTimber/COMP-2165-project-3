@echo off
setlocal
set "SCRIPT=%~dp0..\Run-Student-Build.ps1"
if not exist "%SCRIPT%" (
    echo Build script not found: %SCRIPT%
    if not defined CLICKER_NO_PAUSE pause
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" echo Build stopped. Read the error above.
if not defined CLICKER_NO_PAUSE pause
exit /b %RESULT%
