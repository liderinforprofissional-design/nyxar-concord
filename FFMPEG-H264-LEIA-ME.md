# Tela em H264 (FFmpeg) — Nyxar Concord

O compartilhamento de tela agora usa **H264** (via FFmpeg), com bitrate alto
(~3,5 Mbps) para o 720p ficar nítido. Isso exige as **bibliotecas nativas do FFmpeg**
junto do app — é uma vez só de configuração.

## Jeito fácil (recomendado): script automático
Duplo-clique em **`baixar-ffmpeg.bat`**. Ele baixa a build correta do FFmpeg
(**shared, 64-bit, 8.x**) e copia os `.dll` para `src\NyxarConcord\ffmpeg\` sozinho.
Depois é só recompilar o app.

> Importante: o pacote usado (SIPSorceryMedia.FFmpeg 10.0.16) precisa do **FFmpeg 8.x**
> (não 7.x). O script já pega a versão certa.

## Jeito manual (se preferir)
1. Baixe uma build **shared** do FFmpeg **8.x** para Windows x64
   (ex.: https://github.com/BtbN/FFmpeg-Builds/releases → `ffmpeg-n8.x-...-win64-gpl-shared.zip`).
   Precisa ser **shared** (tem uma pasta `bin` cheia de `.dll`) e **GPL/full** (tem o H264).
2. Copie **todos os `.dll`** da pasta `bin` para:
   ```
   C:\Users\Roberto\source\repos\Nyxar Concord\src\NyxarConcord\ffmpeg\
   ```
   (São arquivos como `avcodec-62.dll`, `avformat-62.dll`, `avutil-60.dll`,
   `swscale-9.dll`, `swresample-6.dll`, `avfilter-11.dll`, `avdevice-62.dll`.)

Em ambos os casos, o `.csproj` já tem uma regra que **copia a pasta `ffmpeg\` para a
saída** ao compilar — então as DLLs vão junto no debug, no `dotnet publish` e no
`.zip`/instalador do release.

## 3. Compilar
Abra no Visual Studio e recompile. Na primeira chamada de vídeo, o app inicializa o
FFmpeg apontando para a subpasta `ffmpeg` ao lado do executável.

## Como testar
Dois PCs, mesmo servidor e canal de voz, um compartilha a tela. Deve aparecer nítido
em 720p e bem mais leve que antes (vai ponto-a-ponto / TURN, não pelo relay).

## Ajustes rápidos (se quiser)
No `Services/WebRtcVoice.cs`:
- Nitidez/banda: `VideoAvgBitrate` (padrão 3.500.000) e `VideoMaxBitrate` (6.000.000).
- A taxa de quadros vem do timer em `MainViewModel.StartScreenShareAsync`
  (`Interval`). Para tela mais fluida, reduza o intervalo (ex.: 100 ms = ~10 fps).

## Se não achar as DLLs
Se a pasta `ffmpeg\` não existir, o app tenta usar o FFmpeg do PATH do sistema; se não
houver, o **áudio continua funcionando** e só a tela por H264 fica indisponível
(sem travar o app).

## Observação
O pacote `SIPSorceryMedia.FFmpeg` é o caminho oficial para H264 em .NET. As DLLs
pesam algumas dezenas de MB e entram no `.zip` do release — é o custo de ter H264.
Se um dia quiser um app mais leve, dá para voltar ao VP8 (não precisa de DLLs).
