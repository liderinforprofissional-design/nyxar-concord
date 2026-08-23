using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace NyxarConcord.Services;

/// <summary>Informação de uma atualização disponível.</summary>
public sealed class UpdateInfo
{
    public string Version { get; init; } = "";
    public string Url { get; init; } = "";
    public string Notes { get; init; } = "";
    /// <summary>Link direto do instalador (.exe) do release, quando existe.</summary>
    public string AssetUrl { get; init; } = "";
}

/// <summary>
/// Verifica se há uma versão mais nova publicada como "release" no GitHub.
/// Compara a versão do app (do assembly) com a tag do último release.
/// Não baixa nada: apenas informa e leva o usuário à página de download.
/// </summary>
public sealed class UpdateService
{
    // >>> AJUSTE para o seu usuário/repositório do GitHub (formato "usuario/repo").
    public const string Repo = "liderinforprofissional-design/nyxar-concord";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Versão atual do app (ex.: "0.1.0"), lida do assembly.</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Retorna a atualização se houver uma mais nova; senão null.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd("NyxarConcord-Updater");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null; // sem releases ainda / repo privado

            string json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = Str(root, "tag_name");
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var latest = ParseVersion(tag);
            var current = ParseVersion(CurrentVersion);
            if (latest is null) return null;
            if (current is not null && latest <= current) return null; // já está atualizado

            return new UpdateInfo
            {
                Version = tag.TrimStart('v', 'V'),
                Url = Str(root, "html_url"),
                Notes = Str(root, "body"),
                AssetUrl = FindInstallerAsset(root)
            };
        }
        catch { return null; } // sem internet / erro: silencioso
    }

    // Procura o instalador (.exe) entre os anexos do release.
    private static string FindInstallerAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return "";
        foreach (var a in assets.EnumerateArray())
        {
            string name = Str(a, "name");
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return Str(a, "browser_download_url");
        }
        return "";
    }

    /// <summary>Baixa o instalador para a pasta temporária e devolve o caminho (ou null).</summary>
    public async Task<string?> DownloadInstallerAsync(string assetUrl, IProgress<double>? progress = null)
    {
        if (string.IsNullOrEmpty(assetUrl)) return null;
        try
        {
            string path = Path.Combine(Path.GetTempPath(), $"NyxarConcordSetup-{Guid.NewGuid():N}.exe");
            using var req = new HttpRequestMessage(HttpMethod.Get, assetUrl);
            req.Headers.UserAgent.ParseAdd("NyxarConcord-Updater");
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return null;

            long? total = resp.Content.Headers.ContentLength;
            await using var input = await resp.Content.ReadAsStreamAsync();
            await using var output = File.Create(path);
            var buffer = new byte[81920];
            long read = 0; int n;
            while ((n = await input.ReadAsync(buffer)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, n));
                read += n;
                if (total is > 0) progress?.Report((double)read / total.Value);
            }
            return path;
        }
        catch { return null; }
    }

    private static string Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var p) ? p.GetString() ?? "" : "";

    /// <summary>Converte "v1.2.3" ou "1.2.3-beta" em Version comparável.</summary>
    private static Version? ParseVersion(string s)
    {
        s = s.Trim().TrimStart('v', 'V');
        int dash = s.IndexOf('-');
        if (dash > 0) s = s[..dash]; // ignora sufixo de pré-lançamento
        return Version.TryParse(s, out var v) ? v : null;
    }
}
