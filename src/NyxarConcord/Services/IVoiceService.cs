using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Chamadas de voz P2P (VoIP).
///
/// PLANO DE IMPLEMENTAÇÃO:
///  1. Capturar microfone com NAudio (WasapiCapture / WaveInEvent).
///  2. Codificar em Opus (pacote Concentus) para baixa latência.
///  3. Enviar os frames por UDP direto ao par (RTP simples ou datagramas próprios).
///  4. No receptor: decodificar Opus e tocar com WasapiOut / WaveOutEvent.
///  5. Usar o canal TCP existente (MessageKind.Signal) para negociar início/fim
///     da chamada e trocar a porta UDP de mídia.
///
/// Para chamadas pela internet, combine com <see cref="INatTraversalService"/>.
/// </summary>
public interface IVoiceService
{
    bool IsInCall { get; }

    Task StartCallAsync(Peer peer, CancellationToken ct = default);
    Task EndCallAsync();

    void Mute(bool muted);

    event Action<Peer>? CallStarted;
    event Action<Peer>? CallEnded;
}

/// <summary>Stub temporário — substitua pela implementação real com NAudio + Opus.</summary>
public sealed class VoiceServiceStub : IVoiceService
{
    public bool IsInCall { get; private set; }

    public event Action<Peer>? CallStarted;
    public event Action<Peer>? CallEnded;

    public Task StartCallAsync(Peer peer, CancellationToken ct = default)
    {
        IsInCall = true;
        CallStarted?.Invoke(peer);
        throw new NotImplementedException("VoIP ainda não implementado. Ver plano nesta interface.");
    }

    public Task EndCallAsync()
    {
        IsInCall = false;
        return Task.CompletedTask;
    }

    public void Mute(bool muted) { }
}
