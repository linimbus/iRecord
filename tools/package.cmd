@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package.ps1" %*
if errorlevel 1 (
    echo.
    echo Packaging failed.
    pause
    exit /b 1
)
echo.
echo Done.
pause
