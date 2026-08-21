using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class ProfileWindow : Window
{
    private readonly MainViewModel? _vm;
    private readonly PeerViewModel? _peer;

    /// <summary>Perfil de um contato — com caixa de mensagem e envio de arquivos.</summary>
    public ProfileWindow(MainViewModel vm, PeerViewModel peer)
    {
        InitializeComponent();
        _vm = vm;
        _peer = peer;
        NameText.Text = peer.DisplayName;
        HandleText.Text = peer.Handle;
        StatusText.Text = "Online";
        AvatarInitials.Text = peer.Initials;
        Wire();
        Loaded += (_, _) => MsgInput.Focus();
    }

    /// <summary>Perfil do próprio usuário — sem caixa de mensagem.</summary>
    public ProfileWindow(string name, string handle, string status, string avatarPath, string initials)
    {
        InitializeComponent();
        NameText.Text = name;
        HandleText.Text = handle;
        StatusText.Text = string.IsNullOrWhiteSpace(status) ? "Online" : status;
        AvatarInitials.Text = initials;
        MessageBar.Visibility = Visibility.Collapsed;
        SelfFooter.Visibility = Visibility.Visible;
        LoadAvatar(avatarPath);
        Wire();
    }

    private void Wire()
        => MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

    private void LoadAvatar(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            try { AvatarBrush.ImageSource = new BitmapImage(new System.Uri(path)); } catch { }
    }

    private void MsgInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            Send();
        }
    }

    private void Send_Click(object sender, RoutedEventArgs e) => Send();

    private void Send()
    {
        if (_vm is null || _peer is null) return;
        string text = MsgInput.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Abre a DM com esse usuário e envia.
        _vm.SelectedPeer = _peer;
        _vm.Draft = text;
        if (_vm.SendCommand.CanExecute(null)) _vm.SendCommand.Execute(null);
        Close();
    }

    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || _peer is null) return;
        var dlg = new OpenFileDialog { Title = "Escolher arquivo (até 100 MB)" };
        if (dlg.ShowDialog() == true)
        {
            _vm.SelectedPeer = _peer; // garante que a DM é o destino
            _ = _vm.SendFileAsync(dlg.FileName);
            Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
