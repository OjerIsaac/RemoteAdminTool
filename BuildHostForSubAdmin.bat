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
echo Paste the sub-admin's ADMIN TOKEN below.
echo (Copy it from the Details button in your Viewer app)
echo.
set /p ADMINTOKEN="Admin Token: "
echo.
echo Building Host EXE for %TENANTNAME% (key: %LICKEY:~0,8%)...
echo.
powershell -ExecutionPolicy Bypass -File "build.ps1" -PublishHostForTenant "%LICKEY%" -HostToken "%HOSTTOKEN%" -AdminToken "%ADMINTOKEN%" -TenantName "%TENANTNAME%"
echo.
echo When done, the complete package folder is in:
echo   dist\[today's-date]\subadmin_%TENANTNAME%_%LICKEY:~0,8%\
echo.
echo Send that folder to the sub-admin.
echo.
pause
