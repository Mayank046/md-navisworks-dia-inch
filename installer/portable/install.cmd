@echo off
rem MD Dia-Inch - Install (no admin). Author: MD (Mayank Dholariya)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0md_diainch.ps1" -Action Install
echo.
pause
