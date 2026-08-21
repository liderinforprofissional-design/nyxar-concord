@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion
cd /d "%~dp0"

REM ============================================================
REM  Nyxar Concord - Publicar uma NOVA versao (release)
REM  Rodar toda vez que quiser lancar uma atualizacao.
REM  Sobe a versao -> commit/push -> compila -> cria o Release.
REM  Quem abrir o app com versao menor recebe o aviso.
REM ============================================================
set "CSPROJ=src\NyxarConcord\NyxarConcord.csproj"

echo.
echo === Verificando ferramentas (git, gh, dotnet) ===
git --version   >nul 2>&1 || (echo [ERRO] Git nao encontrado.    & pause & exit /b 1)
gh --version    >nul 2>&1 || (echo [ERRO] GitHub CLI nao encontrado. & pause & exit /b 1)
dotnet --version>nul 2>&1 || (echo [ERRO] .NET SDK nao encontrado.  & pause & exit /b 1)

if not exist "%CSPROJ%" (echo [ERRO] Nao achei %CSPROJ% & pause & exit /b 1)

REM --- Le a versao atual do csproj ---
for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "[regex]::Match((Get-Content -Raw '%CSPROJ%'),'<Version>(.*?)</Version>').Groups[1].Value"`) do set "ATUAL=%%v"
echo.
echo Versao atual do app: %ATUAL%
set /p "NOVA=Digite a NOVA versao (ex.: 0.2.0): "
if "%NOVA%"=="" (echo Cancelado. & pause & exit /b 0)

REM --- Atualiza a versao no csproj ---
echo.
echo === Atualizando a versao para %NOVA% ===
powershell -NoProfile -Command "(Get-Content -Raw '%CSPROJ%') -replace '<Version>.*?</Version>', '<Version>%NOVA%</Version>' | Set-Content -Encoding UTF8 '%CSPROJ%'"

REM --- Envia o codigo ---
echo.
echo === Enviando o codigo para o GitHub ===
git add .
git commit -m "Versao %NOVA%"
git push

REM --- Compila o app (self-contained: roda sem instalar .NET) ---
echo.
echo === Compilando o app (pode demorar alguns minutos) ===
if exist "dist\app" rmdir /s /q "dist\app"
dotnet publish "%CSPROJ%" -c Release -r win-x64 --self-contained true -o "dist\app"
if errorlevel 1 (echo [ERRO] Falha ao compilar. & pause & exit /b 1)

REM --- Compacta em .zip ---
echo.
echo === Compactando ===
if not exist "dist" mkdir "dist"
set "ZIP=dist\NyxarConcord-v%NOVA%.zip"
if exist "%ZIP%" del "%ZIP%"
powershell -NoProfile -Command "Compress-Archive -Path 'dist\app\*' -DestinationPath '%ZIP%' -Force"

REM --- Publica o Release no GitHub ---
echo.
echo === Publicando o Release v%NOVA% no GitHub ===
gh release create v%NOVA% "%ZIP%" --title "Nyxar Concord v%NOVA%" --notes "Atualizacao v%NOVA%"
if errorlevel 1 (
    echo.
    echo [ATENCAO] Nao consegui criar o release automaticamente.
    echo Verifique se a tag v%NOVA% ja existe ou faca login com: gh auth login
    pause & exit /b 1
)

echo.
echo ============================================================
echo  PRONTO! Release v%NOVA% publicado.
echo  Quem abrir o app com versao menor vera o aviso de update.
echo ============================================================
echo.
pause
