using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NyxarConcord.Models;

public enum RoomKind
{
    /// <summary>Canal de texto.</summary>
    Text,
    /// <summary>Canal de áudio (call), com compartilhamento de tela.</summary>
    Audio
}

/// <summary>
/// Uma "sala" (canal) dentro de um <see cref="Server"/>. Pode ser de texto ou de
/// áudio, ter um emoji, ser trancada e ter usuários banidos (moderação).
/// </summary>
public sealed class Room : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _name = "novo-canal";
    public string Name { get => _name; set => Set(ref _name, value); }

    public RoomKind Kind { get; set; } = RoomKind.Text;

    /// <summary>Emoji do canal (no lugar de foto — mais minimalista).</summary>
    private string _emoji = "";
    public string Emoji { get => _emoji; set => Set(ref _emoji, value); }

    /// <summary>Servidor ao qual o canal pertence.</summary>
    public string ServerId { get; set; } = "";

    // --- Moderação ---
    private bool _locked;
    /// <summary>Trancado: só entram usuários autorizados (ou moderadores).</summary>
    public bool Locked { get => _locked; set { if (Set(ref _locked, value)) OnPropertyChanged(nameof(LockLabel)); } }

    /// <summary>PeerIds autorizados quando o canal está trancado.</summary>
    public List<string> AllowedIds { get; set; } = new();

    /// <summary>PeerIds banidos deste canal.</summary>
    public List<string> BannedIds { get; set; } = new();

    /// <summary>Membros atualmente no canal (runtime, não persistido).</summary>
    [JsonIgnore]
    public ObservableCollection<RoomMember> Members { get; } = new();

    [JsonIgnore] public bool IsAudio => Kind == RoomKind.Audio;
    [JsonIgnore] public string LockLabel => Locked ? "Destrancar canal" : "Trancar canal";

    /// <summary>True se o usuário atual pode gerenciar esta sala (admin do servidor).
    /// Definido pela ViewModel ao abrir o servidor.</summary>
    private bool _canManageByMe;
    [JsonIgnore]
    public bool CanManageByMe { get => _canManageByMe; set => Set(ref _canManageByMe, value); }

    /// <summary>Cronômetro da call desta sala (ex.: "12:34"), ao lado do nome na barra.</summary>
    private string _callTimer = "";
    [JsonIgnore]
    public string CallTimer { get => _callTimer; set => Set(ref _callTimer, value); }

    /// <summary>Mostra o cronômetro ao lado do nome (quando há call em andamento aqui).</summary>
    private bool _showCallTimer;
    [JsonIgnore]
    public bool ShowCallTimer { get => _showCallTimer; set => Set(ref _showCallTimer, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(n); return true;
    }
}

public sealed class RoomMember : INotifyPropertyChanged
{
    public string PeerId { get; init; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsSelf { get; init; }

    private string _avatarPath = "";
    public string AvatarPath { get => _avatarPath; set => Set(ref _avatarPath, value); }

    /// <summary>O próprio usuário silenciou o microfone (propagado a todos).</summary>
    private bool _isMuted;
    public bool IsMuted { get => _isMuted; set => Set(ref _isMuted, value); }

    /// <summary>É administrador/dono do servidor (mostra o selo ADM ao lado do nome).</summary>
    private bool _isAdmin;
    public bool IsAdmin { get => _isAdmin; set => Set(ref _isAdmin, value); }

    /// <summary>Volume desta pessoa para mim (1 = 100%). Ajustável no menu de contexto.</summary>
    private double _volume = 1.0;
    public double Volume { get => _volume; set => Set(ref _volume, value); }

    /// <summary>Está falando agora (anel verde no avatar).</summary>
    private bool _isSpeaking;
    public bool IsSpeaking { get => _isSpeaking; set => Set(ref _isSpeaking, value); }

    /// <summary>Eu silenciei esta pessoa só para mim (local).</summary>
    private bool _isMutedByMe;
    public bool IsMutedByMe
    {
        get => _isMutedByMe;
        set { if (Set(ref _isMutedByMe, value)) { Raise(nameof(MuteMenuLabel)); } }
    }

    /// <summary>Texto do menu de contexto para silenciar/voltar a ouvir esta pessoa.
    /// Deixa claro que é só para MIM — não afeta o microfone da pessoa.</summary>
    public string MuteMenuLabel => _isMutedByMe ? "Voltar a ouvir (só para mim)" : "Silenciar só para mim";

    /// <summary>Eu parei de assistir a transmissão desta pessoa (só para mim).</summary>
    private bool _isWatchBlockedByMe;
    public bool IsWatchBlockedByMe
    {
        get => _isWatchBlockedByMe;
        set { if (Set(ref _isWatchBlockedByMe, value)) Raise(nameof(WatchMenuLabel)); }
    }

    /// <summary>Texto do menu de contexto para assistir/parar de assistir a tela.</summary>
    public string WatchMenuLabel => _isWatchBlockedByMe ? "Assistir à tela" : "Parar de assistir";

    private bool _isSharingScreen;
    public bool IsSharingScreen
    {
        get => _isSharingScreen;
        set { Set(ref _isSharingScreen, value); Raise(nameof(VoiceStatus)); }
    }

    /// <summary>Status na sala de voz: "Transmitindo" ou "Em voz".</summary>
    public string VoiceStatus => _isSharingScreen ? "Transmitindo" : "Em voz";

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DisplayName)) return "?";
            var parts = DisplayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1
                ? parts[0][..1].ToUpperInvariant()
                : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
        }
    }

    public string Role => IsSelf ? "você" : "";

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
    private void Raise(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
