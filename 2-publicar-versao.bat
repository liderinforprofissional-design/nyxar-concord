@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

REM ============================================================
REM  Nyxar Concord - Publicar uma NOVA versao (release)
REM  Sobe a versao -> commit/push -> compila -> zip + instalador
REM  -> cria o Release no GitHub com os dois anexos.
REM ============================================================
set "CSPROJ=src\NyxarConcord\NyxarConcord.csproj"

echo === Verificando ferramentas (git, gh, dotnet) ===
git --version    >nul 2>&1 || (echo [ERRO] Git nao encontrado.        & pause & exit /b 1)
gh --version     >nul 2>&1 || (echo [ERRO] GitHub CLI nao encontrado. & pause & exit /b 1)
dotnet --version >nul 2>&1 || (echo [ERRO] .NET SDK nao encontrado.   & pause & exit /b 1)
if not exist "%CSPROJ%" (echo [ERRO] Nao achei %CSPROJ% & pause & exit /b 1)

REM Inno Setup (opcional): se existir, geramos o instalador tambem.
call :FIND_ISCC

REM Le a versao atual do csproj.
set "ATUAL="
for /f "tokens=2 delims=<> " %%v in ('findstr /i "<Version>" "%CSPROJ%"') do if not defined ATUAL set "ATUAL=%%v"
echo.
echo Versao atual do app: %ATUAL%
set /p "NOVA=Digite a NOVA versao (ex.: 0.5.0): "
if "%NOVA%"=="" (echo Cancelado. & pause & exit /b 0)

echo.
echo === Atualizando a versao para %NOVA% ===
powershell -NoProfile -Command "(Get-Content -Raw '%CSPROJ%') -replace '<Version>.*?</Version>', '<Version>%NOVA%</Version>' | Set-Content -Encoding UTF8 '%CSPROJ%'"

echo.
echo === Enviando o codigo para o GitHub ===
git add .
git commit -m "Versao %NOVA%"
git push

echo.
echo === Compilando (self-contained win-x64) — pode demorar ===
if exist "dist\app" rmdir /s /q "dist\app"
dotnet publish "%CSPROJ%" -c Release -r win-x64 --self-contained true -o "dist\app"
if errorlevel 1 (echo [ERRO] Falha ao compilar. & pause & exit /b 1)

echo.
echo === Compactando (.zip) ===
if not exist "dist" mkdir "dist"
set "ZIP=dist\NyxarConcord-v%NOVA%.zip"
if exist "%ZIP%" del "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path 'dist\app\*' -DestinationPath '%ZIP%' -Force"

set "ASSETS=%ZIP%"
set "SETUP=installer\Output\NyxarConcordSetup-v%NOVA%.exe"

if defined ISCC (
    echo.
    echo === Gerando o instalador ^(Inno Setup^) ===
    "%ISCC%" /DMyAppVersion=%NOVA% "installer\nyxar-concord.iss"
    if exist "%SETUP%" (
        set "ASSETS=%ZIP% %SETUP%"
    ) else (
        echo [ATENCAO] O instalador nao foi gerado; seguindo so com o .zip.
    )
) else (
    echo.
    echo [INFO] Inno Setup nao instalado — o release sai so com o .zip.
    echo        Para incluir o instalador: winget install JRSoftware.InnoSetup
)

echo.
echo === Publicando o Release v%NOVA% no GitHub ===
gh release create v%NOVA% %ASSETS% --title "Nyxar Concord v%NOVA%" --notes "Atualizacao v%NOVA%"
if errorlevel 1 (
    echo.
    echo [ATENCAO] Nao consegui criar o release.
    echo Verifique se a tag v%NOVA% ja existe, ou rode: gh auth login
    pause & exit /b 1
)

echo.
echo ============================================================
echo  PRONTO! Release v%NOVA% publicado com:
echo    - %ZIP%
if exist "%SETUP%" echo    - %SETUP%
echo  Quem abrir o app com versao menor vera o aviso de update.
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
