@echo off
cd /d "%~dp0"
"%~dp0publish\StadiaCodexBridge.UI.exe"
if errorlevel 1 pause
