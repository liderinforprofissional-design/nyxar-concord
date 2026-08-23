# ============================================================
#  Compila a rnnoise.dll (x64) do zero e coloca em
#  src\NyxarConcord\rnnoise\ . Só precisa rodar se quiser
#  recompilar — a DLL pronta já vem no projeto.
#
#  Requisitos: git + compilador MinGW-w64 (x86_64-w64-mingw32-gcc).
#  Se não tiver o MinGW, instale o MSYS2 (https://www.msys2.org) e, no
#  terminal MSYS2, rode:  pacman -S mingw-w64-x86_64-gcc git
#  Depois use o gcc de C:\msys64\mingw64\bin no PATH.
# ============================================================
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $root "src\NyxarConcord\rnnoise"
$work = Join-Path $env:TEMP "rnnoise-build"

# Acha um gcc do MinGW (64 bits).
$gcc = $null
foreach ($c in @("x86_64-w64-mingw32-gcc", "gcc")) {
    $p = Get-Command $c -ErrorAction SilentlyContinue
    if ($p) { $gcc = $p.Source; break }
}
if (-not $gcc -and (Test-Path "C:\msys64\mingw64\bin\gcc.exe")) { $gcc = "C:\msys64\mingw64\bin\gcc.exe" }
if (-not $gcc) { Write-Host "[ERRO] MinGW-w64 (gcc x64) nao encontrado. Instale o MSYS2 e o mingw-w64-x86_64-gcc." -ForegroundColor Red; exit 1 }
Write-Host "Usando gcc: $gcc"

# Baixa a versao classica (que ja traz o modelo treinado no repo).
if (Test-Path $work) { Remove-Item -Recurse -Force $work }
git clone https://github.com/xiph/rnnoise "$work" 2>&1 | Out-Null
Push-Location $work
git checkout -q cdf196b   # commit com src\rnn_data.c incluido (sem download de modelo)

# Compila a DLL (C puro; sem dependencias alem de KERNEL32/msvcrt).
$srcs = @("src\celt_lpc.c","src\kiss_fft.c","src\pitch.c","src\rnn.c","src\rnn_data.c","src\denoise.c")
& $gcc -O2 -DNDEBUG -DWIN32 -DRNNOISE_BUILD -DDLL_EXPORT -Iinclude -Isrc -shared -static-libgcc -o rnnoise.dll $srcs -lm
if ($LASTEXITCODE -ne 0) { Pop-Location; Write-Host "[ERRO] Falha ao compilar." -ForegroundColor Red; exit 1 }
Pop-Location

New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item (Join-Path $work "rnnoise.dll") (Join-Path $dest "rnnoise.dll") -Force
Write-Host "OK! rnnoise.dll gerada em: $dest" -ForegroundColor Green
Write-Host "Agora e so recompilar/republicar o app. O supressor RNNoise ativa sozinho."
