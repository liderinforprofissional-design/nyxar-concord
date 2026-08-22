# Voz + Tela por WebRTC — Nyxar Concord

A voz (fase 1) e agora a **tela** (fase 2) saem do relay e vão **ponto-a-ponto**
(WebRTC), usando o TURN do seu Worker para atravessar NAT. Chamadas e transmissões
ficam bem mais leves e escaláveis.

## Fase 2 — Tela por WebRTC (VP8)
- A faixa de vídeo (VP8) é negociada **junto** com a de áudio quando a chamada conecta,
  então começar/parar de compartilhar tela **não** exige renegociar.
- Novo pacote: **SIPSorceryMedia.Encoders 10.0.4** (codec VP8, com biblioteca nativa
  para Windows). Ao compilar, o Visual Studio baixa tudo.
- A captura vira BGR cru (`ScreenCaptureService.CaptureBgr`), é codificada em VP8 e
  enviada a cada par; no destino é decodificada e vira o quadro da "mini tela".
- Quando o WebRTC não está ativo (ex.: só LAN), a tela volta ao modo antigo (JPEG pelo relay).

> Obs.: o pacote de vídeo está marcado como "legado" pela SIPSorcery (eles sugerem o
> SIPSorceryMedia.FFmpeg). Para nosso caso (Windows), o Encoders é o mais simples e
> funciona; dá para migrar para FFmpeg depois se precisar de H264/melhor desempenho.

## O que mudou no código
- Novo `Services/WebRtcVoice.cs` — gerencia as conexões WebRTC em malha (mesh).
- `NyxarConcord.csproj` — adicionada a dependência **SIPSorcery 10.0.16**.
- `Models/ChatMessage.cs` — novos sinais `RtcOffer`, `RtcAnswer`, `RtcIce`.
- `ViewModels/MainViewModel.cs` — liga/desliga o WebRTC ao entrar/sair da voz,
  roteia a sinalização e envia/recebe o áudio.

Como funciona: a "apresentação" (SDP/ICE) viaja pelo relay que você já tem (`/ws`);
o áudio (PCMU 8 kHz) vai direto entre os pares (ou via TURN quando o NAT é fechado).
Reaproveita sua captura/supressão de ruído e a reprodução com mixagem.

## Pré-requisitos para funcionar pela internet
1. O Worker precisa estar publicado com o **TURN** ativo:
   ```
   wrangler secret put TURN_API_TOKEN
   wrangler deploy
   ```
   Teste abrindo `https://nyxar-signal.nyxarp2p.workers.dev/turn` — deve vir um JSON
   com `iceServers` (urls, username, credential).
2. Restaurar os pacotes NuGet no Visual Studio (ele baixa o SIPSorcery ao compilar).

## Como testar
- Abra o app em **dois computadores** (ou duas contas), entrem no **mesmo servidor**
  e no **mesmo canal de voz**. Devem se ouvir.
- Sem TURN (ou em teste na mesma máquina), o STUN já cobre redes abertas.

## Voltar atrás, se precisar
No `MainViewModel.cs`, mude a linha:
```csharp
private bool _useWebRtcVoice = true;
```
para `false`. Aí a voz volta a passar pelo relay (o modo antigo), sem tocar em mais nada.

## Aviso honesto
Isto é mídia em tempo real e **não deu para compilar/testar aqui** (Windows + biblioteca
nativa). É bem provável precisar de 1–2 ajustes ao rodar no seu PC. Se aparecer erro de
compilação ou a voz não sair, me manda a mensagem exata (ou o log da janela de saída do
Visual Studio) que eu corrijo rápido.

## Próxima fase (quando quiser)
- Migrar a **tela** para WebRTC (vídeo VP8/H264).
- Trocar o áudio para **Opus** (qualidade melhor que PCMU) via Concentus.
