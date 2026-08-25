using System.Windows;
using Microsoft.Win32;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove();
        };
    }

    private void ChoosePhoto_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dlg.ShowDialog() == true)
            _vm.AvatarPath = dlg.FileName;
    }

    private void RemovePhoto_Click(object sender, RoutedEventArgs e) => _vm.AvatarPath = "";

    private void TestMic_Click(object sender, RoutedEventArgs e) => _vm.ToggleMicTest();

    protected override void OnClosed(EventArgs e)
    {
        _vm.StopMicTest(); // garante que o microfone de teste é liberado
        base.OnClosed(e);
    }

    public bool LogoutRequested { get; private set; }
    public bool DeactivateRequested { get; private set; }
    public bool DeleteRequested { get; private set; }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Sair da conta? Você precisará entrar com a senha na próxima vez.",
                "Sair", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        LogoutRequested = true;
        DialogResult = true;
        Close();
    }

    private void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Desativar sua conta? Você sairá agora e ela ficará desativada até " +
                "você entrar de novo com a senha.",
                "Desativar conta", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        DeactivateRequested = true;
        DialogResult = true;
        Close();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Excluir sua conta PERMANENTEMENTE?\n\nIsto apaga a conta e todos os dados " +
                "(servidores, amigos, configurações) desta máquina. NÃO dá para desfazer.",
                "Excluir conta", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        // Segunda confirmação, por segurança.
        if (MessageBox.Show("Tem certeza absoluta? Esta ação é irreversível.",
                "Excluir conta", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        DeleteRequested = true;
        DialogResult = true;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
