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
}
