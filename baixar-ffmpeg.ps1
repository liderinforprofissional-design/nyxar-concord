# Baixa o FFmpeg (build "shared" 64-bit, 8.x) e copia as DLLs para
# src\NyxarConcord\ffmpeg\ — necessário para o H264 do compartilhamento de tela.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $root 'src\NyxarConcord\ffmpeg'
$tmp  = Join-Path $env:TEMP 'nyxar_ffmpeg'

if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
New-Item -ItemType Directory -Force -Path $tmp | Out-Null
$zip = Join-Path $tmp 'ffmpeg.zip'

# Tenta 8.1, depois 8.0, depois master (todas ABI 8.x, avcodec-62).
$urls = @(
  'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.1-latest-win64-gpl-shared.zip',
  'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n8.0-latest-win64-gpl-shared.zip',
  'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip'
)

$ok = $false
foreach ($u in $urls) {
  try {
    Write-Host "Baixando: $u"
    Invoke-WebRequest -Uri $u -OutFile $zip -UseBasicParsing
    $ok = $true; break
  } catch {
    Write-Host "  (essa versao nao baixou, tentando a proxima...)"
  }
}
if (-not $ok) { throw "Nao consegui baixar o FFmpeg. Verifique a internet e tente de novo." }

Write-Host "Extraindo..."
Expand-Archive -Path $zip -DestinationPath $tmp -Force

# Pega os .dll da pasta bin da build.
$dlls = Get-ChildItem -Path $tmp -Recurse -Filter *.dll | Where-Object { $_.DirectoryName -like '*\bin' }
if (-not $dlls -or $dlls.Count -eq 0) { $dlls = Get-ChildItem -Path $tmp -Recurse -Filter *.dll }

New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($d in $dlls) { Copy-Item $d.FullName -Destination $dest -Force }

Write-Host ""
Write-Host "OK! $($dlls.Count) DLLs copiadas para:"
Write-Host "  $dest"
Remove-Item -Recurse -Force $tmp
