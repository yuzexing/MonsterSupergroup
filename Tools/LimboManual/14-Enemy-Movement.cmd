@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Technical.ps1" -Profile movement-observe -LogDetail light
if errorlevel 1 pause
