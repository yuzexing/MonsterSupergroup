@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Limbo.ps1" -Mode Client
if errorlevel 1 pause
