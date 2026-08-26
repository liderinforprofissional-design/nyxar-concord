using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace NyxarConcord.Models;

/// <summary>
/// Um "servidor" (guild) — o container de topo, com foto de perfil, dono, admins,
/// membros e vários canais (salas).
/// </summary>
public sealed class Server : INotifyPropertyChanged
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    private string _name = "Novo servidor";
    public string Name { get => _name; set => Set(ref _name, value); }

    /// <summary>Foto do servidor.</summary>
    private string _avatarPath = "";
    public string AvatarPath { get => _avatarPath; set => Set(ref _avatarPath, value); }

    /// <summary>True quando há alguém em uma sala de voz deste servidor (badge no ícone).</summary>
    private bool _hasVoiceActivity;
    [JsonIgnore]
    public bool HasVoiceActivity { get => _hasVoiceActivity; set => Set(ref _hasVoiceActivity, value); }

    /// <summary>PeerId do dono (criador).</summary>
    public string OwnerId { get; set; } = "";

    /// <summary>PeerIds de administradores (além do dono).</summary>
    public List<string> AdminIds { get; set; } = new();

    /// <summary>Canais (salas) do servidor.</summary>
    public ObservableCollection<Room> Channels { get; set; } = new();

    /// <summary>Membros do servidor (runtime, não persistido).</summary>
    [JsonIgnore]
    public ObservableCollection<RoomMember> Members { get; } = new();

    /// <summary>Iniciais para o ícone quando não há foto.</summary>
    [JsonIgnore]
    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name)) return "?";
            var parts = Name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1
                ? parts[0][..1].ToUpperInvariant()
                : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
        }
    }

    public bool CanModerate(string peerId)
        => !string.IsNullOrEmpty(peerId)
           && ((!string.IsNullOrEmpty(OwnerId) && peerId == OwnerId) || AdminIds.Contains(peerId));

    /// <summary>True se o usuário atual é dono/admin deste servidor (para a UI).</summary>
    [JsonIgnore]
    public bool CanManageByMe => CanModerate(Session.SelfId);

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        return true;
    }
}
