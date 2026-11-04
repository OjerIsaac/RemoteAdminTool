@echo off
cd /d "%~dp0"
echo ===========================================
echo  Build Host EXE for a Sub-Admin
echo ===========================================
echo.
echo Enter the Sub-Admin / Company Name (e.g. AcmeCorp, JohnDoe):
set /p TENANTNAME="Tenant Name: "
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
echo Building Host EXE for %TENANTNAME% (key: %LICKEY:~0,8%)...
echo.
powershell -ExecutionPolicy Bypass -File "build.ps1" -PublishHostForTenant "%LICKEY%" -HostToken "%HOSTTOKEN%" -TenantName "%TENANTNAME%"
echo.
echo When done, the complete package (including a ready-to-share .ZIP) is in:
echo   dist\[today's-date]\subadmin_%TENANTNAME%_%LICKEY:~0,8%\
echo.
echo Send that folder or ZIP to the sub-admin.
echo.
pause
