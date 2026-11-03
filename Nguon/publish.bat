@echo off
setlocal
echo ========== BUILD TQLTK RELEASE ARTIFACTS ==========
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0release-artifacts.ps1" %*
if errorlevel 1 (
    echo.
    echo ========== RELEASE BUILD FAILED ==========
    pause
    exit /b 1
)
echo.
echo ========== RELEASE BUILD COMPLETE ==========
pause
