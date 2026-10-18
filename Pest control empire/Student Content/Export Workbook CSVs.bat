@echo off
setlocal
set "EXPORT=%~dp0..\Export-Workbook-Csv.ps1"
set "SYNC=%~dp0..\Sync-Student-Content.ps1"
if not exist "%EXPORT%" (
    echo Export script not found: %EXPORT%
    if not defined CLICKER_NO_PAUSE pause
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%EXPORT%"
if errorlevel 1 (
    echo Workbook export failed. Read the error above.
    if not defined CLICKER_NO_PAUSE pause
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%SYNC%"
set "RESULT=%ERRORLEVEL%"
if "%RESULT%"=="0" (echo Workbook CSVs are ready.) else (echo CSV sync failed. Read the error above.)
if not defined CLICKER_NO_PAUSE pause
exit /b %RESULT%
