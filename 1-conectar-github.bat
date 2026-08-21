@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

REM ============================================================
REM  Nyxar Concord - Conectar projeto ao GitHub (rodar 1 vez)
REM  Repositorio: liderinforprofissional-design/nyxar-concord
REM ============================================================
set "REPO=liderinforprofissional-design/nyxar-concord"

echo.
echo === 1/5  Verificando o Git ===
git --version >nul 2>&1
if errorlevel 1 (
    echo [ERRO] O Git nao foi encontrado. Instale em https://git-scm.com/download/win
    pause & exit /b 1
)

REM Define identidade local do commit, caso ainda nao exista
git config user.email >nul 2>&1
if errorlevel 1 git config user.email "liderpacaja@gmail.com"
git config user.name >nul 2>&1
if errorlevel 1 git config user.name "Roberto"

echo.
echo === 2/5  Iniciando o repositorio local ===
if not exist ".git" (
    git init
    git branch -M main
) else (
    echo Repositorio ja iniciado. Ok.
)

echo.
echo === 3/5  Apontando para o GitHub ===
git remote remove origin >nul 2>&1
git remote add origin "https://github.com/%REPO%.git"

echo.
echo === 4/5  Salvando os arquivos (commit) ===
git add .
git commit -m "Primeira versao do Nyxar Concord no GitHub"

echo.
echo === 5/5  Enviando para o GitHub ===
git push -u origin main
if errorlevel 1 (
    echo.
    echo O repositorio ja tinha conteudo. Juntando com o que esta local...
    git pull origin main --allow-unrelated-histories --no-edit -X ours
    git push -u origin main
)

echo.
echo ============================================================
echo  PRONTO! Codigo enviado para:
echo  https://github.com/%REPO%
echo ============================================================
echo.
pause
