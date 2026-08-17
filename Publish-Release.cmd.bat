@echo off
setlocal

cd /d "%~dp0"

powershell.exe -NoProfile -ExecutionPolicy Bypass ^
  -File "%~dp0SimpleGit11\Build\Publish-Release.ps1" ^
  -StopRunningApp

set "exitCode=%ERRORLEVEL%"

echo.
if not "%exitCode%"=="0" (
    echo Release publication failed with exit code %exitCode%.
) else (
    echo Release publication completed successfully.
)

pause
exit /b %exitCode%
