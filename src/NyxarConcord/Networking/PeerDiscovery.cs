using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NyxarConcord.Models;

namespace NyxarConcord.Networking;

/// <summary>
/// Descoberta de pares na rede local via UDP broadcast.
///
/// Cada instância anuncia periodicamente sua presença (nome, id e porta TCP do
/// servidor local) para toda a sub-rede. Ao mesmo tempo, escuta anúncios de
/// outros pares. Assim, sem nenhum servidor central, todas as máquinas na mesma
/// LAN se enxergam.
/// </summary>
public sealed class PeerDiscovery : IDisposable
{
    // Porta usada apenas para os anúncios de descoberta (não é a porta de chat).
    public const int DiscoveryPort = 47654;

    private readonly string _selfId;
    private readonly string _displayName;
    private readonly string _handle;
    private readonly int _tcpPort;

    private UdpClient? _listener;
    private UdpClient? _announcer;
    private CancellationTokenSource? _cts;

    /// <summary>Disparado quando um par é descoberto ou atualizado.</summary>
    public event Action<Peer>? PeerDiscovered;

    public PeerDiscovery(string selfId, string displayName, string handle, int tcpPort)
    {
        _selfId = selfId;
        _displayName = displayName;
        _handle = handle;
        _tcpPort = tcpPort;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();

        _listener = new UdpClient();
        _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

        _announcer = new UdpClient { EnableBroadcast = true };

        _ = ListenLoopAsync(_cts.Token);
        _ = AnnounceLoopAsync(_cts.Token);
    }

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        var payload = new DiscoveryPacket
        {
            Id = _selfId,
            Name = _displayName,
            Handle = _handle,
            Port = _tcpPort
        };
        var endpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
                await _announcer!.SendAsync(bytes, bytes.Length, endpoint);
            }
            catch
            {
                // Rede indisponível momentaneamente — ignora e tenta de novo.
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct).ContinueWith(_ => { });
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _listener!.ReceiveAsync(ct);
                var packet = JsonSerializer.Deserialize<DiscoveryPacket>(result.Buffer);
                if (packet is null || packet.Id == _selfId)
                    continue; // ignora a si mesmo

                var peer = new Peer
                {
                    Id = packet.Id,
                    DisplayName = packet.Name,
                    Handle = packet.Handle,
                    Address = result.RemoteEndPoint.Address,
                    Port = packet.Port,
                    LastSeen = DateTime.UtcNow
                };
                PeerDiscovered?.Invoke(peer);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Pacote malformado — ignora.
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _listener?.Dispose();
        _announcer?.Dispose();
        _cts?.Dispose();
    }

    private sealed class DiscoveryPacket
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Handle { get; set; } = "";
        public int Port { get; set; }
    }
}
