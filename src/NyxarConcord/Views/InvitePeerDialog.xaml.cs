using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NyxarConcord.Services;
using NyxarConcord.ViewModels;

namespace NyxarConcord.Views;

/// <summary>
/// Item mostrado na busca: pode ser um contato ONLINE (com Peer para convidar)
/// ou um usuário encontrado no diretório global (offline).
/// </summary>
public sealed class SearchResultItem
{
    public string DisplayName { get; init; } = "";
    public string Initials { get; init; } = "?";
    public string RawHandle { get; init; } = "";
    public bool IsOnline { get; init; }
    public PeerViewModel? Peer { get; init; }

    /// <summary>Texto do handle mostrado na lista (marca "online" quando aplicável).</summary>
    public string Handle => IsOnline ? RawHandle + "   ·   online" : RawHandle;

    public static SearchResultItem FromPeer(PeerViewModel p) => new()
    {
        DisplayName = p.DisplayName,
        Initials = p.Initials,
        RawHandle = p.Handle,
        IsOnline = true,
        Peer = p,
    };

    public static SearchResultItem FromHit(UserHit h)
    {
        string name = string.IsNullOrWhiteSpace(h.DisplayName) ? h.Username : h.DisplayName;
        return new SearchResultItem
        {
            DisplayName = name,
            Initials = MainViewModel.Initials(name),
            RawHandle = h.Handle,
            IsOnline = false,
            Peer = null,
        };
    }
}

public partial class InvitePeerDialog : Window
{
    private readonly List<PeerViewModel> _localPeers;
    private readonly AccountApi _api = new();
    private CancellationTokenSource? _cts;

    public PeerViewModel? Selected { get; private set; }

    public InvitePeerDialog(IEnumerable<PeerViewModel> peers)
    {
        InitializeComponent();
        _localPeers = peers.ToList();
        PeerList.ItemsSource = FilterLocal("");
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Loaded += (_, _) => SearchBox.Focus();
    }

    // Filtra os contatos já conectados (instantâneo, sem rede).
    // Tolerante: ignora o "@" e casa por handle, nome ou pedaço deles.
    private List<SearchResultItem> FilterLocal(string q)
    {
        IEnumerable<PeerViewModel> src = _localPeers;
        string lq = q.Trim().TrimStart('@').ToLowerInvariant();
        if (!string.IsNullOrEmpty(lq))
        {
            src = _localPeers.Where(p =>
                (p.Handle ?? "").TrimStart('@').ToLowerInvariant().Contains(lq) ||
                (p.DisplayName ?? "").ToLowerInvariant().Contains(lq));
        }
        return src.Select(SearchResultItem.FromPeer).ToList();
    }

    // Deixa sempre um item selecionado quando há resultados, para o botão
    // "Convidar" funcionar sem exigir um clique extra na lista.
    private void AutoSelectFirst()
    {
        if (PeerList.SelectedItem is null && PeerList.Items.Count > 0)
            PeerList.SelectedIndex = 0;
    }

    private void ShowNotice(string text)
    {
        Notice.Text = text;
        Notice.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    // Busca ao vivo: mostra locais na hora e, com pequeno atraso, junta o diretório global.
    private async void Search_Changed(object sender, TextChangedEventArgs e)
    {
        string q = SearchBox.Text.Trim();
        ShowNotice("");

        // 1) Resultado local imediato.
        var list = FilterLocal(q);
        PeerList.ItemsSource = list;
        AutoSelectFirst();

        // 2) Busca global com "debounce" (evita uma chamada por tecla).
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        if (q.Length < 1) return;

        try { await Task.Delay(250, token); }
        catch (TaskCanceledException) { return; }
        if (token.IsCancellationRequested) return;

        IReadOnlyList<UserHit> hits;
        try { hits = await _api.SearchAsync(q, token); }
        catch { return; } // sem internet / diretório indisponível: fica só nos locais
        if (token.IsCancellationRequested) return;

        var seen = new HashSet<string>(
            list.Select(i => i.RawHandle), System.StringComparer.OrdinalIgnoreCase);
        var merged = new List<SearchResultItem>(list);
        foreach (var h in hits)
        {
            if (!string.IsNullOrEmpty(h.Handle) && seen.Add(h.Handle))
                merged.Add(SearchResultItem.FromHit(h));
        }
        PeerList.ItemsSource = merged;
        AutoSelectFirst();
    }

    private void PeerList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PeerList.SelectedItem is SearchResultItem) Invite_Click(sender, e);
    }

    private void Invite_Click(object sender, RoutedEventArgs e)
    {
        // Se nada foi clicado mas há resultados, usa o primeiro.
        AutoSelectFirst();

        if (PeerList.SelectedItem is not SearchResultItem item)
        {
            ShowNotice(PeerList.Items.Count == 0
                ? "Ninguém encontrado. Verifique o nome/@handle — a pessoa precisa estar online."
                : "Escolha um usuário na lista.");
            return;
        }
        if (item.Peer is null)
        {
            ShowNotice($"{item.DisplayName} existe, mas não está online agora. Convide quando estiver conectado.");
            return;
        }
        Selected = item.Peer;
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
