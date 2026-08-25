using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;
using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Busca metadados Open Graph de uma URL (título, descrição, site e imagem) para o
/// card de pré-visualização de link no chat. Tudo em cache por URL. Falhas são
/// silenciosas (o card simplesmente não aparece).
/// </summary>
public sealed class LinkPreviewService
{
    private static readonly HttpClient Http = CreateClient();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();

    private static HttpClient CreateClient()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // Alguns sites exigem um User-Agent "de navegador" para servir as meta tags.
        h.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) NyxarConcord/1.0");
        return h;
    }

    /// <summary>Preenche o preview de forma assíncrona (marca IsLoaded ao ter ao menos um título).</summary>
    public async Task FillAsync(LinkPreview lp)
    {
        if (string.IsNullOrWhiteSpace(lp.Url) || !_inFlight.TryAdd(lp.Url, 0)) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, lp.Url);
            req.Headers.Accept.ParseAdd("text/html");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return;

            var ctype = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!ctype.Contains("html")) return; // só páginas HTML têm og tags

            string html = await resp.Content.ReadAsStringAsync();
            if (html.Length > 600_000) html = html[..600_000]; // o <head> vem no começo

            string? title = Meta(html, "og:title") ?? TitleTag(html);
            string? desc = Meta(html, "og:description") ?? Meta(html, "description");
            string? site = Meta(html, "og:site_name");
            string? img = Meta(html, "og:image") ?? Meta(html, "og:image:url");

            if (string.IsNullOrWhiteSpace(title)) return; // sem nada útil: não mostra card

            title = Decode(title);
            desc = Decode(desc);
            site = Decode(site) ?? HostOf(lp.Url);

            BitmapImage? image = await TryLoadImageAsync(AbsoluteUrl(lp.Url, img));

            var app = Application.Current;
            void Apply()
            {
                lp.Title = title!;
                lp.Description = desc ?? "";
                lp.SiteName = site ?? "";
                if (image is not null) lp.Image = image;
                lp.IsLoaded = true;
            }
            if (app is not null) app.Dispatcher.Invoke(Apply); else Apply();
        }
        catch { /* falha silenciosa */ }
        finally { _inFlight.TryRemove(lp.Url, out _); }
    }

    private static async Task<BitmapImage?> TryLoadImageAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            byte[] data = await Http.GetByteArrayAsync(url);
            if (data.Length == 0 || data.Length > 8_000_000) return null;
            var app = Application.Current;
            BitmapImage? Build()
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = new System.IO.MemoryStream(data);
                    bmp.DecodePixelWidth = 480;
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                }
                catch { return null; }
            }
            return app is not null ? app.Dispatcher.Invoke(Build) : Build();
        }
        catch { return null; }
    }

    // <meta property="og:x" content="..."> ou <meta name="x" content="..."> (ordem flexível).
    private static string? Meta(string html, string key)
    {
        string k = Regex.Escape(key);
        var m = Regex.Match(html,
            $"<meta[^>]+(?:property|name)=[\"']{k}[\"'][^>]*?content=[\"']([^\"']*)[\"']",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;
        // content antes de property
        m = Regex.Match(html,
            $"<meta[^>]+content=[\"']([^\"']*)[\"'][^>]*?(?:property|name)=[\"']{k}[\"']",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? TitleTag(string html)
    {
        var m = Regex.Match(html, "<title[^>]*>([^<]*)</title>", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string? Decode(string? s)
        => string.IsNullOrEmpty(s) ? s : System.Net.WebUtility.HtmlDecode(s).Trim();

    private static string HostOf(string url)
    {
        try { return new Uri(url).Host.Replace("www.", ""); } catch { return ""; }
    }

    private static string? AbsoluteUrl(string pageUrl, string? maybeRelative)
    {
        if (string.IsNullOrWhiteSpace(maybeRelative)) return null;
        try
        {
            if (Uri.TryCreate(maybeRelative, UriKind.Absolute, out var abs)) return abs.ToString();
            return new Uri(new Uri(pageUrl), maybeRelative).ToString();
        }
        catch { return maybeRelative; }
    }
}
