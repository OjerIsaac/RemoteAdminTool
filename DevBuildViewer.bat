@echo off
cd /d "%~dp0"
echo Closing any running Viewer.exe before rebuild...
taskkill /F /IM Viewer.exe >nul 2>&1
echo Rebuilding Viewer.exe into dist\[today's-date]\viewer\...
powershell -ExecutionPolicy Bypass -File "build.ps1" -PublishViewer
echo.
pause
