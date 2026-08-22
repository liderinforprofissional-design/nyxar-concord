using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NyxarConcord.Services;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class RegisterWindow : Window
{
    private readonly AccountApi _api = new();

    // Preenchidos quando o cadastro/login termina com sucesso.
    public string Email { get; private set; } = "";
    public string Username { get; private set; } = "";
    public string Password { get; private set; } = "";
    public string Handle { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
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

    // ---------- Alternância de painéis ----------
    private void ShowPanel(StackPanel panel)
    {
        FormPanel.Visibility = panel == FormPanel ? Visibility.Visible : Visibility.Collapsed;
        CodePanel.Visibility = panel == CodePanel ? Visibility.Visible : Visibility.Collapsed;
        LoginPanel.Visibility = panel == LoginPanel ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = "";
    }

    private void ShowLogin_Click(object sender, RoutedEventArgs e) => ShowPanel(LoginPanel);
    private void BackToForm_Click(object sender, RoutedEventArgs e) => ShowPanel(FormPanel);

    private void SetStatus(string msg, bool error = true)
    {
        StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
            error ? System.Windows.Media.Color.FromRgb(0xFF, 0x7A, 0x7A)
                  : System.Windows.Media.Color.FromRgb(0x8B, 0xD4, 0x9C));
        StatusText.Text = msg;
    }

    private void Busy(Button btn, bool busy, string busyText, string normalText)
    {
        btn.IsEnabled = !busy;
        btn.Content = busy ? busyText : normalText;
    }

    // ---------- Passo 1: iniciar cadastro ----------
    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        // Evita disparo pelo Enter (botão padrão) quando outro painel está visível.
        if (FormPanel.Visibility != Visibility.Visible) return;

        string email = EmailInput.Text.Trim();
        string username = UsernameInput.Text.Trim();
        string pass = PasswordInput.Password;

        if (string.IsNullOrWhiteSpace(username) || username.Length < 3) { SetStatus("Escolha um nome de usuário (mín. 3 letras)."); return; }
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) { SetStatus("Informe um e-mail válido."); return; }
        if (pass.Length < 4) { SetStatus("A senha precisa ter ao menos 4 caracteres."); return; }
        if (pass != ConfirmInput.Password) { SetStatus("As senhas não conferem."); return; }

        Busy(CreateButton, true, "Enviando código...", "Criar conta");
        var r = await _api.RegisterStartAsync(email, username, pass);
        Busy(CreateButton, false, "", "Criar conta");

        if (!r.Ok) { SetStatus(r.Error ?? "Não foi possível iniciar o cadastro."); return; }

        Email = email; Username = username; Password = pass;
        CodeHint.Text = $"Enviamos um código de 6 dígitos para {email}. Digite-o abaixo.";
        ShowPanel(CodePanel);
        CodeInput.Focus();
    }

    // ---------- Passo 2: confirmar código ----------
    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        string code = CodeInput.Text.Trim();
        if (code.Length < 6) { SetStatus("Digite os 6 dígitos do código."); return; }

        Busy(VerifyButton, true, "Confirmando...", "Confirmar e ativar conta");
        var r = await _api.RegisterVerifyAsync(Email, code);
        Busy(VerifyButton, false, "", "Confirmar e ativar conta");

        if (!r.Ok) { SetStatus(r.Error ?? "Código inválido."); return; }

        Handle = r.Account?.Handle ?? "";
        DisplayName = string.IsNullOrWhiteSpace(r.Account?.DisplayName) ? Username : r.Account!.DisplayName;
        DialogResult = true;
        Close();
    }

    // ---------- Entrar com conta existente ----------
    private async void ServerLogin_Click(object sender, RoutedEventArgs e)
    {
        string id = LoginIdInput.Text.Trim();
        string pass = LoginPassInput.Password;
        if (string.IsNullOrWhiteSpace(id) || pass.Length < 1) { SetStatus("Informe e-mail/usuário e senha."); return; }

        Busy(LoginButton, true, "Entrando...", "Entrar");
        var r = await _api.LoginAsync(id, pass);
        Busy(LoginButton, false, "", "Entrar");

        if (!r.Ok || r.Account is null) { SetStatus(r.Error ?? "Não foi possível entrar."); return; }

        Email = r.Account.Email;
        Username = r.Account.Username;
        Handle = r.Account.Handle;
        DisplayName = string.IsNullOrWhiteSpace(r.Account.DisplayName) ? r.Account.Username : r.Account.DisplayName;
        Password = pass;
        DialogResult = true;
        Close();
    }
}
