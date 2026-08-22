@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

REM ============================================================
REM  Nyxar Concord - Gerar o instalador do Windows (setup.exe)
REM  1) Compila o app self-contained (win-x64)
REM  2) Empacota com o Inno Setup
REM ============================================================
set "CSPROJ=src\NyxarConcord\NyxarConcord.csproj"

echo === Verificando ferramentas ===
dotnet --version >nul 2>&1 || (echo [ERRO] .NET SDK nao encontrado. & pause & exit /b 1)

REM Le a versao do csproj (vira o nome do instalador).
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "[regex]::Match((Get-Content -Raw '%CSPROJ%'),'<Version>(.*?)</Version>').Groups[1].Value"`) do set "VER=%%v"
if "%VER%"=="" set "VER=0.1.0"
echo Versao: %VER%

echo.
echo === 1/2  Compilando (self-contained win-x64) — pode demorar ===
if exist "dist\app" rmdir /s /q "dist\app"
dotnet publish "%CSPROJ%" -c Release -r win-x64 --self-contained true -o "dist\app"
if errorlevel 1 (echo [ERRO] Falha ao compilar. & pause & exit /b 1)

echo.
echo === 2/2  Gerando o instalador (Inno Setup) ===
set "ISCC=iscc"
where iscc >nul 2>&1
if errorlevel 1 set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if /i not "%ISCC%"=="iscc" if not exist "%ISCC%" (
    echo [ERRO] Inno Setup 6 nao encontrado.
    echo Instale com:  winget install JRSoftware.InnoSetup
    echo ou baixe em:  https://jrsoftware.org/isdl.php
    pause & exit /b 1
)
"%ISCC%" /DMyAppVersion=%VER% "installer\nyxar-concord.iss"
if errorlevel 1 (echo [ERRO] Falha no Inno Setup. & pause & exit /b 1)

echo.
echo ============================================================
echo  PRONTO! Instalador criado em:
echo  installer\Output\NyxarConcordSetup-v%VER%.exe
echo ============================================================
echo.
pause
