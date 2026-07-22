@echo off
cd /d "%~dp0"
echo Please press A, B, X, Y, D-pad directions, and both stick buttons within 25 seconds.
"%~dp0publish\StadiaCodexBridge.exe" --diagnose "%~dp0controller-codex-profile.json"
echo.
echo Diagnostic log: stadia-diagnostic.log
pause
