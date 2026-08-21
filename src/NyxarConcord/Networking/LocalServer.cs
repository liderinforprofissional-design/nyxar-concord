using System.Net;
using System.Net.Sockets;
using NyxarConcord.Models;

namespace NyxarConcord.Networking;

/// <summary>
/// O "servidor local" de cada usuário. Cada máquina roda esta instância, que
/// escuta conexões TCP de outros pares. É o coração da arquitetura P2P: não há
/// servidor central — cada cliente também é servidor.
/// </summary>
public sealed class LocalServer : IDisposable
{
    private readonly string _selfId;
    private readonly string _displayName;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>Porta em que o servidor local está escutando (0 = escolhida pelo SO).</summary>
    public int Port { get; private set; }

    /// <summary>Disparado quando um par se conecta a nós.</summary>
    public event Action<PeerConnection>? PeerConnected;

    public LocalServer(string selfId, string displayName, int preferredPort = 0)
    {
        _selfId = selfId;
        _displayName = displayName;
        Port = preferredPort;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, Port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await _listener!.AcceptTcpClientAsync(ct);
                var connection = new PeerConnection(client, _selfId, _displayName);
                PeerConnected?.Invoke(connection);
                connection.StartReceiving();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Falha ao aceitar — continua o loop.
            }
        }
    }

    /// <summary>Conecta-se ativamente ao servidor local de outro par.</summary>
    public async Task<PeerConnection> ConnectToAsync(Peer peer, CancellationToken ct = default)
    {
        var client = new TcpClient();
        await client.ConnectAsync(peer.Address, peer.Port, ct);
        var connection = new PeerConnection(client, _selfId, _displayName) { RemotePeer = peer };
        connection.StartReceiving();
        await connection.SendHelloAsync();
        return connection;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _cts?.Dispose();
    }
}
