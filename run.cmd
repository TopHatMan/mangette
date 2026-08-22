@echo off
REM Run Mangette from this repo in CMD (not the Windows service).
cd /d "%~dp0"

echo.
echo Stop any old Mangette.exe first if it is still running.
echo Then this window will serve http://localhost:8585
echo You MUST see a DARK LEFT SIDEBAR. If not, Ctrl+F5 or use a private window.
echo.

dotnet run --project API\API.csproj --no-launch-profile
if errorlevel 1 pause
