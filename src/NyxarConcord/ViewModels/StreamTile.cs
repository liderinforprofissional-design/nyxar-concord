using System.Windows.Media;

namespace NyxarConcord.ViewModels;

/// <summary>Uma transmissão de tela ativa (uma "mini tela" na grade do palco).</summary>
public sealed class StreamTile : ObservableObject
{
    public string SharerId { get; init; } = "";
    public string SharerName { get; init; } = "";
    public bool IsSelf { get; init; }

    private ImageSource? _frame;
    public ImageSource? Frame
    {
        get => _frame;
        set => SetProperty(ref _frame, value);
    }

    /// <summary>Eu silenciei o áudio desta transmissão só para mim (viewer).</summary>
    private bool _isMutedByMe;
    public bool IsMutedByMe { get => _isMutedByMe; set => SetProperty(ref _isMutedByMe, value); }

    /// <summary>Eu parei de assistir esta transmissão (só para mim). O tile vira um
    /// espaço reservado com o botão "voltar a assistir" — a pessoa continua transmitindo.</summary>
    private bool _isWatchBlocked;
    public bool IsWatchBlocked
    {
        get => _isWatchBlocked;
        set { if (SetProperty(ref _isWatchBlocked, value)) OnPropertyChanged(nameof(WatchTip)); }
    }

    /// <summary>Dica do botão do olho (parar / voltar a assistir).</summary>
    public string WatchTip => _isWatchBlocked ? "Voltar a assistir" : "Parar de assistir (só para você)";
}
