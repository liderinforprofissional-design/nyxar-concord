using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NyxarConcord.Models;

/// <summary>
/// Pré-visualização de um link (estilo Discord): título, descrição, site e imagem,
/// obtidos das meta tags Open Graph da página. Os campos chegam de forma assíncrona,
/// então notifica a UI conforme carrega. O card só aparece quando <see cref="IsLoaded"/>.
/// </summary>
public sealed class LinkPreview : INotifyPropertyChanged
{
    public string Url { get; init; } = "";

    private string _title = "";
    public string Title { get => _title; set => Set(ref _title, value); }

    private string _description = "";
    public string Description
    {
        get => _description;
        set { if (Set(ref _description, value)) Raise(nameof(HasDescription)); }
    }

    private string _siteName = "";
    public string SiteName { get => _siteName; set => Set(ref _siteName, value); }

    private System.Windows.Media.ImageSource? _image;
    public System.Windows.Media.ImageSource? Image
    {
        get => _image;
        set { if (Set(ref _image, value)) Raise(nameof(HasImage)); }
    }

    private bool _isLoaded;
    public bool IsLoaded { get => _isLoaded; set => Set(ref _isLoaded, value); }

    public bool HasImage => _image is not null;
    public bool HasDescription => !string.IsNullOrWhiteSpace(_description);

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(n); return true;
    }
    private void Raise(string? n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
