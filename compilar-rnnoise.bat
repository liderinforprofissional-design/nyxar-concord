@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo === Compilando rnnoise.dll (x64) ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0compilar-rnnoise.ps1"
pause
