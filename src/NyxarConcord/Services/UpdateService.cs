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
                Notes = Str(root, "body")
            };
        }
        catch { return null; } // sem internet / erro: silencioso
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
