@echo off
REM Run Mangette from this repo in CMD (not the Windows service).
cd /d "%~dp0"

REM Always use 64-bit dotnet (an x86 host on PATH can fail with a Windows error dialog).
set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" set "DOTNET_EXE=dotnet"

echo.
echo Stopping any old Mangette.exe so the rebuild can replace it...
taskkill /IM Mangette.exe /F >nul 2>&1
timeout /t 1 /nobreak >nul

echo Starting Mangette at http://localhost:8585
echo You MUST see a DARK LEFT SIDEBAR. If not, Ctrl+F5 or use a private window.
echo.

"%DOTNET_EXE%" run --project API\API.csproj --no-launch-profile
if errorlevel 1 (
  echo.
  echo Mangette failed to start. Scroll up for the real error.
  echo If Windows said it was unable to start correctly, an old Mangette.exe was still running.
  echo Close that window and run this script again.
  pause
)
