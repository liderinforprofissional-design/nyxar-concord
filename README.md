# Nyxar Concord

Um "Discord" com arquitetura **peer-to-peer (P2P)**: cada máquina roda seu próprio
**servidor local**, então não há servidor central. Os usuários se descobrem na rede
e conversam diretamente entre si. App **Windows** nativo em **C# / .NET 8 + WPF**.

## Como abrir no Visual Studio

1. Abra o **Visual Studio 2022** (precisa do workload **".NET desktop development"** e do **.NET 8 SDK**).
2. `Arquivo → Abrir → Projeto/Solução` e selecione **`NyxarConcord.sln`** (ou dê duplo-clique no arquivo).
3. Pressione **F5** para compilar e rodar.
4. Ao iniciar, escolha um nome. Rode o app em **duas ou mais máquinas na mesma rede**
   (ou duas instâncias no mesmo PC) e elas se enxergam automaticamente na barra lateral.

> Dica: para testar no mesmo PC, inicie o app duas vezes (`Ctrl+F5` numa, `F5` noutra).

## O que já funciona

- **Identidade persistente** — cada usuário tem um `peer_id` estável, salvo em
  `%AppData%\NyxarConcord\identity.json` e reutilizado entre sessões.
- **Descoberta de pares na LAN** — via UDP broadcast, sem configuração.
- **Servidor local por usuário** — cada instância escuta conexões TCP (P2P real).
- **Chat de texto em tempo real** — mensagens NDJSON direto entre pares.
- **Conexão pela internet** — código de convite (`InternetInviteDialog`) para conectar
  fora da LAN (requer IP público/port forwarding hoje; NAT traversal automático é o próximo passo).
- **Configurações** — ícone de engrenagem: escolher nome, ver o peer_id e selecionar
  dispositivos de **áudio de entrada e saída** (enumerados via NAudio/WASAPI).
- **Salas de áudio e texto** — ícone **+** cria salas. Sala de áudio = "call".
- **Controles de call** — convidar alguém (boneco **+**), sair da call (telefone
  **vermelho**) e compartilhar tela (monitor, **só habilitado quando você está em call**).
- **Seletor de compartilhamento** — lista todos os **monitores** e **janelas de
  apps/jogos** abertos no Windows (via P/Invoke `EnumWindows`).
- **UI escura estilo Discord** — barra de ícones à esquerda, contatos, salas, badges.

## Em desenvolvimento (a estrutura/UI já existe; falta o transporte de mídia real)

| Recurso | Arquivo | Estado | Caminho recomendado |
|---|---|---|---|
| Voz (VoIP) | `IVoiceService.cs` | UI + salas prontas; captura/stream é stub | NAudio (captura) + Concentus/Opus (codec) + UDP |
| Compartilhamento de tela | `IScreenShareService.cs` | seletor + estado prontos; envio de quadros é stub | Windows.Graphics.Capture + H.264 |
| Transferência de arquivos | `IFileTransferService.cs` | interface pronta | Socket TCP dedicado + hash SHA-256 |
| NAT traversal (internet auto) | `INatTraversalService.cs` | interface pronta | STUN + UDP hole punching + TURN (fallback) |

> Os botões de voz e tela já funcionam na interface (entram na call, marcam estado,
> avisam os outros por sinal). O que falta é a captura e o transporte real de
> áudio/vídeo — o próximo passo de implementação. A rota mais rápida para fazer voz +
> tela pela internet de uma vez é a biblioteca **SIPSorcery** (WebRTC/ICE completo).

Para voz + tela + NAT pela internet de uma vez, a rota mais rápida é a biblioteca
**SIPSorcery** (WebRTC/ICE completo). As dependências já estão comentadas no
`.csproj` — é só descomentar quando for implementar.

## Estrutura do projeto

```
NyxarConcord.sln
└── src/NyxarConcord/
    ├── Models/          Peer, ChatMessage, UserIdentity, Room
    ├── Networking/      PeerDiscovery, LocalServer, PeerConnection, ChatSession, InviteCode
    ├── Services/        IdentityService, AudioDeviceService, ScreenSourceService,
    │                    interfaces de voz/arquivos/tela/NAT (+ stubs)
    ├── ViewModels/      MainViewModel, SettingsViewModel, PeerViewModel (MVVM)
    ├── Views/           MainWindow, SettingsWindow, CreateRoomDialog,
    │                    ScreenSharePicker, InvitePeerDialog, InternetInviteDialog, NameDialog
    └── Themes/          DarkTheme.xaml
```

## Como a rede funciona

1. Ao abrir, cada instância sobe um **servidor TCP local** numa porta livre.
2. A cada 3s ela **anuncia** (UDP broadcast na porta 47654) seu id, nome e porta TCP.
3. Todos na mesma sub-rede recebem o anúncio e adicionam o par à lista.
4. Ao enviar a primeira mensagem, abre-se uma **conexão TCP direta** com o par.
5. Mensagens trafegam como JSON (uma por linha) por essa conexão — ponto a ponto.

## Roadmap sugerido

1. **Persistência** — salvar histórico de conversa em SQLite local.
2. **Grupos/"servidores"** — salas com vários pares (mesh ou eleição de host).
3. **Voz** → **Arquivos** → **Tela** (nesta ordem de dificuldade).
4. **NAT traversal** para conectar fora da LAN.
5. **Criptografia** — handshake TLS/Noise entre pares (importante antes de sair da LAN).

## Requisitos

- Windows 10 (1803+) ou Windows 11
- .NET 8 SDK
- Visual Studio 2022 (workload *.NET desktop development*)
