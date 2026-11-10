@echo off
rem MD Dia-Inch - Uninstall (no admin). Author: MD (Mayank Dholariya)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0md_diainch.ps1" -Action Uninstall
echo.
pause
