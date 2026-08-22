@echo off
chcp 65001 >nul
cd /d "%~dp0"

echo ============================================================
echo  Nyxar Concord - Baixar as DLLs do FFmpeg (para o H264)
echo  Baixa ~40-80 MB e copia para src\NyxarConcord\ffmpeg\
echo ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0baixar-ffmpeg.ps1"
if errorlevel 1 (
    echo.
    echo [ERRO] Falhou. Verifique a internet e tente de novo.
    pause & exit /b 1
)

echo.
echo Pronto! Agora recompile o app (as DLLs entram na saida automaticamente).
pause
