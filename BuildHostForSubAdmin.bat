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
echo When done, send BOTH files to the sub-admin:
echo   Host\dist\Host.exe   ^<-- the agent
echo   Host\dist\host.key   ^<-- their hashed license key
echo.
echo Both files must stay in the same folder on every end-user machine.
echo Future updates: only Host.exe is replaced automatically. host.key stays.
echo.
pause
