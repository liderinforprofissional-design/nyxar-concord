using System.Windows;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class ScreenViewerWindow : Window
{
    public ScreenViewerWindow(MainViewModel vm, string sharerName)
    {
        InitializeComponent();
        DataContext = vm; // Image vincula ao StreamFrame do VM (mesma fonte do palco inline).
        HeaderText.Text = $"Tela de {sharerName}";
        Title = $"Tela de {sharerName}";
    }
}
