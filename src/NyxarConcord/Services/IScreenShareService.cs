using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Compartilhamento de tela P2P.
///
/// PLANO DE IMPLEMENTAÇÃO:
///  1. Capturar a tela com Windows.Graphics.Capture (Windows 10 1803+) ou
///     BitBlt/DXGI Desktop Duplication para melhor desempenho.
///  2. Codificar os quadros. Opções:
///       - Simples: JPEG por quadro (fácil, largura de banda alta).
///       - Melhor: H.264 via Media Foundation ou FFmpeg (baixa largura de banda).
///  3. Enviar os quadros por UDP/RTP ao par (mesma ideia do vídeo em VoIP).
///  4. No receptor: decodificar e exibir num Image/D3DImage numa janela.
///
/// A biblioteca SIPSorcery (WebRTC) já cobre captura + codec + transporte + NAT,
/// sendo o caminho mais rápido para voz E tela juntos pela internet.
/// </summary>
public interface IScreenShareService
{
    bool IsSharing { get; }

    Task StartSharingAsync(Peer peer, int monitorIndex = 0, CancellationToken ct = default);
    Task StopSharingAsync();

    event Action<Peer>? SharingStarted;
    event Action<Peer>? SharingStopped;
}

/// <summary>Stub temporário — substitua pela captura + codec + transporte reais.</summary>
public sealed class ScreenShareServiceStub : IScreenShareService
{
    public bool IsSharing { get; private set; }

    public event Action<Peer>? SharingStarted;
    public event Action<Peer>? SharingStopped;

    public Task StartSharingAsync(Peer peer, int monitorIndex = 0, CancellationToken ct = default)
    {
        IsSharing = true;
        SharingStarted?.Invoke(peer);
        return Task.CompletedTask; // ainda não transmite quadros
    }

    public Task StopSharingAsync()
    {
        IsSharing = false;
        return Task.CompletedTask;
    }
}
