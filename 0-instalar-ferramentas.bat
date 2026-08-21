@echo off
chcp 65001 >nul
setlocal

REM ============================================================
REM  Nyxar Concord - Instalar Git e GitHub CLI (rodar 1 vez)
REM  Usa o winget (ja vem no Windows 10/11).
REM ============================================================

echo.
echo === Verificando o winget ===
winget --version >nul 2>&1
if errorlevel 1 (
    echo [ERRO] O winget nao foi encontrado.
    echo Instale o Git manualmente em: https://git-scm.com/download/win
    echo e o GitHub CLI em: https://cli.github.com
    pause & exit /b 1
)

echo.
echo === Instalando o Git ===
winget install -e --id Git.Git --accept-source-agreements --accept-package-agreements

echo.
echo === Instalando o GitHub CLI ===
winget install -e --id GitHub.cli --accept-source-agreements --accept-package-agreements

echo.
echo ============================================================
echo  PRONTO! Git e GitHub CLI instalados.
echo.
echo  IMPORTANTE: FECHE esta janela e o Visual Studio,
echo  depois rode o "1-conectar-github.bat".
echo  (o Windows precisa reabrir para reconhecer os programas)
echo ============================================================
echo.
pause
