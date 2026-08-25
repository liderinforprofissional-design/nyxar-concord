using System.Text.Json.Serialization;

namespace NyxarConcord.Models;

/// <summary>
/// Identidade e conta persistente do usuário. O <see cref="PeerId"/> é o id técnico
/// (estável), enquanto o <see cref="Handle"/> é o identificador curto e legível
/// (ex.: @carlos-1234) usado para busca. A conta guarda email/usuário/senha
/// localmente — o login é único e persiste até o usuário sair.
/// </summary>
public sealed class UserIdentity
{
    [JsonPropertyName("peerId")]
    public string PeerId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Identificador curto e legível, ex.: @carlos-1234.</summary>
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("avatarPath")]
    public string AvatarPath { get; set; } = "";

    // --- Conta ---
    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    /// <summary>Hash da senha (SHA-256 com salt). Nunca guarda a senha em texto.</summary>
    [JsonPropertyName("passwordHash")]
    public string PasswordHash { get; set; } = "";

    [JsonPropertyName("passwordSalt")]
    public string PasswordSalt { get; set; } = "";

    /// <summary>Sessão ativa: se true, abre direto sem pedir login.</summary>
    [JsonPropertyName("loggedIn")]
    public bool LoggedIn { get; set; }

    /// <summary>Conta desativada temporariamente (fica fora até logar de novo).</summary>
    [JsonPropertyName("deactivated")]
    public bool Deactivated { get; set; }

    [JsonPropertyName("audio")]
    public AudioPreferences Audio { get; set; } = new();

    /// <summary>Toca sons discretos nas ações do app (mensagens, entrar/sair de call).</summary>
    [JsonPropertyName("soundsEnabled")]
    public bool SoundsEnabled { get; set; } = true;

    [JsonIgnore]
    public string ShortId => PeerId.Length >= 8 ? PeerId[..8] : PeerId;

    /// <summary>True se já existe uma conta cadastrada.</summary>
    [JsonIgnore]
    public bool HasAccount => !string.IsNullOrWhiteSpace(PasswordHash);
}

public sealed class AudioPreferences
{
    [JsonPropertyName("inputDeviceId")]
    public string InputDeviceId { get; set; } = "";

    [JsonPropertyName("outputDeviceId")]
    public string OutputDeviceId { get; set; } = "";

    [JsonPropertyName("noiseSuppression")]
    public bool NoiseSuppression { get; set; } = true;
}
