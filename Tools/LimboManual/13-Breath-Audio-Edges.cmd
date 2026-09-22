@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Technical.ps1" -Profile audio-beam -AudioCase edge -LogDetail light
pause
