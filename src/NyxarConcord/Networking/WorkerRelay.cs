using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
using NyxarConcord.Models;
using NyxarConcord.Services;

namespace NyxarConcord.Networking;

/// <summary>
/// Conexão com o Cloudflare Worker de retransmissão. Cada "sala" do relay é um
/// servidor (guild): ao entrar, todos os membros trocam mensagens pela nuvem,
/// funcionando pela internet sem configurar roteador.
/// </summary>
public sealed class WorkerRelay : IDisposable
{
    // Ajuste aqui se você reimplantar o Worker em outra URL.
    public const string WsBase = "wss://nyxar-signal.nyxarp2p.workers.dev/ws";
    public const string TurnUrl = "https://nyxar-signal.nyxarp2p.workers.dev/turn";

    private readonly string _selfId;
    private readonly string _selfName;
    private readonly string _handle;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private string? _room;
    private volatile bool _closing;   // true quando o fechamento é intencional (não reconectar)
    private int _reconnecting;         // 0/1 — garante um único laço de reconexão

    // Contadores de mídia (voz/tela) para o diagnóstico saber se a mídia cruza a rede.
    private int _txVoice, _txScreen, _rxVoice, _rxScreen;

    public bool IsConnected => _ws is { State: WebSocketState.Open };
    public string? CurrentRoom => _room;

    /// <summary>Mensagem do app recebida (fromPeerId, mensagem).</summary>
    public event Action<string, ChatMessage>? MessageReceived;
    /// <summary>Um par se apresentou na sala (id, nome, handle).</summary>
    public event Action<string, string, string>? PeerHello;
    /// <summary>Um par saiu da sala.</summary>
    public event Action<string>? PeerLeft;
    /// <summary>Reconectou ao relay depois de uma queda (internet caiu / mudou de rede).</summary>
    public event Action? Reconnected;

    public WorkerRelay(string selfId, string selfName, string handle)
    {
        _selfId = selfId;
        _selfName = selfName;
        _handle = handle;
    }

    public async Task JoinRoomAsync(string room)
    {
        if (_room == room && IsConnected) return;
        await DisconnectAsync();

        _closing = false;
        _room = room;
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15); // detecta queda mais rápido
        var uri = new Uri($"{WsBase}?room={Uri.EscapeDataString(room)}&peer={Uri.EscapeDataString(_selfId)}");
        Diag.Log("RELAY", $"Conectando à sala do relay: {room}");
        try
        {
            await _ws.ConnectAsync(uri, _cts.Token);
            Diag.Log("RELAY", $"Conectado ao relay (sala {room})");
            _ = ReceiveLoopAsync(_cts.Token);
            await SendHelloAsync();
        }
        catch (Exception ex)
        {
            Diag.Log("RELAY", $"Falha ao conectar no relay: {ex.Message}");
            // sem internet / worker indisponível — segue só na LAN
        }
    }

    private Task SendHelloAsync() => SendAsync(new ChatMessage
    {
        Kind = MessageKind.Hello, SenderId = _selfId, SenderName = _selfName, Handle = _handle
    });

    public Task SendToRoomAsync(ChatMessage m, bool lowPriority = false)
    {
        m.SenderId = _selfId;
        m.SenderName = _selfName;
        m.To = null;
        return SendAsync(m, lowPriority);
    }

    public Task SendToPeerAsync(string peerId, ChatMessage m)
    {
        m.SenderId = _selfId;
        m.SenderName = _selfName;
        m.To = peerId;
        return SendAsync(m);
    }

    private async Task SendAsync(ChatMessage m, bool lowPriority = false)
    {
        // Não dispara reconexão aqui: durante estados transitórios do socket isso
        // gerava reconexões falsas e conexões duplicadas no relay (os pares se
        // "expulsavam"). A reconexão é decidida só quando o ReceiveLoop cai de fato.
        if (_ws is not { State: WebSocketState.Open }) return;
        // Loga tudo, menos o que é muito frequente (voz/tela/pedaços de arquivo),
        // mas conta a mídia periodicamente pra sabermos se ela está saindo.
        if (m.Signal is SignalType.VoiceFrame or SignalType.ScreenAudioFrame) { if (++_txVoice % 100 == 0) Diag.Log("MEDIA-TX", $"voz enviada x{_txVoice}"); }
        else if (m.Signal == SignalType.ScreenFrame) { if (++_txScreen % 30 == 0) Diag.Log("MEDIA-TX", $"tela enviada x{_txScreen}"); }
        else if (m.Signal != SignalType.FileChunk)
            Diag.Log("RELAY-TX", $"{m.Kind}/{m.Signal} to={m.To ?? "(sala)"} room={m.RoomId}");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(m);

        // Baixa prioridade (vídeo da tela): se o canal está ocupado (áudio/voz enviando),
        // descarta este quadro em vez de esperar. Assim o áudio nunca fica preso atrás do vídeo.
        if (lowPriority)
        {
            if (!await _sendLock.WaitAsync(0)) return;
        }
        else await _sendLock.WaitAsync();
        try { await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { /* o ReceiveLoop detecta a queda e reconecta */ }
        finally { _sendLock.Release(); }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var segment = new ArraySegment<byte>(new byte[64 * 1024]);
        using var ms = new MemoryStream();
        try
        {
            while (!ct.IsCancellationRequested && _ws!.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(segment, ct);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    ms.Write(segment.Array!, segment.Offset, result.Count);
                }
                while (!result.EndOfMessage);

                Handle(ms.ToArray());
            }
        }
        catch { /* conexão caiu */ }
        // Saiu do laço: se não foi fechamento intencional, tenta reconectar.
        if (!_closing) TriggerReconnect();
    }

    // Dispara (uma única vez) o laço de reconexão automática.
    private void TriggerReconnect()
    {
        if (_closing || _room is null) return;
        if (System.Threading.Interlocked.Exchange(ref _reconnecting, 1) == 1) return;
        _ = ReconnectLoopAsync();
    }

    // Reconecta ao relay com backoff. Ao reconectar, reapresenta-se (Hello) e avisa a UI.
    private async Task ReconnectLoopAsync()
    {
        int delayMs = 1000;
        try
        {
            while (!_closing && _room is not null && !IsConnected)
            {
                try { await Task.Delay(delayMs); } catch { }
                if (_closing || _room is null || IsConnected) break;

                try
                {
                    var old = _ws;
                    try { old?.Abort(); old?.Dispose(); } catch { }

                    var cts = new CancellationTokenSource();
                    var ws = new ClientWebSocket();
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    var uri = new Uri($"{WsBase}?room={Uri.EscapeDataString(_room!)}&peer={Uri.EscapeDataString(_selfId)}");
                    Diag.Log("RELAY", "Tentando reconectar ao relay…");
                    await ws.ConnectAsync(uri, cts.Token);
                    _cts = cts;
                    _ws = ws;
                    _ = ReceiveLoopAsync(cts.Token);
                    await SendHelloAsync();       // me reapresento para a sala
                    Diag.Log("RELAY", "Reconectado ao relay.");
                    Reconnected?.Invoke();         // a UI reanuncia presença/transmissão
                    break;
                }
                catch (Exception ex)
                {
                    Diag.Log("RELAY", $"Reconexão falhou: {ex.Message}");
                    delayMs = Math.Min(delayMs * 2, 5000); // backoff até 5s
                }
            }
        }
        finally { _reconnecting = 0; }
    }

    private void Handle(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            // Mensagens de controle do Worker têm "type".
            if (root.TryGetProperty("type", out var typeEl))
            {
                string? type = typeEl.GetString();
                Diag.Log("RELAY-RX", $"ctrl={type}");
                if (type == "join")
                    _ = SendHelloAsync(); // me apresento a quem acabou de entrar
                else if (type == "leave" && root.TryGetProperty("from", out var f))
                    PeerLeft?.Invoke(f.GetString() ?? "");
                return;
            }

            var msg = JsonSerializer.Deserialize<ChatMessage>(data);
            if (msg is null) return;
            string from = string.IsNullOrEmpty(msg.From) ? msg.SenderId : msg.From!;
            if (from == _selfId) return; // ignora eco

            if (msg.Signal is SignalType.VoiceFrame or SignalType.ScreenAudioFrame) { if (++_rxVoice % 100 == 0) Diag.Log("MEDIA-RX", $"voz recebida de {from} x{_rxVoice}"); }
            else if (msg.Signal == SignalType.ScreenFrame) { if (++_rxScreen % 30 == 0) Diag.Log("MEDIA-RX", $"tela recebida de {from} x{_rxScreen}"); }
            else if (msg.Signal != SignalType.FileChunk)
                Diag.Log("RELAY-RX", $"{msg.Kind}/{msg.Signal} from={from} to={msg.To ?? "(sala)"} room={msg.RoomId}");

            if (msg.Kind == MessageKind.Hello)
                PeerHello?.Invoke(from, msg.SenderName, msg.Handle ?? "");
            else
                MessageReceived?.Invoke(from, msg);
        }
        catch { }
    }

    public async Task DisconnectAsync()
    {
        _closing = true; // fechamento intencional: não reconectar
        _cts?.Cancel();
        if (_ws is not null)
        {
            try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            _ws.Dispose();
            _ws = null;
        }
        _room = null;
    }

    public void Dispose()
    {
        _closing = true;
        _cts?.Cancel();
        _ws?.Dispose();
        _sendLock.Dispose();
    }
}
