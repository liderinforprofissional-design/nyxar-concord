using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NyxarConcord.Services;

/// <summary>Conta retornada pelo servidor.</summary>
public sealed class AccountDto
{
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("handle")] public string Handle { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
}

/// <summary>Um usuário encontrado na busca global.</summary>
public sealed class UserHit
{
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("handle")] public string Handle { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
}

/// <summary>Resultado simples de uma chamada (ok + erro + conta opcional).</summary>
public sealed class ApiResult
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public AccountDto? Account { get; init; }

    public static ApiResult Fail(string msg) => new() { Ok = false, Error = msg };
}

/// <summary>
/// Cliente da API de contas do Worker (Cloudflare).
/// Faz cadastro em 2 etapas (código por e-mail), login, "esqueci a senha"
/// e busca global de usuários.
/// </summary>
public sealed class AccountApi
{
    // Mesma base do Worker de sinalização (ver WorkerRelay.cs).
    public const string BaseUrl = "https://nyxar-signal.nyxarp2p.workers.dev";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    // Resposta bruta do servidor.
    private sealed class Raw
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("account")] public AccountDto? Account { get; set; }
        [JsonPropertyName("results")] public List<UserHit>? Results { get; set; }
    }

    private async Task<ApiResult> PostAsync(string path, object payload)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync(BaseUrl + path, payload, JsonOpts);
            var raw = await resp.Content.ReadFromJsonAsync<Raw>(JsonOpts);
            if (raw is null) return ApiResult.Fail("Resposta inválida do servidor.");
            return new ApiResult { Ok = raw.Ok, Error = raw.Error, Account = raw.Account };
        }
        catch (Exception ex)
        {
            return ApiResult.Fail("Sem conexão com o servidor. (" + ex.Message + ")");
        }
    }

    /// <summary>Passo 1 do cadastro: dispara o e-mail com o código.</summary>
    public Task<ApiResult> RegisterStartAsync(string email, string username, string password)
        => PostAsync("/account/register/start", new { email, username, password });

    /// <summary>Passo 2 do cadastro: confirma o código e ativa a conta.</summary>
    public Task<ApiResult> RegisterVerifyAsync(string email, string code)
        => PostAsync("/account/register/verify", new { email, code });

    /// <summary>Login pelo servidor (usar em outro computador).</summary>
    public Task<ApiResult> LoginAsync(string login, string password)
        => PostAsync("/account/login", new { login, password });

    /// <summary>Esqueci a senha: envia o código de redefinição.</summary>
    public Task<ApiResult> ForgotAsync(string email)
        => PostAsync("/account/forgot", new { email });

    /// <summary>Redefine a senha com o código recebido.</summary>
    public Task<ApiResult> ResetAsync(string email, string code, string password)
        => PostAsync("/account/reset", new { email, code, password });

    /// <summary>Busca global de usuários pelo nome/handle. Cancelável (para busca ao vivo).</summary>
    public async Task<IReadOnlyList<UserHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        query = (query ?? "").Trim();
        if (query.Length < 1) return System.Array.Empty<UserHit>();
        try
        {
            string url = BaseUrl + "/account/search?q=" + System.Uri.EscapeDataString(query);
            var raw = await Http.GetFromJsonAsync<Raw>(url, JsonOpts, ct);
            return raw?.Results ?? (IReadOnlyList<UserHit>)System.Array.Empty<UserHit>();
        }
        catch
        {
            return System.Array.Empty<UserHit>();
        }
    }
}
