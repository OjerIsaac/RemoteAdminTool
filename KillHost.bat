@echo off
setlocal

:: Check for Administrator privileges
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo Elevating to Administrator to terminate all Host processes...
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process cmd.exe -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs -Wait"
    exit /b
)

echo Stopping all Host processes (Elevated)...
taskkill /F /T /IM Host.exe >nul 2>&1
powershell -NoProfile -Command "Get-Process -Name 'Host' -ErrorAction SilentlyContinue | Stop-Process -Force"

echo All Host processes stopped.
timeout /t 2 >nul
