@echo off
cd /d "%~dp0"
"%~dp0publish\PadMicro.UI.exe"
if errorlevel 1 pause
