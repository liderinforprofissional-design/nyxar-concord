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
set "VER="
for /f "tokens=2 delims=<> " %%v in ('findstr /i "<Version>" "%CSPROJ%"') do if not defined VER set "VER=%%v"
if "%VER%"=="" set "VER=0.1.0"
echo Versao: %VER%

echo.
echo === 1/2  Compilando (self-contained win-x64) — pode demorar ===
if exist "dist\app" rmdir /s /q "dist\app"
dotnet publish "%CSPROJ%" -c Release -r win-x64 --self-contained true -o "dist\app"
if errorlevel 1 (echo [ERRO] Falha ao compilar. & pause & exit /b 1)

echo.
echo === 2/2  Gerando o instalador (Inno Setup) ===
call :FIND_ISCC
if not defined ISCC (
    echo [ERRO] Inno Setup 6 nao encontrado em nenhum lugar conhecido.
    echo Instale com:  winget install --id JRSoftware.InnoSetup -e
    echo ou baixe em:  https://jrsoftware.org/isdl.php
    echo Depois, FECHE e reabra este terminal e rode de novo.
    pause & exit /b 1
)
echo Inno Setup: %ISCC%
"%ISCC%" /DMyAppVersion=%VER% "installer\nyxar-concord.iss"
if errorlevel 1 (echo [ERRO] Falha no Inno Setup. & pause & exit /b 1)

echo.
echo ============================================================
echo  PRONTO! Instalador criado em:
echo  installer\Output\NyxarConcordSetup-v%VER%.exe
echo ============================================================
echo.
pause
exit /b 0

:FIND_ISCC
REM Procura o ISCC.exe (compilador do Inno Setup) em varios lugares.
set "ISCC="
for /f "delims=" %%i in ('where iscc 2^>nul') do if not defined ISCC set "ISCC=%%i"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC for /f "tokens=2,*" %%a in ('reg query "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" /v InstallLocation 2^>nul ^| find "InstallLocation"') do if exist "%%bISCC.exe" set "ISCC=%%bISCC.exe"
if not defined ISCC for /f "tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" /v InstallLocation 2^>nul ^| find "InstallLocation"') do if exist "%%bISCC.exe" set "ISCC=%%bISCC.exe"
if not defined ISCC for /f "tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" /v InstallLocation 2^>nul ^| find "InstallLocation"') do if exist "%%bISCC.exe" set "ISCC=%%bISCC.exe"
if not defined ISCC for /f "tokens=2,*" %%a in ('reg query "HKCU\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1" /v InstallLocation 2^>nul ^| find "InstallLocation"') do if exist "%%bISCC.exe" set "ISCC=%%bISCC.exe"
goto :eof
