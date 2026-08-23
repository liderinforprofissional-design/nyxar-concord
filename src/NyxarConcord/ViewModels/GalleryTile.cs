using System.Windows.Media;

namespace NyxarConcord.ViewModels;

/// <summary>
/// Um "card" de participante na visualização em galeria (estilo grade).
/// Mostra o avatar centralizado num fundo colorido; se a pessoa estiver
/// transmitindo, a tela dela preenche o card.
/// </summary>
public sealed class GalleryTile : ObservableObject
{
    public string PeerId { get; init; } = "";
    public bool IsSelf { get; init; }

    private string _name = "";
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    private string _avatarPath = "";
    public string AvatarPath { get => _avatarPath; set => SetProperty(ref _avatarPath, value); }

    /// <summary>Cor de fundo do card (única por participante).</summary>
    public Brush Background { get; init; } = Brushes.DimGray;

    private ImageSource? _frame;
    public ImageSource? Frame
    {
        get => _frame;
        set { if (SetProperty(ref _frame, value)) OnPropertyChanged(nameof(ShowAvatar)); }
    }

    private bool _isSharing;
    public bool IsSharing
    {
        get => _isSharing;
        set { if (SetProperty(ref _isSharing, value)) { OnPropertyChanged(nameof(ShowAvatar)); OnPropertyChanged(nameof(ShowSelfShareControl)); } }
    }

    /// <summary>Mostra o controle de áudio da transmissão só no meu próprio card, quando transmito.</summary>
    public bool ShowSelfShareControl => IsSelf && _isSharing;

    private bool _isMuted;
    public bool IsMuted { get => _isMuted; set => SetProperty(ref _isMuted, value); }

    private bool _isMutedByMe;
    public bool IsMutedByMe { get => _isMutedByMe; set => SetProperty(ref _isMutedByMe, value); }

    private bool _isSpeaking;
    public bool IsSpeaking { get => _isSpeaking; set => SetProperty(ref _isSpeaking, value); }

    /// <summary>Mostra o avatar quando NÃO há transmissão com quadro pronto.</summary>
    public bool ShowAvatar => !(_isSharing && _frame is not null);

    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_name)) return "?";
            var parts = _name.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 1
                ? parts[0][..1].ToUpperInvariant()
                : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
        }
    }
}
