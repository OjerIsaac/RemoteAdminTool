@echo off
cd /d "%~dp0"
echo Rebuilding Host.exe for local testing (keys preserved)...
powershell -ExecutionPolicy Bypass -File "build.ps1" -PublishHostDev
echo.
echo Done! Kill the running Host and restart Host\dist\Host.exe
echo.
pause
