@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Limbo.ps1" -Mode Host
if errorlevel 1 pause
