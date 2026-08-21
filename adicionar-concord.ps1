# ============================================================
#  Adiciona "Concord" dentro do balão da sua logo Nyxar.
#  NÃO recria a logo — apenas escreve o texto sobre o seu PNG.
#
#  Como usar (uma das opções):
#   1) Clique com o botão direito neste arquivo -> "Executar com o PowerShell"
#      (ele vai pedir o caminho da sua logo)
#   2) Ou no PowerShell:
#        .\adicionar-concord.ps1 -In "C:\caminho\sua-logo.png"
#
#  Ajustes finos (opcionais):
#   -Texto "Concord"   -> muda a palavra
#   -Y 0.63            -> altura (0 = topo, 1 = base). Suba/desça o texto
#   -Tamanho 0.085     -> tamanho da fonte (fração da altura da imagem)
#   -Fonte "Segoe UI"  -> troque por "Arial Rounded MT Bold" se tiver
# ============================================================
param(
  [string]$In = "",
  [string]$Out = "",
  [string]$Texto = "Concord",
  [double]$Y = 0.63,
  [double]$Tamanho = 0.085,
  [string]$Fonte = "Segoe UI"
)

Add-Type -AssemblyName System.Drawing

if (-not $In) { $In = Read-Host "Arraste sua logo aqui (ou cole o caminho do PNG) e tecle Enter" }
$In = $In.Trim('"')
if (-not (Test-Path $In)) { Write-Host "Arquivo não encontrado: $In" -ForegroundColor Red; Read-Host "Enter para sair"; exit }
if (-not $Out) { $Out = [IO.Path]::Combine([IO.Path]::GetDirectoryName($In), "nyxar-concord.png") }

$img = [System.Drawing.Image]::FromFile($In)
$bmp = New-Object System.Drawing.Bitmap $img
$g   = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

$w = $bmp.Width; $h = $bmp.Height
$fontSize = [single]([Math]::Max(10, $h * $Tamanho))
$font = New-Object System.Drawing.Font($Fonte, $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)

$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment     = [System.Drawing.StringAlignment]::Center
$fmt.LineAlignment = [System.Drawing.StringAlignment]::Center

$cy = [single]($h * $Y)
$rect   = New-Object System.Drawing.RectangleF(0, ($cy - $fontSize), [single]$w, ($fontSize * 2))
$shadowR= New-Object System.Drawing.RectangleF(3, ($cy - $fontSize + 3), [single]$w, ($fontSize * 2))

# sombra suave para dar profundidade (como o "Nyxar")
$shadow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(70, 5, 60, 120))
$g.DrawString($Texto, $font, $shadow, $shadowR, $fmt)
# texto branco
$white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
$g.DrawString($Texto, $font, $white, $rect, $fmt)

$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose(); $img.Dispose()

Write-Host ""
Write-Host "Pronto! Logo salva em:" -ForegroundColor Green
Write-Host "  $Out"
Write-Host "Se o 'Concord' ficou alto/baixo demais, rode de novo mudando -Y (ex.: -Y 0.66) ou -Tamanho."
Read-Host "Enter para sair"
