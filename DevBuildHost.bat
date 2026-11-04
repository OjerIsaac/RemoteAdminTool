@echo off
cd /d "%~dp0"
echo Rebuilding Host.exe into dist\[today's-date]\host_dev\...
powershell -ExecutionPolicy Bypass -File "build.ps1" -PublishHostDev
echo.
pause
