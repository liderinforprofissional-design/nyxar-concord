using NyxarConcord.Models;

namespace NyxarConcord.ViewModels;

/// <summary>Um amigo na lista (com estado online/offline ao vivo).</summary>
public sealed class FriendViewModel : ObservableObject
{
    public string Id { get; }

    public FriendViewModel(FriendRecord r)
    {
        Id = r.Id;
        _name = r.Name;
        _handle = r.Handle;
        _avatarPath = r.AvatarPath;
    }

    private string _name;
    public string Name { get => _name; set { if (SetProperty(ref _name, value)) OnPropertyChanged(nameof(Initials)); } }

    private string _handle;
    public string Handle { get => string.IsNullOrWhiteSpace(_handle) ? "@usuario" : _handle; set => SetProperty(ref _handle, value); }

    private string _avatarPath;
    public string AvatarPath { get => _avatarPath; set => SetProperty(ref _avatarPath, value); }

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        set { if (SetProperty(ref _isOnline, value)) OnPropertyChanged(nameof(StatusText)); }
    }

    public string StatusText => _isOnline ? "Online" : "Offline";

    public string Initials => MainViewModel.Initials(_name);

    public FriendRecord ToRecord() => new() { Id = Id, Name = _name, Handle = _handle, AvatarPath = _avatarPath };
}
