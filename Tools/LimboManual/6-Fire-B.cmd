@echo off
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Technical.ps1" -Profile spatial-b -LogDetail light
if errorlevel 1 pause
