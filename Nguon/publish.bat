@echo off
setlocal
echo ========== BUILD TQLTK RELEASE ARTIFACTS ==========
echo A configured code-signing certificate will be used automatically; without one, an unsigned release package will be created.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0release-artifacts.ps1" -UnsignedRelease %*
if errorlevel 1 (
    echo.
    echo ========== RELEASE BUILD FAILED ==========
    pause
    exit /b 1
)
echo.
echo ========== RELEASE BUILD COMPLETE ==========
pause
