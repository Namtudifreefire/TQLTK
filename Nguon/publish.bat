@echo off
echo ========== UPDATE TQLTK ==========

echo [1/5] Killing running app...
taskkill /F /IM PersonalInfoManager.exe 2>nul
timeout /t 1 /nobreak >nul

echo [2/5] Cleaning old build...
dotnet clean -c Release

echo [3/5] Building...
dotnet build -c Release

echo [4/5] Publishing single-file exe...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true

if %errorlevel% neq 0 (
    echo.
    echo ========== PUBLISH FAILED ==========
    pause
    exit /b 1
)

echo.
echo ========== DONE ==========
echo File exe: bin\Release\net10.0-windows\win-x64\publish\PersonalInfoManager.exe
echo.
pause