@echo off
powershell -NoProfile -Command "Get-Process -Name 'Host' -ErrorAction SilentlyContinue | Stop-Process -Force"
echo All Host processes stopped.
pause
