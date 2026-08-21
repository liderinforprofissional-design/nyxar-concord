using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

public partial class InvitePeerDialog : Window
{
    private readonly List<PeerViewModel> _all;
    public PeerViewModel? Selected { get; private set; }

    public InvitePeerDialog(IEnumerable<PeerViewModel> peers)
    {
        InitializeComponent();
        _all = peers.ToList();
        PeerList.ItemsSource = _all;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        string q = SearchBox.Text.Trim().ToLowerInvariant();
        PeerList.ItemsSource = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(p => p.Handle.ToLowerInvariant().Contains(q) ||
                              p.DisplayName.ToLowerInvariant().Contains(q)).ToList();
    }

    private void Invite_Click(object sender, RoutedEventArgs e)
    {
        Selected = PeerList.SelectedItem as PeerViewModel;
        if (Selected is null) { MessageBox.Show("Selecione um usuário."); return; }
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
