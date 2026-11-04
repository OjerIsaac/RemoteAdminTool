@echo off
cd /d "%~dp0"
echo Rebuilding Viewer.exe into dist\[today's-date]\viewer\...
powershell -ExecutionPolicy Bypass -File "build.ps1" -PublishViewer
echo.
pause
