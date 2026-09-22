@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Technical.ps1" -Profile audio-wisp -AudioCase edge -LogDetail light
pause
