@echo off
cd /d "%~dp0"
"%~dp0publish\StadiaCodexBridge.exe" "%~dp0controller-codex-profile.json"
if errorlevel 1 pause
