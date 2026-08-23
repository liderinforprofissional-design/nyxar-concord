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

        _room = room;
        _cts = new CancellationTokenSource();
        _ws = new ClientWebSocket();
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

    public Task SendToRoomAsync(ChatMessage m)
    {
        m.SenderId = _selfId;
        m.SenderName = _selfName;
        m.To = null;
        return SendAsync(m);
    }

    public Task SendToPeerAsync(string peerId, ChatMessage m)
    {
        m.SenderId = _selfId;
        m.SenderName = _selfName;
        m.To = peerId;
        return SendAsync(m);
    }

    private async Task SendAsync(ChatMessage m)
    {
        if (_ws is not { State: WebSocketState.Open }) return;
        // Loga tudo, menos o que é muito frequente (voz/tela/pedaços de arquivo),
        // mas conta a mídia periodicamente pra sabermos se ela está saindo.
        if (m.Signal == SignalType.VoiceFrame) { if (++_txVoice % 100 == 0) Diag.Log("MEDIA-TX", $"voz enviada x{_txVoice}"); }
        else if (m.Signal == SignalType.ScreenFrame) { if (++_txScreen % 30 == 0) Diag.Log("MEDIA-TX", $"tela enviada x{_txScreen}"); }
        else if (m.Signal != SignalType.FileChunk)
            Diag.Log("RELAY-TX", $"{m.Kind}/{m.Signal} to={m.To ?? "(sala)"} room={m.RoomId}");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(m);
        await _sendLock.WaitAsync();
        try { await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { }
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

            if (msg.Signal == SignalType.VoiceFrame) { if (++_rxVoice % 100 == 0) Diag.Log("MEDIA-RX", $"voz recebida de {from} x{_rxVoice}"); }
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
        _cts?.Cancel();
        _ws?.Dispose();
        _sendLock.Dispose();
    }
}
