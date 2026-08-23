using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NyxarConcord.Models;
using NyxarConcord.Services;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IdentityService _identityService = new();

    public MainWindow()
    {
        InitializeComponent();

        // Logo do app na barra de título e na taskbar.
        try { Icon = new BitmapImage(new System.Uri("pack://application:,,,/Assets/nyxar.ico")); } catch { }

        // Tamanho "restaurado" confortável; abre maximizado por padrão.
        var wa = SystemParameters.WorkArea;
        Width = System.Math.Min(1500, wa.Width * 0.9);
        Height = System.Math.Min(900, wa.Height * 0.9);
        WindowState = WindowState.Maximized;

        var identity = _identityService.Load();

        if (!identity.HasAccount)
        {
            // Primeiro acesso: criar conta.
            var register = new RegisterWindow();
            if (register.ShowDialog() != true) { Application.Current.Shutdown(); return; }
            var (hash, salt) = AccountService.HashPassword(register.Password);
            identity.Email = register.Email;
            identity.Username = register.Username;
            identity.DisplayName = string.IsNullOrWhiteSpace(register.DisplayName) ? register.Username : register.DisplayName;
            identity.AvatarPath = register.AvatarPath;
            identity.PasswordHash = hash;
            identity.PasswordSalt = salt;
            identity.Handle = string.IsNullOrWhiteSpace(register.Handle)
                ? AccountService.GenerateHandle(register.Username)
                : register.Handle;
            identity.LoggedIn = true;
            _identityService.Save(identity);
        }
        else if (!identity.LoggedIn)
        {
            // Sessão encerrada: pedir login.
            if (new LoginWindow(identity).ShowDialog() != true) { Application.Current.Shutdown(); return; }
            identity.LoggedIn = true;
            _identityService.Save(identity);
        }

        // Garante handle mesmo em contas antigas.
        if (string.IsNullOrWhiteSpace(identity.Handle))
        {
            identity.Handle = AccountService.GenerateHandle(identity.DisplayName);
            _identityService.Save(identity);
        }

        _vm = new MainViewModel(identity, _identityService, new AudioDeviceService(), new ScreenSourceService());
        DataContext = _vm;
        Title = _vm.AppTitle; // "Nyxar Concord vX.Y.Z" na barra de título/taskbar

        _vm.Messages.CollectionChanged += OnMessagesChanged;
        Closed += (_, _) => _vm.Dispose();

        // Verifica atualizações no GitHub após a janela abrir (silencioso se falhar).
        Loaded += async (_, _) => await CheckForUpdatesAsync();

        // Barra de título personalizada: maximizar respeitando a barra de tarefas.
        SourceInitialized += (_, _) =>
        {
            var src = System.Windows.Interop.HwndSource.FromHwnd(
                new System.Windows.Interop.WindowInteropHelper(this).Handle);
            src?.AddHook(WndProc);
        };
        StateChanged += (_, _) => UpdateMaxGlyph();
        Loaded += (_, _) => UpdateMaxGlyph();
    }

    private void UpdateMaxGlyph()
    {
        if (MaxBtn is not null)
            MaxBtn.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    // --- Barra de título (min/max/fechar) ---
    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void WindowClose_Click(object sender, RoutedEventArgs e) => Close();

    // Faz o "maximizar" ocupar só a área útil (sem cobrir a barra de tarefas / sem cortar).
    private System.IntPtr WndProc(System.IntPtr hwnd, int msg, System.IntPtr wParam, System.IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = System.Runtime.InteropServices.Marshal.PtrToStructure<MINMAXINFO>(lParam);
            System.IntPtr mon = MonitorFromWindow(hwnd, 2 /*NEAREST*/);
            if (mon != System.IntPtr.Zero)
            {
                var mi = new MONITORINFO();
                mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(MONITORINFO));
                if (GetMonitorInfo(mon, ref mi))
                {
                    RECT work = mi.rcWork, area = mi.rcMonitor;
                    mmi.ptMaxPosition.X = work.left - area.left;
                    mmi.ptMaxPosition.Y = work.top - area.top;
                    mmi.ptMaxSize.X = work.right - work.left;
                    mmi.ptMaxSize.Y = work.bottom - work.top;
                    mmi.ptMinTrackSize.X = (int)MinWidth;
                    mmi.ptMinTrackSize.Y = (int)MinHeight;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(mmi, lParam, true);
                }
            }
            handled = true;
        }
        return System.IntPtr.Zero;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern System.IntPtr MonitorFromWindow(System.IntPtr hwnd, int flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(System.IntPtr hMonitor, ref MONITORINFO mi);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }

    private async System.Threading.Tasks.Task CheckForUpdatesAsync()
    {
        try
        {
            var info = await new UpdateService().CheckAsync();
            if (info is not null) _vm.SetUpdateAvailable(info); // mostra a caixa flutuante
        }
        catch { }
    }

    private void Update_Click(object sender, RoutedEventArgs e) => _ = _vm.StartUpdateAsync();
    private void UpdateLater_Click(object sender, RoutedEventArgs e) => _vm.DismissUpdate();

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) => MessageScroll.ScrollToEnd();

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            if (_vm.SendCommand.CanExecute(null)) _vm.SendCommand.Execute(null);
        }
    }

    // Só o admin do servidor vê o menu (alterar foto / excluir).
    private void ServerMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Server s } && !s.CanManageByMe) e.Handled = true;
    }

    // Só o admin do servidor vê o menu da sala (trancar / excluir).
    private void RoomMenu_Opening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Room r } && !r.CanManageByMe) e.Handled = true;
    }

    // --- Rail: servidores ---
    private void Home_Click(object sender, RoutedEventArgs e) => _vm.SelectHome();

    private void SelfNotes_Click(object sender, RoutedEventArgs e) => _vm.SelectSelfNotes();

    private void Server_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Server server }) _vm.SelectServer(server);
    }

    private void Friends_Click(object sender, RoutedEventArgs e)
        => new FriendsWindow(_vm) { Owner = this }.ShowDialog();

    private void CreateServer_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CreateServerDialog { Owner = this };
        if (dlg.ShowDialog() == true) _vm.CreateServer(dlg.ServerNameText, dlg.AvatarPath);
    }

    private void DeleteServer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Server server } &&
            MessageBox.Show($"Excluir o servidor \"{server.Name}\"?", "Excluir servidor",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _vm.DeleteServer(server);
    }

    // --- Sidebar: canais ---
    private void CreateChannel_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasServer) { MessageBox.Show("Selecione um servidor primeiro."); return; }
        if (!_vm.CanModerate) { MessageBox.Show("Só o administrador do servidor pode criar salas."); return; }
        var dlg = new CreateRoomDialog { Owner = this };
        if (dlg.ShowDialog() == true) _vm.CreateChannel(dlg.RoomNameText, dlg.SelectedKind, dlg.Emoji);
    }

    private void Channel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Room room }) _vm.JoinRoom(room);
    }

    private void ToggleLock_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Room room }) _vm.ToggleLock(room);
    }

    private void DeleteChannel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Room room } &&
            MessageBox.Show($"Excluir a sala \"{room.Name}\"?", "Excluir sala",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _vm.DeleteChannel(room);
    }

    // --- Convite / call ---
    private void InternetInvite_Click(object sender, RoutedEventArgs e)
        => new InternetInviteDialog(_vm) { Owner = this }.ShowDialog();

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var vm = new SettingsViewModel(_vm.Identity, _vm.IdentityService, _vm.AudioDeviceService);
        var win = new SettingsWindow(vm) { Owner = this };
        if (win.ShowDialog() != true) return;

        if (win.LogoutRequested)
        {
            _vm.Logout();
            try { System.Diagnostics.Process.Start(System.Environment.ProcessPath ?? ""); } catch { }
            Application.Current.Shutdown();
            return;
        }
        _vm.ApplyAudioSettings();
    }

    private void Invite_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.HasServer) return;
        var dlg = new InvitePeerDialog(_vm.Peers) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Selected is not null) _ = _vm.InvitePeerAsync(dlg.Selected);
    }

    private void ShareScreen_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanShareScreen) { MessageBox.Show("Você precisa estar num canal de voz para compartilhar a tela."); return; }
        var picker = new ScreenSharePicker(_vm.ScreenSourceService) { Owner = this };
        if (picker.ShowDialog() == true && picker.Selected is not null)
            _ = _vm.StartScreenShareAsync(picker.Selected, picker.SelectedHeight);
    }

    private void StopShare_Click(object sender, RoutedEventArgs e) => _vm.StopScreenShare();
    private void ShareAudio_Click(object sender, RoutedEventArgs e) => _vm.ToggleShareAudio();

    // Espectador muta a transmissão de outra pessoa (localmente).
    private void GalleryMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NyxarConcord.ViewModels.GalleryTile t }) _vm.ToggleMuteForPeer(t.PeerId);
    }
    private void StreamMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NyxarConcord.ViewModels.StreamTile t }) _vm.ToggleMuteForPeer(t.SharerId);
    }

    // --- Galeria de participantes ---
    private void ToggleGallery_Click(object sender, RoutedEventArgs e) => _vm.ToggleGalleryView();

    private void GalleryTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NyxarConcord.ViewModels.GalleryTile tile }) _vm.ToggleGalleryMaximize(tile);
    }

    private void GalleryRestore_Click(object sender, MouseButtonEventArgs e) => _vm.RestoreGallery();

    private void Hangup_Click(object sender, RoutedEventArgs e) => _vm.LeaveCall();
    private void MuteMic_Click(object sender, RoutedEventArgs e) => _vm.ToggleMic();

    // --- Arquivos ---
    private void Attach_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Escolher arquivo (até 100 MB)" };
        if (dlg.ShowDialog() == true) _ = _vm.SendFileAsync(dlg.FileName);
    }

    private void SaveFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NyxarConcord.Models.ChatMessage { FileData: { } data } msg })
        {
            var dlg = new SaveFileDialog { FileName = msg.FileName, Title = "Salvar arquivo" };
            if (dlg.ShowDialog() == true)
            {
                try { System.IO.File.WriteAllBytes(dlg.FileName, data); }
                catch { MessageBox.Show("Não foi possível salvar o arquivo."); }
            }
        }
    }

    // --- Transmissão ---
    private void Watch_Click(object sender, RoutedEventArgs e) => _vm.WatchStream();
    private void BackToChat_Click(object sender, RoutedEventArgs e) => _vm.StopWatching();

    private void Tile_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: StreamTile tile }) _vm.ToggleMaximize(tile);
    }

    private void Restore_Click(object sender, MouseButtonEventArgs e) => _vm.Restore();

    // --- Moderação ---
    private void Kick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: RoomMember member }) _vm.KickMember(member);
    }

    private void Ban_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: RoomMember member } &&
            MessageBox.Show($"Banir {member.DisplayName} deste canal?", "Banir",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            _vm.BanMember(member);
    }

    // Silenciar/reativar uma pessoa só para mim (local).
    private void ToggleMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: RoomMember member }) _vm.TogglePeerMute(member);
    }

    // --- Foto do servidor ---
    private void ChangeServerPhoto_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: Server server })
        {
            var dlg = new OpenFileDialog { Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
            if (dlg.ShowDialog() == true) _vm.ChangeServerPhoto(server, dlg.FileName);
        }
    }

    // --- Perfis ---
    private void SelfProfile_Click(object sender, RoutedEventArgs e)
        => new ProfileWindow(_vm.SelfName, _vm.SelfHandle, _vm.SelfStatus, _vm.SelfAvatarPath, _vm.SelfInitials)
        { Owner = this }.ShowDialog();

    private void Member_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoomMember member })
        {
            if (member.IsSelf) { SelfProfile_Click(sender, e); return; }
            var peer = _vm.Peers.FirstOrDefault(p => p.Peer.Id == member.PeerId);
            if (peer is not null) OpenPeerProfile(peer);
        }
    }

    private void MessageAvatar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessage msg } && !msg.IsSystem)
        {
            var peer = _vm.Peers.FirstOrDefault(p => p.Peer.Id == msg.SenderId);
            if (peer is not null) OpenPeerProfile(peer);
        }
    }

    private void OpenPeerProfile(PeerViewModel peer)
        => new ProfileWindow(_vm, peer) { Owner = this }.ShowDialog();
}
