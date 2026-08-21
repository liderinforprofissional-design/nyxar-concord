using NyxarConcord.Models;

namespace NyxarConcord.ViewModels;

public sealed class PeerViewModel : ObservableObject
{
    public Peer Peer { get; }

    public PeerViewModel(Peer peer) => Peer = peer;

    public string DisplayName => Peer.DisplayName;
    public string Handle => string.IsNullOrWhiteSpace(Peer.Handle) ? "@usuario" : Peer.Handle;
    public string Address => $"{Peer.Address}:{Peer.Port}";
    public string Initials => MainViewModel.Initials(Peer.DisplayName);

    private bool _isOnline = true;
    public bool IsOnline
    {
        get => _isOnline;
        set => SetProperty(ref _isOnline, value);
    }

    private int _unread;
    public int Unread
    {
        get => _unread;
        set { if (SetProperty(ref _unread, value)) OnPropertyChanged(nameof(HasUnread)); }
    }

    public bool HasUnread => _unread > 0;

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Address));
    }
}
