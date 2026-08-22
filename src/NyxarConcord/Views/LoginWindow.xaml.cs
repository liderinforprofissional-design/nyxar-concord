using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using NyxarConcord.Models;
using NyxarConcord.Services;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class LoginWindow : Window
{
    private readonly UserIdentity _identity;

    public LoginWindow(UserIdentity identity)
    {
        InitializeComponent();
        _identity = identity;
        WelcomeText.Text = $"Olá, {identity.DisplayName}";
        HandleText.Text = identity.Handle;
        AvatarInitials.Text = MainViewModel.Initials(identity.DisplayName);
        if (!string.IsNullOrWhiteSpace(identity.AvatarPath) && System.IO.File.Exists(identity.AvatarPath))
        {
            try { AvatarBrush.ImageSource = new BitmapImage(new System.Uri(identity.AvatarPath)); } catch { }
        }
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        if (AccountService.Verify(PasswordInput.Password, _identity.PasswordHash, _identity.PasswordSalt))
        {
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Senha incorreta.");
            PasswordInput.Clear();
        }
    }

    private void Forgot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ForgotPasswordDialog(_identity.Email) { Owner = this };
        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.NewPassword))
        {
            // Atualiza o hash local para a nova senha (login offline continua funcionando).
            var (hash, salt) = AccountService.HashPassword(dlg.NewPassword);
            _identity.PasswordHash = hash;
            _identity.PasswordSalt = salt;
            MessageBox.Show("Senha redefinida com sucesso! Você já está entrando.");
            DialogResult = true;
            Close();
        }
    }
}
