@echo off
cd /d "%~dp0"
"%~dp0publish\PadMicro.exe" "%~dp0controller-padmicro-profile.json"
if errorlevel 1 pause
