using System.Linq;
using System.Windows;
using System.Windows.Input;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class FriendsWindow : Window
{
    private readonly MainViewModel _vm;

    public FriendsWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    // Arrasta a janela só pelo cabeçalho (não pela lista/controles).
    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed && !ProfileWindow.IsInteractive(e.OriginalSource as DependencyObject))
            DragMove();
    }

    // Clicar num amigo: se estiver online (temos o par), abre o perfil/DM dele.
    private void Friend_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FriendViewModel f })
        {
            var peer = _vm.Peers.FirstOrDefault(p => p.Peer.Id == f.Id);
            if (peer is not null)
            {
                Notice.Visibility = Visibility.Collapsed;
                new ProfileWindow(_vm, peer) { Owner = this }.ShowDialog();
            }
            else
            {
                Notice.Text = $"{f.Name} está offline no momento.";
                Notice.Visibility = Visibility.Visible;
            }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
