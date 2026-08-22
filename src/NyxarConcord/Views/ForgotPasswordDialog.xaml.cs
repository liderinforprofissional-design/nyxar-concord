using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NyxarConcord.Services;

namespace NyxarConcord.Views;

public partial class ForgotPasswordDialog : Window
{
    private readonly AccountApi _api = new();
    private string _email = "";

    /// <summary>Senha nova definida com sucesso (vazia se cancelou).</summary>
    public string NewPassword { get; private set; } = "";

    public ForgotPasswordDialog(string? prefillEmail = null)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(prefillEmail)) EmailInput.Text = prefillEmail;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => EmailInput.Focus();
    }

    private void SetStatus(string msg) => StatusText.Text = msg;

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        string email = EmailInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@')) { SetStatus("Informe um e-mail válido."); return; }

        SendButton.IsEnabled = false; SendButton.Content = "Enviando...";
        var r = await _api.ForgotAsync(email);
        SendButton.IsEnabled = true; SendButton.Content = "Enviar código";

        if (!r.Ok) { SetStatus(r.Error ?? "Não foi possível enviar o código."); return; }

        _email = email;
        SetStatus("");
        EmailPanel.Visibility = Visibility.Collapsed;
        ResetPanel.Visibility = Visibility.Visible;
        CodeInput.Focus();
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        string code = CodeInput.Text.Trim();
        string pass = NewPassInput.Password;
        if (code.Length < 6) { SetStatus("Digite os 6 dígitos do código."); return; }
        if (pass.Length < 4) { SetStatus("A nova senha precisa ter ao menos 4 caracteres."); return; }

        ResetButton.IsEnabled = false; ResetButton.Content = "Redefinindo...";
        var r = await _api.ResetAsync(_email, code, pass);
        ResetButton.IsEnabled = true; ResetButton.Content = "Redefinir senha";

        if (!r.Ok) { SetStatus(r.Error ?? "Não foi possível redefinir."); return; }

        NewPassword = pass;
        DialogResult = true;
        Close();
    }
}
