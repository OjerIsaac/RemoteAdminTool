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
echo Paste the sub-admin's HOST TOKEN below.
echo (Copy it from the Details button in your Viewer app)
echo.
set /p HOSTTOKEN="Host Token: "
echo.
echo Building Host EXE for key: %LICKEY:~0,8%...
echo.
powershell -ExecutionPolicy Bypass -File "build.ps1" -PublishHostForTenant "%LICKEY%" -HostToken "%HOSTTOKEN%"
echo.
echo When done, the complete package is ready in:
echo   dist\[today's-date]\subadmin_%LICKEY:~0,8%\
echo.
echo Send ALL files in that folder to the sub-admin.
echo Both Host.exe and the key files must stay together in the same folder.
echo.
pause
