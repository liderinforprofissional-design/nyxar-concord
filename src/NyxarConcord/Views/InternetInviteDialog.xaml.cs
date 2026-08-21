using System.Windows;
using System.Windows.Input;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class InternetInviteDialog : Window
{
    private readonly MainViewModel _vm;

    public InternetInviteDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        string code = _vm.CreateServerCode();
        MyCode.Text = string.IsNullOrEmpty(code)
            ? "Selecione um servidor primeiro para gerar o código."
            : code;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(MyCode.Text); } catch { }
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        string code = PeerCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Cole o código de um servidor.");
            return;
        }

        bool ok = _vm.JoinServerByCode(code);
        MessageBox.Show(ok
            ? "Entrou no servidor! Ele aparece na barra da esquerda."
            : "Código inválido.");
        if (ok) Close();
    }
}
