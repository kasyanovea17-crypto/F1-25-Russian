@echo off
set /p "GAME=F1 25 folder: "
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Engine.ps1" -Action restore -GamePath "%GAME%"
pause
