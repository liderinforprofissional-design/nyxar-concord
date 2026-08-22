# Tela em H264 (FFmpeg) — Nyxar Concord

O compartilhamento de tela agora usa **H264** (via FFmpeg), com bitrate alto
(~3,5 Mbps) para o 720p ficar nítido. Isso exige as **bibliotecas nativas do FFmpeg**
junto do app — é uma vez só de configuração.

## 1. Baixar o FFmpeg (build "shared", 64-bit, versão 7.x)
Baixe uma build **shared** do FFmpeg 7.x para Windows x64. Opções:
- https://www.gyan.dev/ffmpeg/builds/  → pegue "ffmpeg-release-full-shared" (7.x)
- ou https://github.com/BtbN/FFmpeg-Builds/releases → "ffmpeg-n7.x-win64-gpl-shared"

> Precisa ser **shared** (tem uma pasta `bin` cheia de `.dll`). A versão "static"
> não serve. Use a **GPL/full** para ter o codificador H264 (libx264).

## 2. Copiar as DLLs para o projeto
Descompacte e copie **todos os `.dll`** da pasta `bin` do FFmpeg para:
```
C:\Users\Roberto\source\repos\Nyxar Concord\src\NyxarConcord\ffmpeg\
```
(São arquivos como `avcodec-61.dll`, `avformat-61.dll`, `avutil-59.dll`,
`swscale-8.dll`, `swresample-5.dll`, `avfilter-10.dll`, `avdevice-61.dll`.)

O `.csproj` já tem uma regra que **copia essa pasta `ffmpeg\` para a saída** ao
compilar — então elas vão junto no debug e no `dotnet publish` (e no `.zip` do release).

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
