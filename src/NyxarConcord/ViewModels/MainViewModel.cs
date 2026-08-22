using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NyxarConcord.Models;
using NyxarConcord.Networking;
using NyxarConcord.Services;

namespace NyxarConcord.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ChatSession _session;
    private readonly WorkerRelay _relay;
    private readonly ServerStore _serverStore = new();
    private readonly ConcurrentDictionary<string, Peer> _relayPeers = new();

    public UserIdentity Identity { get; }
    public IdentityService IdentityService { get; }
    public AudioDeviceService AudioDeviceService { get; }
    public ScreenSourceService ScreenSourceService { get; }

    private readonly VoiceService _voice = new();
    private readonly SoundService _sfx = new();
    private volatile Peer[] _voiceTargets = Array.Empty<Peer>();
    private volatile string? _voiceRoomId;

    // Voz por WebRTC (mídia ponto-a-ponto via TURN). false = volta ao relay.
    private readonly WebRtcVoice _webVoice;
    private bool _useWebRtcVoice = true;

    public ObservableCollection<PeerViewModel> Peers { get; } = new();
    public ObservableCollection<Server> Servers { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public string SelfId => Identity.PeerId;
    public string SelfName => Identity.DisplayName;
    public string PeerId => Identity.PeerId;
    public string SelfAvatarPath => Identity.AvatarPath;
    public string SelfHandle => string.IsNullOrWhiteSpace(Identity.Handle) ? "@usuario" : Identity.Handle;
    public string SelfStatus => string.IsNullOrWhiteSpace(Identity.Status) ? "Online" : Identity.Status;
    public string Status => $"Porta {_session.LocalPort} • ID {Identity.ShortId}";
    public string SelfInitials => Initials(Identity.DisplayName);

    public MainViewModel(
        UserIdentity identity,
        IdentityService identityService,
        AudioDeviceService audioDeviceService,
        ScreenSourceService screenSourceService)
    {
        Identity = identity;
        IdentityService = identityService;
        AudioDeviceService = audioDeviceService;
        ScreenSourceService = screenSourceService;

        // Torna o id do usuário visível aos modelos (permissões de admin na UI).
        Session.SelfId = identity.PeerId;

        _voice.NoiseSuppression = identity.Audio.NoiseSuppression;
        _voice.FrameCaptured += OnVoiceCaptured;

        _sfx.Enabled = identity.SoundsEnabled;

        _session = new ChatSession(identity);
        _session.PeerUpdated += OnPeerUpdated;
        _session.MessageReceived += OnMessageReceived;
        _session.SignalReceived += OnSignalReceived;
        _session.Start();

        // Relay pela nuvem (Cloudflare) — servidores funcionam pela internet.
        _relay = new WorkerRelay(SelfId, identity.DisplayName, identity.Handle);
        _relay.PeerHello += OnRelayHello;
        _relay.PeerLeft += OnRelayLeft;
        _relay.MessageReceived += OnRelayMessage;

        _webVoice = new WebRtcVoice(SelfId, _voice, _relay);
        _webVoice.VideoFrameDecoded += OnWebRtcVideoFrame;

        foreach (var server in _serverStore.Load())
        {
            EnsureSelfServerMember(server);
            foreach (var ch in server.Channels) ch.ServerId = server.Id;
            Servers.Add(server);
        }

        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => CanSend());
    }

    // ============================================================
    //  Navegação: servidores e canais
    // ============================================================

    private Server? _currentServer;
    public Server? CurrentServer
    {
        get => _currentServer;
        private set
        {
            if (SetProperty(ref _currentServer, value))
            {
                OnPropertyChanged(nameof(Channels));
                OnPropertyChanged(nameof(IsHome));
                OnPropertyChanged(nameof(HasServer));
                OnPropertyChanged(nameof(ServerName));
                OnPropertyChanged(nameof(ServerMembers));
                OnPropertyChanged(nameof(CanModerate));
            }
        }
    }

    public ObservableCollection<Room>? Channels => _currentServer?.Channels;
    public ObservableCollection<RoomMember>? ServerMembers => _currentServer?.Members;

    /// <summary>Em call: mostra os conectados na sala. Fora: os membros do servidor.</summary>
    public System.Collections.IEnumerable? ActiveMembers =>
        InCall ? _currentRoom?.Members : _currentServer?.Members;
    public string MembersHeader => InCall ? "CONECTADOS NA SALA" : "MEMBROS";
    public bool ShowVoiceStatus => InCall;
    public bool IsHome => _currentServer is null;
    public bool HasServer => _currentServer is not null;
    public string ServerName => _currentServer?.Name ?? "Mensagens diretas";
    public bool CanModerate => _currentServer?.CanModerate(SelfId) == true;

    public void SelectHome()
    {
        LeaveCurrentChannel();
        _isSelfNotes = false;
        ClearStreams();
        CurrentRoom = null;
        CurrentServer = null;
        _selectedPeer = null;
        OnPropertyChanged(nameof(SelectedPeer));
        Messages.Clear();
        RaiseConversationChanged();
        // Mantém a conexão do relay no último servidor (presença/mensagens continuam).
    }

    public void SelectServer(Server server)
    {
        if (_currentServer?.Id == server.Id) return;
        LeaveCurrentChannel();
        _isSelfNotes = false;
        ClearStreams();
        EnsureSelfServerMember(server);
        _selectedPeer = null;
        OnPropertyChanged(nameof(SelectedPeer));
        CurrentRoom = null;
        CurrentServer = server;
        ApplyRoomPermissions(server);
        Messages.Clear();
        Messages.Add(SystemMessage($"Servidor \"{server.Name}\". Escolha uma sala para começar."));
        RaiseConversationChanged();

        // Conecta ao relay (sala do servidor) para funcionar pela internet.
        _ = _relay.JoinRoomAsync(server.Id);
    }

    public Server CreateServer(string name, string avatarPath = "")
    {
        var server = new Server { Name = name, AvatarPath = avatarPath, OwnerId = SelfId };
        server.Channels.Add(new Room { Name = "geral", Kind = RoomKind.Text, Emoji = "chat", ServerId = server.Id });
        server.Channels.Add(new Room { Name = "Sala de voz", Kind = RoomKind.Audio, Emoji = "voice", ServerId = server.Id });
        EnsureSelfServerMember(server);
        Servers.Add(server);
        SaveServers();
        SelectServer(server);
        _sfx.Success();
        return server;
    }

    public void DeleteServer(Server server)
    {
        if (!server.CanModerate(SelfId)) return; // só o admin exclui o servidor
        if (_currentServer?.Id == server.Id) SelectHome();
        Servers.Remove(server);
        SaveServers();
    }

    public Room? CreateChannel(string name, RoomKind kind, string emoji)
    {
        var server = _currentServer ?? throw new InvalidOperationException("Sem servidor selecionado.");
        if (!server.CanModerate(SelfId)) return null; // só o admin cria salas
        var room = new Room { Name = name, Kind = kind, Emoji = emoji, ServerId = server.Id, CanManageByMe = true };
        server.Channels.Add(room);
        SaveServers();
        JoinRoom(room);
        return room;
    }

    public void DeleteChannel(Room room)
    {
        if (_currentServer?.CanModerate(SelfId) != true) return; // só o admin exclui salas
        if (_currentRoom?.Id == room.Id) { StopWatching(); LeaveCurrentChannel(); _currentRoom = null; OnPropertyChanged(nameof(CurrentRoom)); }
        _currentServer?.Channels.Remove(room);
        SaveServers();
    }

    /// <summary>Marca em cada sala se o usuário atual pode gerenciá-la (admin do servidor).</summary>
    private static void ApplyRoomPermissions(Server server)
    {
        bool can = server.CanManageByMe;
        foreach (var r in server.Channels) r.CanManageByMe = can;
    }

    private void EnsureSelfServerMember(Server server)
    {
        if (server.Members.All(m => m.PeerId != SelfId))
            server.Members.Insert(0, new RoomMember
            {
                PeerId = SelfId,
                DisplayName = Identity.DisplayName,
                IsSelf = true,
                AvatarPath = Identity.AvatarPath
            });
    }

    private void SaveServers() => _serverStore.Save(Servers);

    public void ChangeServerPhoto(Server server, string path)
    {
        if (!server.CanModerate(SelfId)) return; // só o admin muda a foto
        server.AvatarPath = path;
        SaveServers();
    }

    /// <summary>Encerra a sessão: no próximo início pedirá login.</summary>
    public void Logout()
    {
        Identity.LoggedIn = false;
        IdentityService.Save(Identity);
    }

    // ============================================================
    //  Canal atual (sala) + conversa
    // ============================================================

    private Room? _currentRoom;
    public Room? CurrentRoom
    {
        get => _currentRoom;
        private set
        {
            if (SetProperty(ref _currentRoom, value))
            {
                OnPropertyChanged(nameof(HasRoom));
                OnPropertyChanged(nameof(InCall));
                OnPropertyChanged(nameof(CanShareScreen));
                RaiseConversationChanged();
            }
        }
    }

    private PeerViewModel? _selectedPeer;
    public PeerViewModel? SelectedPeer
    {
        get => _selectedPeer;
        set
        {
            if (SetProperty(ref _selectedPeer, value))
            {
                if (value is not null)
                {
                    value.Unread = 0;
                    _isSelfNotes = false;
                    LeaveCurrentChannel();
                    ClearStreams();
                    CurrentRoom = null;
                    CurrentServer = null;
                }
                Messages.Clear();
                RaiseConversationChanged();
            }
        }
    }

    public bool HasRoom => _currentRoom is not null;
    public bool InCall => _currentRoom is { IsAudio: true };
    public bool CanShareScreen => InCall && !IsSharingScreen;
    public bool HasSelectedPeer => _selectedPeer is not null;
    public bool CanChat => _selectedPeer is not null || _currentRoom is not null || _isSelfNotes;

    /// <summary>Nada aberto: mostra a tela de boas-vindas com a logo.</summary>
    public bool ShowWelcome => _currentServer is null && _selectedPeer is null && _currentRoom is null && !_isSelfNotes;

    /// <summary>Marca d'água da logo só nas mensagens diretas/anotações (não nos canais).</summary>
    public bool ShowChatWatermark => ShowChat && _currentRoom is null && !ShowWelcome;

    // --- Anotações pessoais (você mesmo na lista de DMs) ---
    private readonly List<ChatMessage> _selfNotesStore = new();
    private bool _isSelfNotes;
    public bool IsSelfNotes => _isSelfNotes;

    public void SelectSelfNotes()
    {
        LeaveCurrentChannel();
        _isSelfNotes = true;
        _selectedPeer = null; OnPropertyChanged(nameof(SelectedPeer));
        ClearStreams();
        CurrentRoom = null;
        CurrentServer = null;
        Messages.Clear();
        if (_selfNotesStore.Count == 0)
            Messages.Add(SystemMessage("📝 Anotações pessoais — só você vê. Guarde lembretes, links e mensagens aqui."));
        else
            foreach (var m in _selfNotesStore) Messages.Add(m);
        RaiseConversationChanged();
    }

    public string ConversationTitle => _isSelfNotes
        ? "Anotações pessoais"
        : _currentRoom is not null
            ? _currentRoom.Name
            : _selectedPeer?.DisplayName ?? (_currentServer is not null ? _currentServer.Name : "Selecione um canal ou conversa");

    public string ConversationSubtitle => _isSelfNotes
        ? "Só você vê"
        : _currentRoom is not null
            ? (_currentRoom.IsAudio ? "Canal de voz" : "Canal de texto") + (_currentRoom.Locked ? " • 🔒 trancado" : "")
            : _selectedPeer is not null ? "Conversa direta" : "";

    private bool _isSharingScreen;
    public bool IsSharingScreen
    {
        get => _isSharingScreen;
        set { if (SetProperty(ref _isSharingScreen, value)) OnPropertyChanged(nameof(CanShareScreen)); }
    }

    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? parts[0][..1].ToUpperInvariant()
                                 : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }

    private void RaiseConversationChanged()
    {
        OnPropertyChanged(nameof(HasSelectedPeer));
        OnPropertyChanged(nameof(CanChat));
        OnPropertyChanged(nameof(ConversationTitle));
        OnPropertyChanged(nameof(ConversationSubtitle));
        OnPropertyChanged(nameof(InCall));
        OnPropertyChanged(nameof(CanShareScreen));
        OnPropertyChanged(nameof(ShowWelcome));
        OnPropertyChanged(nameof(ShowChatWatermark));
        OnPropertyChanged(nameof(ActiveMembers));
        OnPropertyChanged(nameof(MembersHeader));
        OnPropertyChanged(nameof(ShowVoiceStatus));
    }

    private string _draft = "";
    public string Draft { get => _draft; set => SetProperty(ref _draft, value); }

    public RelayCommand SendCommand { get; }

    // ============================================================
    //  Entrar em canal (com moderação)
    // ============================================================

    public void JoinRoom(Room room)
    {
        // Moderação: banido ou trancado?
        if (room.BannedIds.Contains(SelfId))
        {
            Messages.Clear();
            Messages.Add(SystemMessage($"🚫 Você foi banido do canal \"{room.Name}\"."));
            _sfx.Error();
            return;
        }
        bool isMod = _currentServer?.CanModerate(SelfId) == true;
        if (room.Locked && !isMod && !room.AllowedIds.Contains(SelfId))
        {
            Messages.Clear();
            Messages.Add(SystemMessage($"🔒 O canal \"{room.Name}\" está trancado. Peça acesso a um moderador."));
            _sfx.Error();
            return;
        }

        LeaveCurrentChannel();
        _isSelfNotes = false;
        SelectedPeer = null;
        ClearStreams();

        CurrentRoom = room;
        Messages.Clear();
        Messages.Add(SystemMessage(room.IsAudio
            ? $"🔊 Você entrou no canal de voz \"{room.Name}\"."
            : $"💬 Canal \"{room.Name}\"."));

        OnPropertyChanged(nameof(InCall));
        OnPropertyChanged(nameof(CanShareScreen));
        RaiseStageState();

        if (room.IsAudio)
        {
            AddSelfToChannel(room);
            int dev = int.TryParse(Identity.Audio.InputDeviceId, out var n) ? n : -1;
            _voice.Muted = _isMicMuted;
            _voice.Start(dev);
            UpdateVoiceTargets();
            if (_useWebRtcVoice && _relay.IsConnected)
                _ = _webVoice.StartAsync(room.Id, ServerPeers().Select(p => p.Id).ToList());
            _sfx.JoinCall();
            NotifyServer(new ChatMessage { Signal = SignalType.RoomJoin, RoomId = room.Id, ServerId = room.ServerId });
        }
        else
        {
            _voice.Stop();
            _voiceTargets = Array.Empty<Peer>();
            _voiceRoomId = null;
        }
    }

    private void AddSelfToChannel(Room room)
    {
        if (room.Members.All(m => m.PeerId != SelfId))
            room.Members.Add(new RoomMember
            {
                PeerId = SelfId, DisplayName = Identity.DisplayName, IsSelf = true, AvatarPath = Identity.AvatarPath
            });
    }

    private void LeaveCurrentChannel()
    {
        if (_currentRoom is null) return;
        if (IsSharingScreen) StopScreenShare();
        if (_currentRoom.IsAudio)
        {
            _webVoice.Stop();
            _voice.Stop();
            var self = _currentRoom.Members.FirstOrDefault(m => m.IsSelf);
            if (self is not null) _currentRoom.Members.Remove(self);
            NotifyServer(new ChatMessage { Signal = SignalType.RoomLeave, RoomId = _currentRoom.Id, ServerId = _currentRoom.ServerId });
        }
        _voiceTargets = Array.Empty<Peer>();
        _voiceRoomId = null;
    }

    public void LeaveCall()
    {
        if (_currentRoom is null || !_currentRoom.IsAudio) return;
        LeaveCurrentChannel();
        ClearStreams();
        _sfx.LeaveCall();
        Messages.Add(SystemMessage("📞 Você saiu do canal de voz."));
        CurrentRoom = null;
    }

    // ============================================================
    //  Chat
    // ============================================================

    private bool CanSend() => CanChat && !string.IsNullOrWhiteSpace(Draft);

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(Draft)) return;
        string text = Draft.Trim();
        Draft = "";

        var mine = new ChatMessage
        {
            Kind = MessageKind.Text, SenderId = SelfId, SenderName = Identity.DisplayName, Text = text, IsMine = true
        };
        Messages.Add(mine);
        _sfx.MessageSent();

        if (_isSelfNotes)
        {
            _selfNotesStore.Add(mine); // fica só no seu app
            return;
        }

        try
        {
            if (_selectedPeer is not null)
            {
                if (_selectedPeer.Peer.IsRelay)
                    await _relay.SendToPeerAsync(_selectedPeer.Peer.Id, new ChatMessage { Kind = MessageKind.Text, Text = text });
                else
                    await _session.SendTextAsync(_selectedPeer.Peer, text);
            }
            else if (_currentRoom is not null)
            {
                if (_relay.IsConnected)
                    await _relay.SendToRoomAsync(new ChatMessage { Kind = MessageKind.Text, Text = text });
                else
                    foreach (var p in ServerPeers())
                        await _session.SendTextAsync(p, text);
            }
        }
        catch { Messages.Add(SystemMessage("⚠ Não foi possível entregar a mensagem.")); }
    }

    private IEnumerable<Peer> ServerPeers()
    {
        if (_currentServer is null) yield break;
        foreach (var m in _currentServer.Members)
        {
            if (m.IsSelf) continue;
            var p = Peers.FirstOrDefault(x => x.Peer.Id == m.PeerId);
            if (p is not null) yield return p.Peer;
        }
    }

    private void NotifyServer(ChatMessage signal)
    {
        if (_relay.IsConnected) { _ = _relay.SendToRoomAsync(signal); return; }
        foreach (var p in ServerPeers().ToList())
            _ = _session.SendSignalAsync(p, signal);
    }

    // ============================================================
    //  Transferência de arquivos (até 100 MB)
    // ============================================================

    private const long MaxFileBytes = 100L * 1024 * 1024;
    private const int FileChunkSize = 128 * 1024;
    private readonly ConcurrentDictionary<string, IncomingFile> _incoming = new();

    private bool _isTransferring;
    public bool IsTransferring { get => _isTransferring; private set => SetProperty(ref _isTransferring, value); }
    private string _transferStatus = "";
    public string TransferStatus { get => _transferStatus; private set => SetProperty(ref _transferStatus, value); }
    private double _transferProgress;
    public double TransferProgress { get => _transferProgress; private set => SetProperty(ref _transferProgress, value); }

    private void UpdateTransfer(bool active, string status, double progress)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsTransferring = active;
            TransferStatus = status;
            TransferProgress = progress;
        });
    }

    private sealed class IncomingFile
    {
        public string Name = "";
        public long Size;
        public long Received;
        public int ChunkCount;
        public string SenderName = "";
        public string SenderId = "";
        public readonly MemoryStream Buffer = new();
    }

    private List<Peer> FileTargets()
    {
        if (_selectedPeer is not null) return new List<Peer> { _selectedPeer.Peer };
        if (_currentRoom is not null) return ServerPeers().ToList();
        return new List<Peer>();
    }

    public async Task SendFileAsync(string path)
    {
        // Roteamento: DM relay, sala relay, ou LAN (TCP).
        string? relayPeer = _selectedPeer?.Peer.IsRelay == true ? _selectedPeer.Peer.Id : null;
        bool relayRoom = _selectedPeer is null && _currentRoom is not null && _relay.IsConnected;
        var targets = FileTargets();
        if (relayPeer is null && !relayRoom && targets.Count == 0)
        {
            Messages.Add(SystemMessage("Abra uma conversa ou canal para enviar o arquivo."));
            return;
        }

        async Task SendPart(ChatMessage part)
        {
            if (relayPeer is not null) await _relay.SendToPeerAsync(relayPeer, part);
            else if (relayRoom) await _relay.SendToRoomAsync(part);
            else foreach (var p in targets) await _session.SendSignalAsync(p, part);
        }

        var info = new FileInfo(path);
        if (info.Length > MaxFileBytes)
        {
            Messages.Add(SystemMessage($"Arquivo muito grande ({FormatSize(info.Length)}). Máximo 100 MB."));
            return;
        }

        string id = Guid.NewGuid().ToString("N");
        string name = info.Name;
        long size = info.Length;
        Messages.Add(new ChatMessage
        {
            Kind = MessageKind.Text, SenderId = SelfId, SenderName = Identity.DisplayName, IsMine = true,
            IsFile = true, FileName = name, FileSize = size
        });
        _sfx.FileSent();

        byte[] data;
        try { data = await File.ReadAllBytesAsync(path); }
        catch { Messages.Add(SystemMessage("Não foi possível ler o arquivo.")); return; }

        _ = Task.Run(async () =>
        {
            UpdateTransfer(true, $"Enviando {name}…", 0);
            await SendPart(new ChatMessage { Signal = SignalType.FileOffer, Payload = $"{id}|{name}|{size}" });

            int since = 0;
            for (int off = 0; off < data.Length; off += FileChunkSize)
            {
                int len = Math.Min(FileChunkSize, data.Length - off);
                string b64 = Convert.ToBase64String(data, off, len);
                await SendPart(new ChatMessage { Signal = SignalType.FileChunk, Payload = id, Text = b64 });

                if (++since >= 8 || off + len >= data.Length)
                {
                    since = 0;
                    double prog = data.Length == 0 ? 1 : (double)(off + len) / data.Length;
                    UpdateTransfer(true, $"Enviando {name} — {(int)(prog * 100)}%", prog);
                }
            }

            await SendPart(new ChatMessage { Signal = SignalType.FileEnd, Payload = id });

            UpdateTransfer(false, "", 0);
        });
    }

    private void HandleFileSignal(Peer peer, ChatMessage msg)
    {
        switch (msg.Signal)
        {
            case SignalType.FileOffer:
                var parts = (msg.Payload ?? "").Split('|');
                if (parts.Length >= 3)
                {
                    var f = new IncomingFile
                    {
                        Name = parts[1],
                        Size = long.TryParse(parts[2], out var s) ? s : 0,
                        SenderName = peer.DisplayName,
                        SenderId = peer.Id
                    };
                    _incoming[parts[0]] = f;
                    UpdateTransfer(true, $"Recebendo {f.Name}…", 0);
                }
                break;

            case SignalType.FileChunk:
                if (msg.Payload is { } cid && _incoming.TryGetValue(cid, out var inc) && !string.IsNullOrEmpty(msg.Text))
                {
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(msg.Text);
                        lock (inc) { inc.Buffer.Write(bytes, 0, bytes.Length); inc.Received += bytes.Length; inc.ChunkCount++; }
                        if (inc.ChunkCount % 8 == 0)
                        {
                            double prog = inc.Size <= 0 ? 0 : (double)inc.Received / inc.Size;
                            UpdateTransfer(true, $"Recebendo {inc.Name} — {(int)(prog * 100)}%", prog);
                        }
                    }
                    catch { }
                }
                break;

            case SignalType.FileEnd:
                if (msg.Payload is { } eid && _incoming.TryRemove(eid, out var done))
                {
                    byte[] data = done.Buffer.ToArray();
                    done.Buffer.Dispose();
                    UpdateTransfer(false, "", 0);
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Messages.Add(new ChatMessage
                        {
                            Kind = MessageKind.Text, SenderId = done.SenderId, SenderName = done.SenderName,
                            IsFile = true, FileName = done.Name, FileSize = data.LongLength, FileData = data
                        });
                        _sfx.FileReceived();
                    });
                }
                break;
        }
    }

    private static string FormatSize(long b) =>
        b >= 1024 * 1024 ? $"{b / 1024.0 / 1024:0.#} MB"
        : b >= 1024 ? $"{b / 1024.0:0.#} KB"
        : $"{b} B";

    // ============================================================
    //  Convite para servidor
    // ============================================================

    public async Task InvitePeerAsync(PeerViewModel peer)
    {
        if (_currentServer is null) return;
        try
        {
            string channelsJson = JsonSerializer.Serialize(_currentServer.Channels);
            await _session.SendSignalAsync(peer.Peer, new ChatMessage
            {
                Signal = SignalType.ServerInvite,
                ServerId = _currentServer.Id,
                ServerName = _currentServer.Name,
                Payload = channelsJson
            });
            Messages.Add(SystemMessage($"✉ Convite do servidor enviado para {peer.DisplayName}."));
        }
        catch { Messages.Add(SystemMessage($"⚠ Não foi possível convidar {peer.DisplayName}.")); }
    }

    // ============================================================
    //  Moderação
    // ============================================================

    public void ToggleLock(Room room)
    {
        if (_currentServer?.CanModerate(SelfId) != true) return;
        room.Locked = !room.Locked;
        SaveServers();
        Messages.Add(SystemMessage(room.Locked ? $"🔒 Canal \"{room.Name}\" trancado." : $"🔓 Canal \"{room.Name}\" destrancado."));
        BroadcastChannelUpdate(room);
        OnPropertyChanged(nameof(ConversationSubtitle));
    }

    public void KickMember(RoomMember member)
    {
        if (_currentServer?.CanModerate(SelfId) != true || member.IsSelf) return;
        _currentRoom?.Members.Remove(_currentRoom.Members.FirstOrDefault(m => m.PeerId == member.PeerId) ?? member);
        Messages.Add(SystemMessage($"👢 {member.DisplayName} foi expulso do canal."));
    }

    public void BanMember(RoomMember member)
    {
        if (_currentServer?.CanModerate(SelfId) != true || member.IsSelf || _currentRoom is null) return;
        if (!_currentRoom.BannedIds.Contains(member.PeerId))
            _currentRoom.BannedIds.Add(member.PeerId);
        _currentRoom.Members.Remove(_currentRoom.Members.FirstOrDefault(m => m.PeerId == member.PeerId) ?? member);
        SaveServers();
        Messages.Add(SystemMessage($"🚫 {member.DisplayName} foi banido do canal \"{_currentRoom.Name}\"."));
        BroadcastChannelUpdate(_currentRoom);
        NotifyServer(new ChatMessage { Signal = SignalType.MemberBanned, RoomId = _currentRoom.Id, TargetId = member.PeerId });
    }

    private void BroadcastChannelUpdate(Room room)
    {
        var payload = JsonSerializer.Serialize(new ChannelModeration
        {
            Locked = room.Locked, AllowedIds = room.AllowedIds, BannedIds = room.BannedIds
        });
        NotifyServer(new ChatMessage { Signal = SignalType.ChannelUpdate, RoomId = room.Id, Payload = payload });
    }

    private sealed class ChannelModeration
    {
        public bool Locked { get; set; }
        public List<string> AllowedIds { get; set; } = new();
        public List<string> BannedIds { get; set; } = new();
    }

    // ============================================================
    //  Microfone
    // ============================================================

    private bool _isMicMuted;
    public bool IsMicMuted
    {
        get => _isMicMuted;
        private set { if (SetProperty(ref _isMicMuted, value)) OnPropertyChanged(nameof(MicToolTip)); }
    }
    public string MicToolTip => _isMicMuted ? "Ativar microfone" : "Silenciar microfone";

    public void ToggleMic()
    {
        IsMicMuted = !IsMicMuted;
        _voice.Muted = _isMicMuted;
        if (_isMicMuted) _sfx.MuteOn(); else _sfx.MuteOff();
    }

    public void ApplyAudioSettings()
    {
        _voice.NoiseSuppression = Identity.Audio.NoiseSuppression;
        _sfx.Enabled = Identity.SoundsEnabled;
        OnPropertyChanged(nameof(SelfName));
        OnPropertyChanged(nameof(SelfAvatarPath));
        OnPropertyChanged(nameof(SelfInitials));
        OnPropertyChanged(nameof(SelfStatus));
    }

    private void OnVoiceCaptured(byte[] pcm)
    {
        var roomId = _voiceRoomId;
        if (roomId is null) return;

        // WebRTC ativo: manda a mídia ponto-a-ponto (não passa pelo relay).
        if (_useWebRtcVoice && _webVoice.IsActive) { _webVoice.SendFrame(pcm); return; }

        string b64 = Convert.ToBase64String(pcm);

        if (_relay.IsConnected)
        {
            _ = _relay.SendToRoomAsync(new ChatMessage { Signal = SignalType.VoiceFrame, RoomId = roomId, Text = b64 });
            return;
        }
        foreach (var peer in _voiceTargets)
            _ = _session.SendSignalAsync(peer, new ChatMessage { Signal = SignalType.VoiceFrame, RoomId = roomId, Text = b64 });
    }

    private void UpdateVoiceTargets()
    {
        if (_currentRoom is null || !_currentRoom.IsAudio)
        {
            _voiceTargets = Array.Empty<Peer>(); _voiceRoomId = null; return;
        }
        _voiceRoomId = _currentRoom.Id;
        _voiceTargets = ServerPeers().ToArray();
    }

    // ============================================================
    //  Compartilhamento de tela + palco
    // ============================================================

    private readonly ScreenCaptureService _capture = new();
    private DispatcherTimer? _shareTimer;
    private ScreenSource? _shareSource;
    private int _shareMaxHeight = 720;
    private bool _inStage;

    // Otimização: só envia quando a tela muda (com keyframe periódico).
    private ulong _lastFrameHash;
    private DateTime _lastFrameSentAt;

    private static ulong FnvHash(byte[] data)
    {
        ulong h = 14695981039346656037UL;
        // Amostra o array (passo 7) para hash rápido em quadros grandes.
        for (int i = 0; i < data.Length; i += 7)
        {
            h ^= data[i];
            h *= 1099511628211UL;
        }
        h ^= (ulong)data.Length;
        return h;
    }

    public event Action? StoppedWatching;

    /// <summary>Transmissões ativas no canal (até 4 mini telas).</summary>
    public ObservableCollection<StreamTile> Streams { get; } = new();

    private StreamTile? _maximized;
    public StreamTile? MaximizedStream
    {
        get => _maximized;
        private set { if (SetProperty(ref _maximized, value)) OnPropertyChanged(nameof(IsMaximized)); }
    }

    public bool IsMaximized => _maximized is not null;
    public bool HasStreams => Streams.Count > 0;
    public bool ShowStage => _inStage && HasStreams;
    public bool ShowChat => !ShowStage;
    public bool ShowWatchBanner => InCall && HasStreams && !_inStage;
    public string StreamBarText => Streams.Count == 1 ? Streams[0].SharerName : $"{Streams.Count} transmissões ao vivo";

    public void WatchStream()
    {
        if (!HasStreams) return;
        _inStage = true;
        RaiseStageState();
    }

    public void StopWatching()
    {
        _inStage = false;
        MaximizedStream = null;
        RaiseStageState();
        StoppedWatching?.Invoke();
    }

    public void ToggleMaximize(StreamTile? tile)
        => MaximizedStream = (tile is not null && _maximized != tile) ? tile : null;

    public void Restore() => MaximizedStream = null;

    private void RaiseStageState()
    {
        OnPropertyChanged(nameof(HasStreams));
        OnPropertyChanged(nameof(ShowStage));
        OnPropertyChanged(nameof(ShowChat));
        OnPropertyChanged(nameof(ShowChatWatermark));
        OnPropertyChanged(nameof(ShowWatchBanner));
        OnPropertyChanged(nameof(StreamBarText));
        OnPropertyChanged(nameof(IsMaximized));
    }

    private StreamTile? GetOrCreateTile(string sharerId, string name, bool isSelf)
    {
        var t = Streams.FirstOrDefault(x => x.SharerId == sharerId);
        if (t is null)
        {
            if (Streams.Count >= 4) return null; // no máximo 4 telas
            t = new StreamTile { SharerId = sharerId, SharerName = name, IsSelf = isSelf };
            Streams.Add(t);
            RaiseStageState();
        }
        return t;
    }

    private void RemoveTile(string sharerId)
    {
        var t = Streams.FirstOrDefault(x => x.SharerId == sharerId);
        if (t is not null)
        {
            if (_maximized == t) MaximizedStream = null;
            Streams.Remove(t);
        }
        if (!HasStreams) _inStage = false;
        RaiseStageState();
    }

    private void ClearStreams()
    {
        if (Streams.Count == 0 && !_inStage && _maximized is null) return;
        Streams.Clear();
        MaximizedStream = null;
        _inStage = false;
        RaiseStageState();
    }

    private void SetMemberSharing(string peerId, bool sharing)
    {
        var sm = _currentServer?.Members.FirstOrDefault(x => x.PeerId == peerId);
        if (sm is not null) sm.IsSharingScreen = sharing;
        var cm = _currentRoom?.Members.FirstOrDefault(x => x.PeerId == peerId);
        if (cm is not null) cm.IsSharingScreen = sharing;
    }

    // Quadro de tela recebido por WebRTC (VP8 decodificado em BGR) -> tile.
    private void OnWebRtcVideoFrame(string peerId, byte[] bgr, int w, int h, int stride)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (peerId == SelfId) return;
            var img = BgrToBitmap(bgr, w, h, stride);
            if (img is null) return;
            string name = _currentServer?.Members.FirstOrDefault(m => m.PeerId == peerId)?.DisplayName
                          ?? Peers.FirstOrDefault(p => p.Peer.Id == peerId)?.DisplayName ?? "Usuário";
            SetMemberSharing(peerId, true);
            var tile = GetOrCreateTile(peerId, name, false);
            if (tile is not null) tile.Frame = img;
        });
    }

    private static System.Windows.Media.Imaging.BitmapSource? BgrToBitmap(byte[] bgr, int w, int h, int stride)
    {
        try
        {
            var bmp = System.Windows.Media.Imaging.BitmapSource.Create(
                w, h, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, bgr, stride);
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static BitmapImage? DecodeJpeg(byte[] data)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    public Task StartScreenShareAsync(ScreenSource source, int maxHeight = 720)
    {
        if (!InCall) return Task.CompletedTask;
        _shareSource = source;
        _shareMaxHeight = maxHeight;
        IsSharingScreen = true;
        SetMemberSharing(SelfId, true);
        GetOrCreateTile(SelfId, "Você", true);
        _inStage = true; // já mostra o palco para quem transmite
        Messages.Add(SystemMessage($"🖥 Você está compartilhando: {source.Title}"));
        NotifyServer(new ChatMessage { Signal = SignalType.ScreenShareStart, RoomId = _currentRoom!.Id });
        _sfx.ScreenShare();
        RaiseStageState();

        _lastFrameHash = 0;
        _lastFrameSentAt = DateTime.MinValue;
        // ~10 fps — H264 comprime bem, então dá pra ser mais fluido que antes.
        _shareTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _shareTimer.Tick += (_, _) => CaptureAndSend();
        _shareTimer.Start();
        return Task.CompletedTask;
    }

    private void CaptureAndSend()
    {
        if (!IsSharingScreen || _shareSource is null || _currentRoom is null) return;

        // Caminho WebRTC: envia o vídeo (VP8) ponto-a-ponto, sem passar pelo relay.
        if (_useWebRtcVoice && _webVoice.IsActive)
        {
            byte[]? bgr = _capture.CaptureBgr(_shareSource, _shareMaxHeight, out int vw, out int vh);
            if (bgr is null) return;
            var selfTile = Streams.FirstOrDefault(x => x.SharerId == SelfId);
            if (selfTile is not null) selfTile.Frame = BgrToBitmap(bgr, vw, vh, vw * 3);
            _ = System.Threading.Tasks.Task.Run(() => _webVoice.SendVideoFrame(bgr, vw, vh));
            return;
        }

        byte[]? jpeg = _capture.CaptureJpeg(_shareSource, _shareMaxHeight);
        if (jpeg is null) return;

        // Só envia se a tela mudou; mas manda um "keyframe" a cada 2s
        // (pra quem entra no meio da transmissão receber o quadro atual).
        ulong hash = FnvHash(jpeg);
        bool changed = hash != _lastFrameHash;
        bool keyframe = (DateTime.UtcNow - _lastFrameSentAt).TotalSeconds >= 2;
        if (!changed && !keyframe) return;
        _lastFrameHash = hash;
        _lastFrameSentAt = DateTime.UtcNow;

        var self = Streams.FirstOrDefault(x => x.SharerId == SelfId);
        if (self is not null) self.Frame = DecodeJpeg(jpeg);
        string b64 = Convert.ToBase64String(jpeg);

        if (_relay.IsConnected)
        {
            _ = _relay.SendToRoomAsync(new ChatMessage { Signal = SignalType.ScreenFrame, RoomId = _currentRoom.Id, Text = b64 });
            return;
        }
        foreach (var p in ServerPeers())
            _ = _session.SendSignalAsync(p, new ChatMessage { Signal = SignalType.ScreenFrame, RoomId = _currentRoom.Id, Text = b64 });
    }

    public void StopScreenShare()
    {
        if (!IsSharingScreen) return;
        _shareTimer?.Stop();
        _shareTimer = null;
        _shareSource = null;
        IsSharingScreen = false;
        SetMemberSharing(SelfId, false);
        RemoveTile(SelfId);
        Messages.Add(SystemMessage("🖥 Compartilhamento encerrado."));
        _sfx.ScreenShareStop();
        NotifyServer(new ChatMessage { Signal = SignalType.ScreenShareStop, RoomId = _currentRoom?.Id });
    }

    // ============================================================
    //  Convite por CÓDIGO DE SERVIDOR (funciona pela internet via relay)
    // ============================================================

    /// <summary>Gera um código do servidor atual para compartilhar com amigos.</summary>
    public string CreateServerCode()
    {
        if (_currentServer is null) return "";
        var dto = new ServerCode
        {
            Id = _currentServer.Id,
            Name = _currentServer.Name,
            Channels = _currentServer.Channels.Select(c => new ChannelCode
            {
                Id = c.Id, Name = c.Name, Emoji = c.Emoji, Kind = c.Kind
            }).ToList()
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(dto);
        return "NYX-SRV-" + Convert.ToBase64String(json);
    }

    /// <summary>Entra num servidor a partir de um código (cria localmente + conecta ao relay).</summary>
    public bool JoinServerByCode(string code)
    {
        try
        {
            code = code.Trim();
            if (code.StartsWith("NYX-SRV-", StringComparison.OrdinalIgnoreCase))
                code = code["NYX-SRV-".Length..];
            var dto = JsonSerializer.Deserialize<ServerCode>(Convert.FromBase64String(code));
            if (dto is null || string.IsNullOrWhiteSpace(dto.Id)) return false;

            var server = Servers.FirstOrDefault(s => s.Id == dto.Id);
            if (server is null)
            {
                server = new Server { Id = dto.Id, Name = dto.Name, OwnerId = "" };
                foreach (var ch in dto.Channels)
                    server.Channels.Add(new Room { Id = ch.Id, Name = ch.Name, Emoji = ch.Emoji, Kind = ch.Kind, ServerId = dto.Id });
                EnsureSelfServerMember(server);
                Servers.Add(server);
                SaveServers();
            }
            SelectServer(server);
            _sfx.Success();
            return true;
        }
        catch { return false; }
    }

    private sealed class ServerCode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<ChannelCode> Channels { get; set; } = new();
    }
    private sealed class ChannelCode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Emoji { get; set; } = "";
        public RoomKind Kind { get; set; }
    }

    // ============================================================
    //  Eventos da rede
    // ============================================================

    private void OnPeerUpdated(Peer peer)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var existing = Peers.FirstOrDefault(p => p.Peer.Id == peer.Id);
            if (existing is null) Peers.Add(new PeerViewModel(peer));
            else
            {
                existing.Peer.Address = peer.Address;
                existing.Peer.Port = peer.Port;
                existing.Peer.LastSeen = peer.LastSeen;
                existing.IsOnline = true;
                existing.Refresh();
            }
        });
    }

    // ---------------- Relay (Cloudflare) ----------------

    private Peer GetRelayPeer(string id, string name = "", string handle = "")
    {
        var p = _relayPeers.GetOrAdd(id, _ => new Peer
        {
            Id = id, DisplayName = string.IsNullOrEmpty(name) ? "Usuário" : name, Handle = handle, IsRelay = true
        });
        if (!string.IsNullOrEmpty(name)) p.DisplayName = name;
        if (!string.IsNullOrEmpty(handle)) p.Handle = handle;
        return p;
    }

    private void OnRelayHello(string id, string name, string handle)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var peer = GetRelayPeer(id, name, handle);
            var pvm = Peers.FirstOrDefault(p => p.Peer.Id == id);
            if (pvm is null) { Peers.Add(new PeerViewModel(peer)); _sfx.UserJoined(); }
            else { pvm.IsOnline = true; pvm.Refresh(); }

            if (_currentServer is not null && _currentServer.Members.All(m => m.PeerId != id))
            {
                _currentServer.Members.Add(new RoomMember { PeerId = id, DisplayName = peer.DisplayName });
                OnPropertyChanged(nameof(ServerMembers));
                if (_currentRoom is not null) UpdateVoiceTargets();
                if (_currentRoom?.IsAudio == true && _useWebRtcVoice)
                    _ = _webVoice.PeerJoinedAsync(id);
            }
        });
    }

    private void OnRelayLeft(string id)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_currentServer is not null)
            {
                var sm = _currentServer.Members.FirstOrDefault(m => m.PeerId == id);
                if (sm is not null) _currentServer.Members.Remove(sm);
                foreach (var ch in _currentServer.Channels)
                {
                    var cm = ch.Members.FirstOrDefault(x => x.PeerId == id);
                    if (cm is not null) ch.Members.Remove(cm);
                }
            }
            SetMemberSharing(id, false);
            RemoveTile(id);
            _webVoice.PeerLeft(id);
        });
    }

    private void OnRelayMessage(string fromId, ChatMessage msg)
    {
        var peer = GetRelayPeer(fromId, msg.SenderName);
        if (msg.Kind == MessageKind.Text) OnMessageReceived(peer, msg);
        else OnSignalReceived(peer, msg);
    }

    private void OnMessageReceived(Peer peer, ChatMessage msg)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _sfx.MessageReceived();
            bool viewingPeer = _selectedPeer?.Peer.Id == peer.Id;
            bool viewingServerWithPeer = _currentServer?.Members.Any(m => m.PeerId == peer.Id) == true && _currentRoom is not null;
            if (viewingPeer || viewingServerWithPeer)
                Messages.Add(msg);
            else
            {
                var pvm = Peers.FirstOrDefault(p => p.Peer.Id == peer.Id);
                if (pvm is not null) pvm.Unread++;
            }
        });
    }

    private void OnSignalReceived(Peer peer, ChatMessage msg)
    {
        // Sinalização WebRTC (SDP/ICE) — trata fora da thread de UI.
        if (msg.Signal is SignalType.RtcOffer or SignalType.RtcAnswer or SignalType.RtcIce)
        {
            _ = _webVoice.HandleSignalAsync(peer.Id, msg);
            return;
        }

        if (msg.Signal == SignalType.VoiceFrame)
        {
            if (!string.IsNullOrEmpty(msg.Text) && msg.RoomId == _currentRoom?.Id)
                try { _voice.PlayFrom(peer.Id, Convert.FromBase64String(msg.Text)); } catch { }
            return;
        }

        if (msg.Signal is SignalType.FileOffer or SignalType.FileChunk or SignalType.FileEnd)
        {
            HandleFileSignal(peer, msg);
            return;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            switch (msg.Signal)
            {
                case SignalType.ServerInvite: HandleServerInvite(peer, msg); break;
                case SignalType.ServerJoin: HandleServerJoin(peer, msg); break;
                case SignalType.RoomJoin: HandleChannelPresence(peer, msg, true); break;
                case SignalType.RoomLeave: HandleChannelPresence(peer, msg, false); break;
                case SignalType.ChannelUpdate: HandleChannelUpdate(msg); break;
                case SignalType.MemberBanned: HandleMemberBanned(msg); break;
                case SignalType.ScreenShareStart:
                    SetMemberSharing(peer.Id, true);
                    GetOrCreateTile(peer.Id, peer.DisplayName, false);
                    RaiseStageState();
                    _sfx.ScreenShare();
                    if (_currentRoom?.Id == msg.RoomId)
                        Messages.Add(SystemMessage($"🖥 {peer.DisplayName} começou a compartilhar a tela."));
                    break;
                case SignalType.ScreenShareStop:
                    SetMemberSharing(peer.Id, false);
                    RemoveTile(peer.Id);
                    _sfx.ScreenShareStop();
                    break;
                case SignalType.ScreenFrame:
                    if (!string.IsNullOrEmpty(msg.Text) && _currentRoom?.Id == msg.RoomId)
                    {
                        try
                        {
                            byte[] jpeg = Convert.FromBase64String(msg.Text);
                            SetMemberSharing(peer.Id, true);
                            var tile = GetOrCreateTile(peer.Id, peer.DisplayName, false);
                            if (tile is not null) tile.Frame = DecodeJpeg(jpeg);
                        }
                        catch { }
                    }
                    break;
            }
        });
    }


    private void HandleServerInvite(Peer peer, ChatMessage msg)
    {
        if (MessageBox.Show($"{peer.DisplayName} convidou você para o servidor \"{msg.ServerName}\".\nEntrar?",
                "Convite de servidor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var server = Servers.FirstOrDefault(s => s.Id == msg.ServerId);
        if (server is null)
        {
            server = new Server { Id = msg.ServerId ?? Guid.NewGuid().ToString("N"), Name = msg.ServerName ?? "Servidor", OwnerId = peer.Id };
            try
            {
                var channels = JsonSerializer.Deserialize<List<Room>>(msg.Payload ?? "[]") ?? new();
                foreach (var ch in channels) { ch.ServerId = server.Id; server.Channels.Add(ch); }
            }
            catch { }
            EnsureSelfServerMember(server);
            server.Members.Add(new RoomMember { PeerId = peer.Id, DisplayName = peer.DisplayName });
            Servers.Add(server);
            SaveServers();
        }
        _ = _session.SendSignalAsync(peer, new ChatMessage { Signal = SignalType.ServerJoin, ServerId = server.Id });
        SelectServer(server);
    }

    private void HandleServerJoin(Peer peer, ChatMessage msg)
    {
        var server = Servers.FirstOrDefault(s => s.Id == msg.ServerId);
        if (server is null) return;
        if (server.Members.All(m => m.PeerId != peer.Id))
            server.Members.Add(new RoomMember { PeerId = peer.Id, DisplayName = peer.DisplayName });
    }

    private void HandleChannelPresence(Peer peer, ChatMessage msg, bool joined)
    {
        var room = FindChannel(msg.RoomId);
        if (room is null) return;
        if (joined)
        {
            if (room.Members.All(m => m.PeerId != peer.Id))
                room.Members.Add(new RoomMember { PeerId = peer.Id, DisplayName = peer.DisplayName });
        }
        else
        {
            var m = room.Members.FirstOrDefault(x => x.PeerId == peer.Id);
            if (m is not null) room.Members.Remove(m);
        }
        if (_currentRoom?.Id == room.Id) UpdateVoiceTargets();
    }

    private void HandleChannelUpdate(ChatMessage msg)
    {
        var room = FindChannel(msg.RoomId);
        if (room is null || msg.Payload is null) return;
        try
        {
            var mod = JsonSerializer.Deserialize<ChannelModeration>(msg.Payload);
            if (mod is null) return;
            room.Locked = mod.Locked;
            room.AllowedIds = mod.AllowedIds;
            room.BannedIds = mod.BannedIds;
            // Se estou nesse canal e fui banido/trancado, saio.
            if (_currentRoom?.Id == room.Id && (room.BannedIds.Contains(SelfId) ||
                (room.Locked && !room.AllowedIds.Contains(SelfId) && _currentServer?.CanModerate(SelfId) != true)))
            {
                LeaveCurrentChannel();
                CurrentRoom = null;
                Messages.Add(SystemMessage("Você não tem mais acesso a este canal."));
            }
            OnPropertyChanged(nameof(ConversationSubtitle));
        }
        catch { }
    }

    private void HandleMemberBanned(ChatMessage msg)
    {
        var room = FindChannel(msg.RoomId);
        if (room is null || msg.TargetId is null) return;
        if (!room.BannedIds.Contains(msg.TargetId)) room.BannedIds.Add(msg.TargetId);
        var m = room.Members.FirstOrDefault(x => x.PeerId == msg.TargetId);
        if (m is not null) room.Members.Remove(m);
        if (msg.TargetId == SelfId && _currentRoom?.Id == room.Id)
        {
            LeaveCurrentChannel();
            CurrentRoom = null;
            Messages.Add(SystemMessage($"🚫 Você foi banido do canal \"{room.Name}\"."));
        }
    }

    private Room? FindChannel(string? channelId)
    {
        if (channelId is null) return null;
        foreach (var s in Servers)
        {
            var r = s.Channels.FirstOrDefault(c => c.Id == channelId);
            if (r is not null) return r;
        }
        return null;
    }

    private static ChatMessage SystemMessage(string text) => new()
    {
        Kind = MessageKind.Text, SenderName = "sistema", Text = text
    };

    public void Dispose()
    {
        _webVoice.Dispose();
        _voice.Dispose();
        _relay.Dispose();
        _session.Dispose();
    }
}
