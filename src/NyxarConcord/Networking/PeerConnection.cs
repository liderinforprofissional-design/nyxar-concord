using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NyxarConcord.Models;

namespace NyxarConcord.Networking;

/// <summary>
/// Uma conexão TCP viva com um par. Envia e recebe <see cref="ChatMessage"/>
/// em formato NDJSON (um JSON por linha).
/// </summary>
public sealed class PeerConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly string _selfId;
    private readonly string _selfName;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _cts;

    public Peer? RemotePeer { get; set; }

    /// <summary>Disparado ao receber uma mensagem do par.</summary>
    public event Action<PeerConnection, ChatMessage>? MessageReceived;

    /// <summary>Disparado quando a conexão cai.</summary>
    public event Action<PeerConnection>? Disconnected;

    public PeerConnection(TcpClient client, string selfId, string selfName)
    {
        _client = client;
        _selfId = selfId;
        _selfName = selfName;
        _stream = client.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8);
        _writer = new StreamWriter(_stream, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void StartReceiving()
    {
        _cts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_cts.Token);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line = await _reader.ReadLineAsync(ct);
                if (line is null) break; // conexão encerrada
                if (string.IsNullOrWhiteSpace(line)) continue;

                var msg = JsonSerializer.Deserialize<ChatMessage>(line);
                if (msg is null) continue;

                if (msg.Kind == MessageKind.Hello)
                    RemotePeer ??= new Peer { Id = msg.SenderId, DisplayName = msg.SenderName };

                MessageReceived?.Invoke(this, msg);
            }
        }
        catch
        {
            // Erro de leitura = desconexão.
        }
        finally
        {
            Disconnected?.Invoke(this);
        }
    }

    public Task SendHelloAsync() => SendAsync(new ChatMessage
    {
        Kind = MessageKind.Hello,
        SenderId = _selfId,
        SenderName = _selfName
    });

    public Task SendTextAsync(string text) => SendAsync(new ChatMessage
    {
        Kind = MessageKind.Text,
        SenderId = _selfId,
        SenderName = _selfName,
        Text = text
    });

    public async Task SendAsync(ChatMessage message)
    {
        // Serializa as escritas: voz, tela e chat vêm de threads diferentes.
        await _sendLock.WaitAsync();
        try
        {
            string json = JsonSerializer.Serialize(message);
            await _writer.WriteLineAsync(json);
        }
        catch
        {
            Disconnected?.Invoke(this);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _reader.Dispose();
        _writer.Dispose();
        _client.Dispose();
        _cts?.Dispose();
        _sendLock.Dispose();
    }
}
