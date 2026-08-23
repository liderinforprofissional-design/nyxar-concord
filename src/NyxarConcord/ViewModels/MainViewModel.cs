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

    // Voz por WebRTC (mídia ponto-a-ponto via TURN). false = usa o relay (mais confiável).
    // Deixado em false por enquanto: o relay transmite para todos na sala nos dois sentidos.
    private readonly WebRtcVoice _webVoice;
    private bool _useWebRtcVoice = false;

    public ObservableCollection<PeerViewModel> Peers { get; } = new();
    public ObservableCollection<Server> Servers { get; } = new();
    public ObservableCollection<ChatMessage> Messages { get; } = new();

    // Amigos/contatos (persistidos para mostrar também os offline).
    private readonly FriendStore _friendStore = new();
    private readonly Dictionary<string, FriendViewModel> _friends = new();
    public ObservableCollection<FriendViewModel> OnlineFriends { get; } = new();
    public ObservableCollection<FriendViewModel> OfflineFriends { get; } = new();
    public string FriendsHeader => $"AMIGOS — {OnlineFriends.Count} online / {_friends.Count} no total";

    public string SelfId => Identity.PeerId;
    public string SelfName => Identity.DisplayName;
    public string PeerId => Identity.PeerId;
    public string SelfAvatarPath => Identity.AvatarPath;
    public string SelfHandle => string.IsNullOrWhiteSpace(Identity.Handle) ? "@usuario" : Identity.Handle;
    public string SelfStatus => string.IsNullOrWhiteSpace(Identity.Status) ? "Online" : Identity.Status;
    public string Status => $"Porta {_session.LocalPort} • ID {Identity.ShortId}";
    public string SelfInitials => Initials(Identity.DisplayName);
    /// <summary>Nome + versão do app, para o cabeçalho.</summary>
    public string AppTitle => $"Nyxar Concord  v{UpdateService.CurrentVersion}";
    public string AppVersionLabel => $"v{UpdateService.CurrentVersion}";

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
        _voice.SelfId = identity.PeerId;
        _voice.FrameCaptured += OnVoiceCaptured;
        _voice.SpeakingChanged += OnSpeakingChanged;

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

        // Amigos salvos começam offline até aparecerem online.
        foreach (var r in _friendStore.Load())
        {
            if (r.Id == SelfId || _friends.ContainsKey(r.Id)) continue;
            var fvm = new FriendViewModel(r);
            _friends[r.Id] = fvm;
            OfflineFriends.Add(fvm);
        }

        SendCommand = new RelayCommand(_ => _ = SendAsync(), _ => CanSend());
    }

    // ============================================================
    //  Amigos / contatos
    // ============================================================

    /// <summary>Registra/atualiza um amigo e seu estado online, mantendo as listas.</summary>
    private void UpsertFriend(string id, string name, string handle, string? avatar, bool online)
    {
        if (string.IsNullOrEmpty(id) || id == SelfId) return;
        if (!_friends.TryGetValue(id, out var f))
        {
            f = new FriendViewModel(new FriendRecord { Id = id, Name = name, Handle = handle, AvatarPath = avatar ?? "" });
            _friends[id] = f;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(name)) f.Name = name;
            if (!string.IsNullOrWhiteSpace(handle)) f.Handle = handle;
            if (!string.IsNullOrWhiteSpace(avatar)) f.AvatarPath = avatar;
        }
        f.IsOnline = online;

        // Coloca na lista certa (online no topo, offline embaixo).
        var target = online ? OnlineFriends : OfflineFriends;
        var other = online ? OfflineFriends : OnlineFriends;
        if (other.Contains(f)) other.Remove(f);
        if (!target.Contains(f)) target.Add(f);

        OnPropertyChanged(nameof(FriendsHeader));
        _friendStore.Save(_friends.Values.Select(x => x.ToRecord()));
    }

    /// <summary>Foto de perfil conhecida de um peer (para o card de perfil).</summary>
    public string GetPeerAvatar(string id)
    {
        if (_avatars.TryGetValue(id, out var a) && !string.IsNullOrEmpty(a)) return a;
        return _friends.TryGetValue(id, out var f) ? f.AvatarPath : "";
    }

    /// <summary>Atualiza só a foto de um amigo (quando chega pela rede).</summary>
    private void SetFriendAvatar(string id, string avatar)
    {
        if (_friends.TryGetValue(id, out var f))
        {
            f.AvatarPath = avatar;
            _friendStore.Save(_friends.Values.Select(x => x.ToRecord()));
        }
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
        // Propaga a nova foto para todos os participantes do servidor.
        BroadcastServerPhoto(server, null);
    }

    // ---------- Foto do servidor: enviar/receber pela rede ----------

    // Reduz e converte a imagem para PNG pequeno (base64) para caber no relay.
    private static string? EncodeServerAvatar(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.UriSource = new Uri(path);
            src.DecodePixelWidth = 256; // miniatura suficiente para o ícone
            src.EndInit();
            src.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(src));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
        catch { return null; }
    }

    // Salva o PNG recebido num arquivo local e devolve o caminho.
    private static string? SaveServerAvatar(string serverId, string base64)
    {
        try
        {
            byte[] data = Convert.FromBase64String(base64);
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NyxarConcord", "avatars");
            Directory.CreateDirectory(dir);
            // Nome único por versão, para o WPF não reusar a imagem do cache.
            string file = Path.Combine(dir, $"srv-{serverId}-{data.Length}.png");
            File.WriteAllBytes(file, data);
            return file;
        }
        catch { return null; }
    }

    // Envia a foto do servidor: para um par específico (toPeerId) ou para a sala toda.
    private void BroadcastServerPhoto(Server server, string? toPeerId)
    {
        string? b64 = EncodeServerAvatar(server.AvatarPath);
        if (string.IsNullOrEmpty(b64)) return;
        var msg = new ChatMessage
        {
            Signal = SignalType.ServerUpdate,
            ServerId = server.Id,
            ServerName = server.Name,
            Text = b64
        };
        if (toPeerId is not null)
        {
            if (_relay.IsConnected) _ = _relay.SendToPeerAsync(toPeerId, msg);
        }
        else NotifyServer(msg);
    }

    private void HandleServerUpdate(Peer peer, ChatMessage msg)
    {
        var server = Servers.FirstOrDefault(s => s.Id == msg.ServerId);
        if (server is null) return;
        if (string.IsNullOrEmpty(msg.Text)) return;
        string? file = SaveServerAvatar(server.Id, msg.Text);
        if (file is null) return;
        server.AvatarPath = file;
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

    private bool _isShareAudioMuted;
    public bool IsShareAudioMuted
    {
        get => _isShareAudioMuted;
        private set { if (SetProperty(ref _isShareAudioMuted, value)) OnPropertyChanged(nameof(ShareAudioTip)); }
    }
    public string ShareAudioTip => _isShareAudioMuted ? "Ativar áudio do computador na transmissão"
                                                      : "Silenciar áudio do computador na transmissão";

    /// <summary>Liga/desliga o envio do áudio do computador durante a transmissão.</summary>
    public void ToggleShareAudio()
    {
        IsShareAudioMuted = !IsShareAudioMuted;
        _voice.DesktopAudioMuted = _isShareAudioMuted;
        if (_isShareAudioMuted) _sfx.MuteOn(); else _sfx.MuteOff();
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
        Diag.Log("ROOM", $"Abrindo sala '{room.Name}' ({room.Id}), audio={room.IsAudio}");
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

        OnPropertyChanged(nameof(CanUseGallery));

        if (room.IsAudio)
        {
            AddSelfToChannel(room);
            // Lista na hora quem já sabemos estar nesta sala (sem esperar os "acks").
            foreach (var kv in _peerRoom)
            {
                if (kv.Value != room.Id || kv.Key == SelfId) continue;
                if (room.Members.Any(m => m.PeerId == kv.Key)) continue;
                string nm = _currentServer?.Members.FirstOrDefault(m => m.PeerId == kv.Key)?.DisplayName
                            ?? (_relayPeers.TryGetValue(kv.Key, out var rp) ? rp.DisplayName : "Usuário");
                var rmem = new RoomMember { PeerId = kv.Key, DisplayName = nm };
                ApplyMuteState(rmem);
                room.Members.Add(rmem);
            }
            room.Members.CollectionChanged += OnRoomMembersChanged;
            int dev = int.TryParse(Identity.Audio.InputDeviceId, out var n) ? n : -1;
            _voice.Muted = _isMicMuted;
            _voice.Start(dev);
            UpdateVoiceTargets();
            if (_useWebRtcVoice && _relay.IsConnected)
                _ = _webVoice.StartAsync(room.Id, ServerPeers().Select(p => p.Id).ToList());
            _sfx.JoinCall();
            NotifyServer(new ChatMessage { Signal = SignalType.RoomJoin, RoomId = room.Id, ServerId = room.ServerId });
            AnnounceMicState(); // avisa se entrei já mutado
            StartPresenceHeartbeat(room);
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
                PeerId = SelfId, DisplayName = Identity.DisplayName, IsSelf = true,
                AvatarPath = Identity.AvatarPath, IsMuted = _isMicMuted
            });
    }

    // Reanuncia minha presença na sala a cada poucos segundos. Ao receber isso,
    // quem está na mesma sala me readiciona e responde (ack), então nós dois
    // voltamos à lista mesmo depois de uma reconexão do relay.
    private void StartPresenceHeartbeat(Room room)
    {
        StopPresenceHeartbeat();
        _presenceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _presenceTimer.Tick += (_, _) =>
        {
            if (_currentRoom?.Id != room.Id || !_currentRoom.IsAudio) { StopPresenceHeartbeat(); return; }
            NotifyServer(new ChatMessage { Signal = SignalType.RoomJoin, RoomId = room.Id, ServerId = room.ServerId });
        };
        _presenceTimer.Start();
    }

    private void StopPresenceHeartbeat()
    {
        _presenceTimer?.Stop();
        _presenceTimer = null;
    }

    private void OnRoomMembersChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_galleryView) RebuildGallery();
    }

    private void LeaveCurrentChannel()
    {
        if (_currentRoom is null) return;
        StopPresenceHeartbeat();
        if (IsSharingScreen) StopScreenShare();
        if (_currentRoom.IsAudio)
        {
            _currentRoom.Members.CollectionChanged -= OnRoomMembersChanged;
            _webVoice.Stop();
            _voice.Stop();
            var self = _currentRoom.Members.FirstOrDefault(m => m.IsSelf);
            if (self is not null) _currentRoom.Members.Remove(self);
            NotifyServer(new ChatMessage { Signal = SignalType.RoomLeave, RoomId = _currentRoom.Id, ServerId = _currentRoom.ServerId });
        }
        // Sai da galeria ao deixar a call.
        GalleryView = false;
        MaximizedGalleryTile = null;
        Gallery.Clear();
        OnPropertyChanged(nameof(CanUseGallery));
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
        Diag.Log("MSG-TX", $"enviando ({text.Length} chars) selfNotes={_isSelfNotes} peer={_selectedPeer?.Peer.Id ?? "(nenhum)"} room={_currentRoom?.Id ?? "(nenhuma)"}");

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

    /// <summary>Envia uma mensagem direta a um contato sem mudar a conversa atual.
    /// Usado pelo card de perfil (botão enviar).</summary>
    public async Task SendDirectMessageAsync(PeerViewModel peer, string text)
    {
        text = (text ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return;

        var mine = new ChatMessage
        {
            Kind = MessageKind.Text, SenderId = SelfId, SenderName = Identity.DisplayName, Text = text, IsMine = true
        };
        // Se essa DM já está aberta, mostra a mensagem na hora.
        if (_selectedPeer?.Peer.Id == peer.Peer.Id) Messages.Add(mine);
        _sfx.MessageSent();

        try
        {
            if (peer.Peer.IsRelay)
                await _relay.SendToPeerAsync(peer.Peer.Id, new ChatMessage { Kind = MessageKind.Text, Text = text });
            else
                await _session.SendTextAsync(peer.Peer, text);
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

        byte[] data;
        try { data = await File.ReadAllBytesAsync(path); }
        catch { Messages.Add(SystemMessage("Não foi possível ler o arquivo.")); return; }

        // A mensagem do remetente já leva os bytes, para ele poder reabrir/salvar o anexo.
        Messages.Add(new ChatMessage
        {
            Kind = MessageKind.Text, SenderId = SelfId, SenderName = Identity.DisplayName, IsMine = true,
            IsFile = true, FileName = name, FileSize = size, FileData = data
        });
        _sfx.FileSent();

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
                Payload = channelsJson,
                Text = EncodeServerAvatar(_currentServer.AvatarPath) ?? "" // a foto já vai no convite
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
    //  Atualização in-app (caixa flutuante)
    // ============================================================

    private UpdateInfo? _pendingUpdate;
    private bool _updateDismissed;

    public bool UpdateAvailable => _pendingUpdate is not null && !_updateDismissed;
    public string UpdateVersionText => _pendingUpdate is null ? "" : $"Nova versão v{_pendingUpdate.Version} disponível";

    private bool _isUpdating;
    public bool IsUpdating { get => _isUpdating; private set { if (SetProperty(ref _isUpdating, value)) OnPropertyChanged(nameof(ShowUpdateButtons)); } }
    public bool ShowUpdateButtons => !_isUpdating;

    private double _updateProgress;
    public double UpdateProgress { get => _updateProgress; private set => SetProperty(ref _updateProgress, value); }

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }

    /// <summary>Mostra a caixa de atualização (chamado após checar o GitHub).</summary>
    public void SetUpdateAvailable(UpdateInfo info)
    {
        _pendingUpdate = info;
        _updateDismissed = false;
        OnPropertyChanged(nameof(UpdateAvailable));
        OnPropertyChanged(nameof(UpdateVersionText));
    }

    public void DismissUpdate()
    {
        _updateDismissed = true;
        OnPropertyChanged(nameof(UpdateAvailable));
    }

    /// <summary>Baixa e instala a atualização com barra de progresso; reinicia sozinho.</summary>
    public async Task StartUpdateAsync()
    {
        var info = _pendingUpdate;
        if (info is null || _isUpdating) return;

        // Sem instalador anexado ao release: abre a página como plano B.
        if (string.IsNullOrEmpty(info.AssetUrl))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.Url) { UseShellExecute = true }); }
            catch { }
            return;
        }

        IsUpdating = true;
        UpdateProgress = 0;
        UpdateStatus = "Baixando… 0%";
        var progress = new Progress<double>(p =>
        {
            UpdateProgress = p;
            UpdateStatus = $"Baixando… {(int)(p * 100)}%";
        });

        string? setup = await new UpdateService().DownloadInstallerAsync(info.AssetUrl, progress);
        if (setup is null)
        {
            IsUpdating = false;
            UpdateStatus = "Falha ao baixar. Tente de novo.";
            return;
        }

        UpdateProgress = 1;
        UpdateStatus = "Concluído! Reiniciando…";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup)
            {
                UseShellExecute = true,
                Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /NORESTART"
            });
            Application.Current.Shutdown();
        }
        catch
        {
            IsUpdating = false;
            UpdateStatus = "Não foi possível iniciar o instalador.";
        }
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
        // Mostra o ícone no meu próprio nome e avisa todo mundo.
        _micState[SelfId] = _isMicMuted;
        SetMemberMuted(SelfId, _isMicMuted);
        AnnounceMicState();
    }

    // Avisa a sala/servidor se meu microfone está mutado (para o ícone dos outros).
    private void AnnounceMicState()
    {
        NotifyServer(new ChatMessage { Signal = SignalType.MicState, Text = _isMicMuted ? "1" : "0" });
    }

    // ---------- Estado de mudo (próprio e "silenciar para mim") ----------

    // Último estado de microfone conhecido de cada peer (mudo = true).
    private readonly Dictionary<string, bool> _micState = new();
    // Peers que eu silenciei só para mim.
    private readonly HashSet<string> _locallyMuted = new();
    // Foto de perfil (caminho local salvo) de cada peer.
    private readonly Dictionary<string, string> _avatars = new();
    // Em qual sala cada peer está agora (para listar na hora ao entrar).
    private readonly Dictionary<string, string> _peerRoom = new();

    // Aplica os estados conhecidos (mudo/foto) a um membro recém-criado.
    private void ApplyMuteState(RoomMember m)
    {
        if (_micState.TryGetValue(m.PeerId, out var muted)) m.IsMuted = muted;
        m.IsMutedByMe = _locallyMuted.Contains(m.PeerId);
        if (_avatars.TryGetValue(m.PeerId, out var avatar)) m.AvatarPath = avatar;
    }

    // Envia meu perfil (nome + foto) para a sala ou para um par específico.
    private void AnnounceProfile(string? toPeerId)
    {
        string? b64 = EncodeServerAvatar(Identity.AvatarPath);
        if (string.IsNullOrEmpty(b64)) return; // sem foto: nada a propagar
        var msg = new ChatMessage { Signal = SignalType.UserUpdate, SenderName = Identity.DisplayName, Text = b64 };
        if (toPeerId is not null) { if (_relay.IsConnected) _ = _relay.SendToPeerAsync(toPeerId, msg); }
        else NotifyServer(msg);
    }

    private void HandleUserUpdate(Peer peer, ChatMessage msg)
    {
        if (string.IsNullOrEmpty(msg.Text)) return;
        string? file = SaveServerAvatar("user-" + peer.Id, msg.Text);
        if (file is null) return;
        _avatars[peer.Id] = file;
        foreach (var m in AllMemberInstances(peer.Id)) m.AvatarPath = file;
        UpdateGalleryTile(peer.Id, t => t.AvatarPath = file);
        SetFriendAvatar(peer.Id, file);
    }

    // Marca "mic mutado" (próprio, propagado) em todas as listas onde a pessoa aparece.
    private void SetMemberMuted(string peerId, bool muted)
    {
        foreach (var m in AllMemberInstances(peerId)) m.IsMuted = muted;
        UpdateGalleryTile(peerId, t => t.IsMuted = muted);
    }

    // Indicador "falando" (anel verde) — vem da VoiceService, aplica na UI.
    private void OnSpeakingChanged(string peerId, bool speaking)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            foreach (var m in AllMemberInstances(peerId)) m.IsSpeaking = speaking;
            UpdateGalleryTile(peerId, t => t.IsSpeaking = speaking);
        });
    }

    // Silencia/reativa uma pessoa só para mim: áudio + ícone + som.
    public void TogglePeerMute(RoomMember member)
    {
        if (member.IsSelf) return;
        ToggleMuteForPeer(member.PeerId);
    }

    /// <summary>Silencia/reativa localmente o áudio de um peer (usado também pelos
    /// botões de headset na transmissão, para quem assiste).</summary>
    public void ToggleMuteForPeer(string peerId)
    {
        if (string.IsNullOrEmpty(peerId) || peerId == SelfId) return;
        bool mute = !_locallyMuted.Contains(peerId);
        if (mute) _locallyMuted.Add(peerId); else _locallyMuted.Remove(peerId);
        _voice.SetPeerMuted(peerId, mute);
        foreach (var m in AllMemberInstances(peerId)) m.IsMutedByMe = mute;
        UpdateGalleryTile(peerId, t => t.IsMutedByMe = mute);
        var st = Streams.FirstOrDefault(s => s.SharerId == peerId);
        if (st is not null) st.IsMutedByMe = mute;
        if (mute) _sfx.MuteOn(); else _sfx.MuteOff();
    }

    // Todas as instâncias de RoomMember (servidor + canais) com este PeerId.
    private IEnumerable<RoomMember> AllMemberInstances(string peerId)
    {
        foreach (var s in Servers)
        {
            foreach (var m in s.Members) if (m.PeerId == peerId) yield return m;
            foreach (var ch in s.Channels)
                foreach (var m in ch.Members) if (m.PeerId == peerId) yield return m;
        }
    }

    public void ApplyAudioSettings()
    {
        _voice.NoiseSuppression = Identity.Audio.NoiseSuppression;
        _sfx.Enabled = Identity.SoundsEnabled;
        OnPropertyChanged(nameof(SelfName));
        OnPropertyChanged(nameof(SelfAvatarPath));
        OnPropertyChanged(nameof(SelfInitials));
        OnPropertyChanged(nameof(SelfStatus));

        // Atualiza meu próprio card/lista e avisa os outros da nova foto/nome na hora.
        foreach (var m in AllMemberInstances(SelfId))
        {
            m.DisplayName = Identity.DisplayName;
            m.AvatarPath = Identity.AvatarPath;
        }
        UpdateGalleryTile(SelfId, t => { t.Name = Identity.DisplayName; t.AvatarPath = Identity.AvatarPath; });
        if (_currentServer is not null && _relay.IsConnected) AnnounceProfile(null);
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
    private CancellationTokenSource? _shareCts;
    // Batimento de presença: reanuncia periodicamente que estou na sala, para
    // curar a lista de membros caso o relay tenha soltado/reconectado alguém.
    private DispatcherTimer? _presenceTimer;
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
    public bool ShowStage => _inStage && HasStreams && !_galleryView;
    public bool ShowGallery => _galleryView && InCall;
    public bool ShowChat => !ShowStage && !ShowGallery;
    public bool ShowWatchBanner => InCall && HasStreams && !_inStage && !_galleryView;
    /// <summary>Se o botão de alternar galeria deve aparecer (só em call).</summary>
    public bool CanUseGallery => InCall;
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

    // ============================================================
    //  Visualização em GALERIA (cards dos participantes)
    // ============================================================

    /// <summary>Cards dos participantes (avatar centralizado; tela preenche quando transmite).</summary>
    public ObservableCollection<GalleryTile> Gallery { get; } = new();

    private bool _galleryView;
    public bool GalleryView
    {
        get => _galleryView;
        private set { if (SetProperty(ref _galleryView, value)) RaiseGalleryState(); }
    }

    private GalleryTile? _maxTile;
    public GalleryTile? MaximizedGalleryTile
    {
        get => _maxTile;
        private set { if (SetProperty(ref _maxTile, value)) OnPropertyChanged(nameof(IsGalleryMaximized)); }
    }
    public bool IsGalleryMaximized => _maxTile is not null;

    public string GalleryToggleTip => _galleryView ? "Ver o chat" : "Ver os participantes (galeria)";

    /// <summary>Alterna entre o chat e a galeria de participantes.</summary>
    public void ToggleGalleryView()
    {
        if (!InCall) return;
        if (!_galleryView) RebuildGallery();
        MaximizedGalleryTile = null;
        GalleryView = !_galleryView;
    }

    /// <summary>Clique no card: só o de quem está transmitindo maximiza/volta.</summary>
    public void ToggleGalleryMaximize(GalleryTile? tile)
    {
        if (tile is null || !tile.IsSharing) return;
        MaximizedGalleryTile = _maxTile != tile ? tile : null;
    }

    public void RestoreGallery() => MaximizedGalleryTile = null;

    private void RaiseGalleryState()
    {
        OnPropertyChanged(nameof(ShowGallery));
        OnPropertyChanged(nameof(ShowChat));
        OnPropertyChanged(nameof(ShowStage));
        OnPropertyChanged(nameof(ShowWatchBanner));
        OnPropertyChanged(nameof(ShowChatWatermark));
        OnPropertyChanged(nameof(GalleryToggleTip));
    }

    // Reconstrói os cards a partir dos membros da sala atual (mantém quadros/estados).
    private void RebuildGallery()
    {
        Gallery.Clear();
        if (_currentRoom is null) return;
        foreach (var m in _currentRoom.Members)
        {
            var tile = new GalleryTile
            {
                PeerId = m.PeerId,
                IsSelf = m.IsSelf,
                Name = m.IsSelf ? Identity.DisplayName : m.DisplayName,
                AvatarPath = m.AvatarPath,
                Background = ColorForPeer(m.PeerId),
                IsSharing = m.IsSharingScreen,
                IsMuted = m.IsMuted,
                IsMutedByMe = m.IsMutedByMe,
                IsSpeaking = m.IsSpeaking,
                Frame = Streams.FirstOrDefault(s => s.SharerId == m.PeerId)?.Frame
            };
            Gallery.Add(tile);
        }
    }

    // Aplica uma mudança ao card do participante (se a galeria estiver montada).
    private void UpdateGalleryTile(string peerId, Action<GalleryTile> apply)
    {
        var t = Gallery.FirstOrDefault(g => g.PeerId == peerId);
        if (t is not null) apply(t);
    }

    // Cor de fundo estável e agradável (tom pastel escuro) a partir do id.
    private static readonly Brush[] _galleryPalette = BuildGalleryPalette();
    private static Brush ColorForPeer(string peerId)
    {
        int h = 0; foreach (char c in peerId) h = h * 31 + c;
        return _galleryPalette[(uint)h % (uint)_galleryPalette.Length];
    }
    private static Brush[] BuildGalleryPalette()
    {
        // Tons escuros e discretos (parecidos com o mock: marrom/vinho/ardósia).
        string[] hex = { "#3A2E33", "#4A3A42", "#5A4A44", "#3A3340", "#334044", "#40363A", "#463C4A", "#3E4436" };
        var arr = new Brush[hex.Length];
        for (int i = 0; i < hex.Length; i++)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex[i]);
            var b = new SolidColorBrush(c); b.Freeze(); arr[i] = b;
        }
        return arr;
    }

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
            t = new StreamTile { SharerId = sharerId, SharerName = name, IsSelf = isSelf,
                                 IsMutedByMe = _locallyMuted.Contains(sharerId) };
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
        UpdateGalleryTile(peerId, t => { t.IsSharing = sharing; if (!sharing) t.Frame = null; });
        if (!sharing && _maxTile?.PeerId == peerId) MaximizedGalleryTile = null;
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
            UpdateGalleryTile(peerId, t => t.Frame = img);
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
        IsShareAudioMuted = false;          // começa transmitindo o som do PC
        _voice.DesktopAudioMuted = false;
        SetMemberSharing(SelfId, true);
        GetOrCreateTile(SelfId, "Você", true);
        _inStage = true; // já mostra o palco para quem transmite
        Messages.Add(SystemMessage($"🖥 Você está compartilhando: {source.Title}"));
        NotifyServer(new ChatMessage { Signal = SignalType.ScreenShareStart, RoomId = _currentRoom!.Id });
        _sfx.ScreenShare();
        RaiseStageState();

        _lastFrameHash = 0;
        _lastFrameSentAt = DateTime.MinValue;
        // Captura/codifica/envia numa thread própria (não trava a UI) => mais fluido.
        _shareCts = new CancellationTokenSource();
        string roomId = _currentRoom!.Id;
        var token = _shareCts.Token;
        _ = Task.Run(() => ShareLoopAsync(roomId, token));

        // Também captura o áudio do computador (vídeos/jogo) para a transmissão.
        _voice.StartDesktopAudio();
        return Task.CompletedTask;
    }

    // ~12 quadros/s; a checagem de "mudou" segura telas estáticas e o "drop se ocupado"
    // impede que os quadros de tela engulam a fila do relay e travem a voz.
    private int _screenSending; // 0 = livre, 1 = enviando
    private async Task ShareLoopAsync(string roomId, CancellationToken token)
    {
        const int frameMs = 33; // ~30 fps (o "descarta se ocupado" protege a voz)
        var sw = new System.Diagnostics.Stopwatch();
        while (!token.IsCancellationRequested && IsSharingScreen)
        {
            sw.Restart();
            try { CaptureAndSendOnce(roomId); } catch { }
            int rest = frameMs - (int)sw.ElapsedMilliseconds;
            try { await Task.Delay(rest > 0 ? rest : 1, token); }
            catch (TaskCanceledException) { break; }
        }
    }

    private void CaptureAndSendOnce(string roomId)
    {
        var src = _shareSource;
        if (!IsSharingScreen || src is null) return;

        // Se o quadro anterior ainda está sendo enviado, pula este (a voz tem prioridade).
        if (System.Threading.Interlocked.CompareExchange(ref _screenSending, 1, 0) == 1) return;
        try
        {
            byte[]? jpeg = _capture.CaptureJpeg(src, _shareMaxHeight, quality: 42);
            if (jpeg is null) { _screenSending = 0; return; }

            // Só envia se a tela mudou; keyframe a cada 2s (pra quem entra no meio).
            ulong hash = FnvHash(jpeg);
            bool changed = hash != _lastFrameHash;
            bool keyframe = (DateTime.UtcNow - _lastFrameSentAt).TotalSeconds >= 2;
            if (!changed && !keyframe) { _screenSending = 0; return; }
            _lastFrameHash = hash;
            _lastFrameSentAt = DateTime.UtcNow;

            // Preview local (frozen -> pode cruzar para a UI).
            var selfFrame = DecodeJpeg(jpeg);
            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                var self = Streams.FirstOrDefault(x => x.SharerId == SelfId);
                if (self is not null) self.Frame = selfFrame;
                UpdateGalleryTile(SelfId, t => t.Frame = selfFrame);
            });

            string b64 = Convert.ToBase64String(jpeg);
            if (_relay.IsConnected)
                _relay.SendToRoomAsync(new ChatMessage { Signal = SignalType.ScreenFrame, RoomId = roomId, Text = b64 })
                      .ContinueWith(_ => _screenSending = 0);
            else
            {
                foreach (var p in ServerPeers())
                    _ = _session.SendSignalAsync(p, new ChatMessage { Signal = SignalType.ScreenFrame, RoomId = roomId, Text = b64 });
                _screenSending = 0;
            }
        }
        catch { _screenSending = 0; }
    }

    public void StopScreenShare()
    {
        if (!IsSharingScreen) return;
        _shareCts?.Cancel();
        _shareCts = null;
        _shareSource = null;
        IsSharingScreen = false;
        _voice.StopDesktopAudio();
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
        Diag.Log("HELLO", $"{name}/{id}");
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpsertFriend(id, name, handle, _avatars.GetValueOrDefault(id), online: true);
            var peer = GetRelayPeer(id, name, handle);
            var pvm = Peers.FirstOrDefault(p => p.Peer.Id == id);
            if (pvm is null) { Peers.Add(new PeerViewModel(peer)); _sfx.UserJoined(); }
            else { pvm.IsOnline = true; pvm.Refresh(); }

            if (_currentServer is not null && _currentServer.Members.All(m => m.PeerId != id))
            {
                var nm = new RoomMember { PeerId = id, DisplayName = peer.DisplayName };
                ApplyMuteState(nm);
                _currentServer.Members.Add(nm);
                OnPropertyChanged(nameof(ServerMembers));
                if (_currentRoom is not null) UpdateVoiceTargets();
                if (_currentRoom?.IsAudio == true && _useWebRtcVoice)
                    _ = _webVoice.PeerJoinedAsync(id);
            }

            // Se estou numa sala de voz e alguém (re)apareceu, reanuncio minha
            // presença direto para ele — assim, após uma reconexão do relay, o par
            // volta a saber que estou na sala (e me readiciona/responde).
            if (_currentRoom?.IsAudio == true && id != SelfId && _relay.IsConnected)
            {
                var join = new ChatMessage { Signal = SignalType.RoomJoin, RoomId = _currentRoom.Id, ServerId = _currentRoom.ServerId };
                _ = _relay.SendToPeerAsync(id, join);
                // e informo meu estado de microfone para ele mostrar o ícone certo
                _ = _relay.SendToPeerAsync(id, new ChatMessage { Signal = SignalType.MicState, Text = _isMicMuted ? "1" : "0" });
            }

            // O dono é a fonte da verdade da foto: quando alguém aparece no servidor,
            // manda a foto atual direto para ele (cobre entrada por código e reconexão).
            if (_currentServer is not null && _currentServer.OwnerId == SelfId
                && id != SelfId && !string.IsNullOrEmpty(_currentServer.AvatarPath))
            {
                BroadcastServerPhoto(_currentServer, id);
            }

            // Mando minha foto de perfil para quem apareceu (para o avatar aparecer na lista).
            if (_currentServer is not null && id != SelfId && _relay.IsConnected)
                AnnounceProfile(id);
        });
    }

    private void OnRelayLeft(string id)
    {
        Diag.Log("LEFT", id);
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_friends.TryGetValue(id, out var fr)) UpsertFriend(id, fr.Name, fr.Handle, null, online: false);
            _peerRoom.Remove(id);
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
        // Só é chat de verdade quando NÃO há sinal. Voz, tela, presença de sala e
        // arquivos chegam com Signal setado (o Kind pode vir como Text pelo relay),
        // então roteamos pelo Signal — senão eles caem no chat (o "flood").
        if (msg.Signal == SignalType.None && msg.Kind == MessageKind.Text)
            OnMessageReceived(peer, msg);
        else
            OnSignalReceived(peer, msg);
    }

    private void OnMessageReceived(Peer peer, ChatMessage msg)
    {
        Diag.Log("MSG-RX", $"de {peer.DisplayName}/{peer.Id} ({(msg.Text ?? "").Length} chars)");
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
                case SignalType.ServerUpdate: HandleServerUpdate(peer, msg); break;
                case SignalType.MicState:
                    _micState[peer.Id] = msg.Text == "1";
                    SetMemberMuted(peer.Id, msg.Text == "1");
                    break;
                case SignalType.UserUpdate: HandleUserUpdate(peer, msg); break;
                case SignalType.RoomJoin: HandleChannelPresence(peer, msg, true); break;
                case SignalType.RoomLeave: HandleChannelPresence(peer, msg, false); break;
                case SignalType.ChannelUpdate: HandleChannelUpdate(msg); break;
                case SignalType.MemberBanned: HandleMemberBanned(msg); break;
                case SignalType.ScreenShareStart:
                    EnsurePeerInCurrentRoom(peer, msg.RoomId);
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
                            EnsurePeerInCurrentRoom(peer, msg.RoomId);
                            SetMemberSharing(peer.Id, true);
                            var frame = DecodeJpeg(jpeg);
                            var tile = GetOrCreateTile(peer.Id, peer.DisplayName, false);
                            if (tile is not null) tile.Frame = frame;
                            UpdateGalleryTile(peer.Id, t => t.Frame = frame);
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
            // Foto do servidor veio junto no convite? Salva e aplica.
            if (!string.IsNullOrEmpty(msg.Text))
            {
                string? file = SaveServerAvatar(server.Id, msg.Text);
                if (file is not null) server.AvatarPath = file;
            }
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

    // Se um par está claramente ativo na minha sala (mandando tela/voz) mas sumiu
    // da lista por causa de uma reconexão do relay, readiciona-o. A presença passa
    // a seguir a atividade real, não só os eventos de entrada/saída.
    private void EnsurePeerInCurrentRoom(Peer peer, string? roomId)
    {
        if (peer.Id == SelfId) return;
        if (_currentRoom is null || _currentRoom.Id != roomId) return;
        if (_currentServer is not null && _currentServer.Members.All(m => m.PeerId != peer.Id))
        {
            var nm = new RoomMember { PeerId = peer.Id, DisplayName = peer.DisplayName };
            ApplyMuteState(nm);
            _currentServer.Members.Add(nm);
            OnPropertyChanged(nameof(ServerMembers));
        }
        if (_currentRoom.Members.All(m => m.PeerId != peer.Id))
        {
            var rm = new RoomMember { PeerId = peer.Id, DisplayName = peer.DisplayName };
            ApplyMuteState(rm);
            _currentRoom.Members.Add(rm);
            UpdateVoiceTargets();
        }
    }

    private void HandleChannelPresence(Peer peer, ChatMessage msg, bool joined)
    {
        Diag.Log("PRESENCE", $"{(joined ? "JOIN" : "LEAVE")} de {peer.DisplayName}/{peer.Id} sala={msg.RoomId} to={msg.To ?? "(broadcast)"} minhaSala={_currentRoom?.Id}");
        var room = FindChannel(msg.RoomId);
        if (room is null) { Diag.Log("PRESENCE", $"sala {msg.RoomId} nao encontrada localmente"); return; }
        if (joined)
        {
            _peerRoom[peer.Id] = room.Id;
            if (room.Members.All(m => m.PeerId != peer.Id))
            {
                var nm = new RoomMember { PeerId = peer.Id, DisplayName = peer.DisplayName };
                ApplyMuteState(nm);
                room.Members.Add(nm);
            }

            // Se eu já estou nesta sala e o outro acabou de anunciar (broadcast),
            // respondo direto pra ele saber que eu também estou aqui.
            if (_currentRoom?.Id == room.Id && string.IsNullOrEmpty(msg.To) && peer.Id != SelfId)
            {
                Diag.Log("PRESENCE", $"respondendo (ack) minha presenca para {peer.DisplayName}");
                var ack = new ChatMessage { Signal = SignalType.RoomJoin, RoomId = room.Id, ServerId = room.ServerId };
                if (_relay.IsConnected) _ = _relay.SendToPeerAsync(peer.Id, ack);
                else _ = _session.SendSignalAsync(peer, ack);
            }
        }
        else
        {
            if (_peerRoom.TryGetValue(peer.Id, out var r) && r == room.Id) _peerRoom.Remove(peer.Id);
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
        StopPresenceHeartbeat();
        _webVoice.Dispose();
        _voice.Dispose();
        _relay.Dispose();
        _session.Dispose();
    }
}
