@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Technical.ps1" -Profile pickup-drops -LogDetail light
pause
