using System.Collections.Concurrent;
using NyxarConcord.Models;

namespace NyxarConcord.Networking;

/// <summary>
/// Orquestra toda a camada P2P para este usuário: identidade persistente,
/// descoberta na LAN, servidor local, conexões ativas e sinais de sala.
/// A UI conversa só com esta classe.
/// </summary>
public sealed class ChatSession : IDisposable
{
    public UserIdentity Identity { get; }
    public string SelfId => Identity.PeerId;
    public string DisplayName => Identity.DisplayName;

    private readonly LocalServer _server;
    private readonly PeerDiscovery _discovery;
    private readonly ConcurrentDictionary<string, PeerConnection> _connections = new();
    private readonly ConcurrentDictionary<string, Peer> _knownPeers = new();

    /// <summary>Par descoberto/atualizado na rede.</summary>
    public event Action<Peer>? PeerUpdated;

    /// <summary>Mensagem de texto recebida de qualquer par.</summary>
    public event Action<Peer, ChatMessage>? MessageReceived;

    /// <summary>Sinal recebido (convite de sala, screen share, etc.).</summary>
    public event Action<Peer, ChatMessage>? SignalReceived;

    public int LocalPort => _server.Port;

    public ChatSession(UserIdentity identity)
    {
        Identity = identity;

        _server = new LocalServer(SelfId, DisplayName);
        _server.Start(); // reserva a porta antes de anunciar

        _discovery = new PeerDiscovery(SelfId, DisplayName, Identity.Handle, _server.Port);

        _server.PeerConnected += OnPeerConnected;
        _discovery.PeerDiscovered += OnPeerDiscovered;
    }

    public void Start() => _discovery.Start();

    private void OnPeerDiscovered(Peer peer)
    {
        _knownPeers[peer.Id] = peer;
        PeerUpdated?.Invoke(peer);
    }

    private void OnPeerConnected(PeerConnection conn) => WireConnection(conn);

    private void WireConnection(PeerConnection conn)
    {
        conn.MessageReceived += (c, msg) =>
        {
            if (c.RemotePeer is null) return;
            _connections[c.RemotePeer.Id] = c;
            _knownPeers[c.RemotePeer.Id] = c.RemotePeer;

            switch (msg.Kind)
            {
                case MessageKind.Text:
                    MessageReceived?.Invoke(c.RemotePeer, msg);
                    break;
                case MessageKind.Signal:
                    SignalReceived?.Invoke(c.RemotePeer, msg);
                    break;
            }
        };
        conn.Disconnected += c =>
        {
            if (c.RemotePeer is not null)
                _connections.TryRemove(c.RemotePeer.Id, out _);
        };
    }

    /// <summary>Garante uma conexão TCP com o par (conecta se ainda não houver).</summary>
    public async Task<PeerConnection> EnsureConnectionAsync(Peer peer)
    {
        if (_connections.TryGetValue(peer.Id, out var existing))
            return existing;

        var conn = await _server.ConnectToAsync(peer);
        WireConnection(conn);
        _connections[peer.Id] = conn;
        return conn;
    }

    public async Task SendTextAsync(Peer peer, string text)
    {
        var conn = await EnsureConnectionAsync(peer);
        await conn.SendTextAsync(text);
    }

    public async Task SendSignalAsync(Peer peer, ChatMessage signal)
    {
        signal.Kind = MessageKind.Signal;
        signal.SenderId = SelfId;
        signal.SenderName = DisplayName;
        var conn = await EnsureConnectionAsync(peer);
        await conn.SendAsync(signal);
    }

    // --- Conexão pela internet via código de convite ---

    /// <summary>Gera um código de convite com o endpoint público/porta deste usuário.</summary>
    public string CreateInviteCode(string publicHost) =>
        InviteCode.Encode(new InvitePayload
        {
            PeerId = SelfId,
            Name = DisplayName,
            Host = publicHost,
            Port = LocalPort
        });

    /// <summary>Conecta-se a um par fora da LAN a partir de um código de convite.</summary>
    public async Task<Peer?> ConnectByInviteAsync(string code)
    {
        var payload = InviteCode.Decode(code);
        if (payload is null) return null;

        var peer = new Peer
        {
            Id = payload.PeerId,
            DisplayName = payload.Name,
            Address = System.Net.IPAddress.Parse(
                (await System.Net.Dns.GetHostAddressesAsync(payload.Host))[0].ToString()),
            Port = payload.Port
        };

        _knownPeers[peer.Id] = peer;
        await EnsureConnectionAsync(peer);
        PeerUpdated?.Invoke(peer);
        return peer;
    }

    public void Dispose()
    {
        foreach (var c in _connections.Values) c.Dispose();
        _discovery.Dispose();
        _server.Dispose();
    }
}
