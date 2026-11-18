@echo off
setlocal

:: Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Elevating to Administrator to terminate all Host processes...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs -Wait"
    exit /b
)

echo Removing Watchdog scheduled task (if present)...
schtasks /Delete /TN "RemoteAdminHost" /F >nul 2>&1

echo Signaling Host to disconnect cleanly from Relay...
powershell -NoProfile -Command "try { $e = [System.Threading.EventWaitHandle]::OpenExisting('Global\RemoteAdminTool_Host_StopSignal'); $e.Set(); Start-Sleep -Seconds 1 } catch {}"

echo Stopping all Host processes (Elevated)...
taskkill /F /T /IM Host.exe >nul 2>&1
powershell -NoProfile -Command "Get-Process -Name 'Host' -ErrorAction SilentlyContinue | Stop-Process -Force"

echo All Host processes stopped.
timeout /t 2 >nul
