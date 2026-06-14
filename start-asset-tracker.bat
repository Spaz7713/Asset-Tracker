@echo off
REM Batch file to start Asset Tracker from repository root
SET REPO_DIR=%~dp0
cd /d "%REPO_DIR%"
cd /d "C:\Users\Spencer\Desktop\Repository\Asset Tracker"
echo Starting Asset Tracker (dotnet run)...
dotnet run --project "MediaTracker.csproj" -c Debug
echo Application exited. Press any key to close.
pause > nul
