@echo off
cd /d "%~dp0"
echo ===========================================
echo  Build Host EXE for a Sub-Admin
echo ===========================================
echo.
echo Paste the sub-admin's LICENSE KEY below.
echo (Copy it from the Details button in your Viewer app)
echo.
set /p LICKEY="License Key: "
echo.
echo Building Host EXE with key: %LICKEY:~0,8%...
echo.
powershell -ExecutionPolicy Bypass -File "build.ps1" -PublishHostForTenant "%LICKEY%"
echo.
echo When done, grab: Host\dist\Host.exe
echo.
