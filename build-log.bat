@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo Limpando build anterior...
if exist "src\NyxarConcord\obj" rmdir /s /q "src\NyxarConcord\obj"
if exist "src\NyxarConcord\bin" rmdir /s /q "src\NyxarConcord\bin"
echo Compilando Nyxar Concord... isso pode levar 1-2 minutos.
echo ============================================================ > build-log.txt
dotnet build "src\NyxarConcord\NyxarConcord.csproj" -v minimal >> build-log.txt 2>&1
echo.
echo Pronto! O resultado foi salvo em build-log.txt (na mesma pasta).
echo Se aparecerem erros, me envie o arquivo build-log.txt.
echo.
pause
