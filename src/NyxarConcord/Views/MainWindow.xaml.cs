using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
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
        _vm.NotificationRequested += OnNotificationRequested;
        Closed += (_, _) => { try { TrayIcon?.Dispose(); } catch { } _vm.Dispose(); };

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

        if (win.LogoutRequested || win.DeactivateRequested || win.DeleteRequested)
        {
            if (win.DeleteRequested) _vm.DeleteAccount();
            else if (win.DeactivateRequested) _vm.DeactivateAccount();
            else _vm.Logout();
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

    // Abre o menu de resolução da transmissão.
    private void Resolution_Click(object sender, RoutedEventArgs e) => ResPopup.IsOpen = !ResPopup.IsOpen;

    // Escolha de uma resolução no menu — altera AO VIVO.
    private void ResolutionPick_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: int height }) _vm.SetShareResolution(height);
        ResPopup.IsOpen = false;
    }

    // "Não me assistir": oculta o próprio preview enquanto transmite.
    private void HideSelfView_Click(object sender, RoutedEventArgs e)
    {
        _vm.ToggleHideSelfView();
        ResPopup.IsOpen = false;
    }

    // Espectador muta a transmissão de outra pessoa (localmente).
    private void GalleryMute_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NyxarConcord.ViewModels.GalleryTile t }) _vm.ToggleMuteForPeer(t.PeerId);
    }

    // Galeria: "Assistir" abre a transmissão (só aqui ela carrega/maximiza).
    private void GalleryWatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: NyxarConcord.ViewModels.GalleryTile t }) _vm.WatchGalleryTile(t);
    }

    // Galeria maximizada: muta/ativa o áudio da transmissão que estou assistindo.
    private void GalleryMaxMute_Click(object sender, RoutedEventArgs e)
    {
        var t = _vm.MaximizedGalleryTile;
        if (t is not null) _vm.ToggleMuteForPeer(t.PeerId);
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

    private void InputVol_Click(object sender, RoutedEventArgs e) => InVolPopup.IsOpen = !InVolPopup.IsOpen;
    private void OutputVol_Click(object sender, RoutedEventArgs e) => OutVolPopup.IsOpen = !OutVolPopup.IsOpen;
    private void Deafen_Click(object sender, RoutedEventArgs e) => _vm.ToggleDeafen();

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
        if (sender is FrameworkElement { DataContext: NyxarConcord.Models.ChatMessage msg } && msg.CanSaveFile)
        {
            // Funciona tanto ao vivo (bytes na memória) quanto no histórico (arquivo salvo).
            var data = msg.LoadFileBytes();
            if (data is null) { MessageBox.Show("O arquivo não está mais disponível."); return; }
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

    // Espectador alterna assistir/parar de assistir UMA transmissão (botão do olho
    // no tile / card). O mesmo botão para e volta a assistir — o tile continua
    // visível como espaço reservado enquanto está pausado.
    private void StopWatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is StreamTile st) _vm.ToggleWatchPeer(st.SharerId);
            else if (fe.Tag is GalleryTile gt) _vm.ToggleWatchPeer(gt.PeerId);
        }
    }

    // Botão "voltar a assistir" (galeria / espaço reservado).
    private void ResumeWatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is StreamTile st) _vm.ResumeWatchingStream(st.SharerId);
            else if (fe.Tag is GalleryTile gt) _vm.ResumeWatchingStream(gt.PeerId);
        }
    }

    // Alterna assistir/parar de assistir a tela (menu de contexto do membro).
    private void ToggleWatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: RoomMember member }) _vm.ToggleWatch(member);
    }

    // Notificação temporária na bandeja quando chega mensagem e a janela não está em foco.
    private void OnNotificationRequested(string title, string body)
    {
        if (IsActive && WindowState != WindowState.Minimized) return; // só quando fora de foco
        if (body.Length > 120) body = body[..120] + "…";
        try { TrayIcon?.ShowBalloonTip(title, body, BalloonIcon.Info); } catch { }
    }

    // Duplo clique no ícone da bandeja: traz a janela de volta.
    private void TrayIcon_DoubleClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Show();
        Activate();
        Topmost = true; Topmost = false;
    }

    // Abre o link do card de pré-visualização no navegador.
    private void LinkPreview_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string url } && !string.IsNullOrEmpty(url))
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
    }

    // Não abre o menu de contexto no PRÓPRIO usuário (silenciar/volume não valem para si).
    private void Member_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RoomMember m } && m.IsSelf) e.Handled = true;
    }

    // --- Preview de vídeo no chat (clique para reproduzir/pausar) ---
    private readonly HashSet<MediaElement> _videoPlaying = new();

    private void Video_Opened(object sender, RoutedEventArgs e)
    {
        // Mostra o primeiro quadro como "capa" (pausado no início).
        if (sender is MediaElement me)
            try { me.Position = TimeSpan.Zero; me.Pause(); } catch { }
    }

    private void Video_Ended(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement me) return;
        _videoPlaying.Remove(me);
        try { me.Position = TimeSpan.Zero; me.Pause(); } catch { }
        if (me.Parent is Grid host)
        {
            var overlay = host.Children.OfType<Grid>().FirstOrDefault();
            var badge = overlay?.Children.OfType<Border>().FirstOrDefault();
            if (badge is not null) badge.Visibility = Visibility.Visible;
        }
    }

    private void Video_Toggle(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Grid overlay || overlay.Parent is not Grid host) return;
        var me = host.Children.OfType<MediaElement>().FirstOrDefault();
        if (me is null) return;
        var badge = overlay.Children.OfType<Border>().FirstOrDefault();
        if (_videoPlaying.Contains(me))
        {
            me.Pause(); _videoPlaying.Remove(me);
            if (badge is not null) badge.Visibility = Visibility.Visible;
        }
        else
        {
            me.Play(); _videoPlaying.Add(me);
            if (badge is not null) badge.Visibility = Visibility.Collapsed;
        }
    }

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

    // Barra de volume por usuário (menu de contexto).
    private void Volume_Changed(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (sender is System.Windows.Controls.Slider { DataContext: RoomMember member })
            _vm.SetPeerVolume(member, e.NewValue);
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
