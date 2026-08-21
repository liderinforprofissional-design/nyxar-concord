using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class RegisterWindow : Window
{
    public string Email => EmailInput.Text.Trim();
    public string Username => UsernameInput.Text.Trim();
    public string Password => PasswordInput.Password;
    public string AvatarPath { get; private set; } = "";

    public RegisterWindow()
    {
        InitializeComponent();
        UsernameInput.Focus();
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dlg.ShowDialog() == true)
        {
            AvatarPath = dlg.FileName;
            try { AvatarBrush.ImageSource = new BitmapImage(new System.Uri(AvatarPath)); } catch { }
        }
    }

    private void UsernameInput_TextChanged(object sender, TextChangedEventArgs e)
        => AvatarInitials.Text = MainViewModel.Initials(UsernameInput.Text);

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Username)) { MessageBox.Show("Escolha um nome de usuário."); return; }
        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@')) { MessageBox.Show("Informe um email válido."); return; }
        if (Password.Length < 4) { MessageBox.Show("A senha precisa ter ao menos 4 caracteres."); return; }
        if (Password != ConfirmInput.Password) { MessageBox.Show("As senhas não conferem."); return; }
        DialogResult = true;
        Close();
    }
}
