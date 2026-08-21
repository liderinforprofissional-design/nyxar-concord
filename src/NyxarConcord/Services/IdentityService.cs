using System.IO;
using System.Text.Json;
using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Carrega e salva a <see cref="UserIdentity"/> em
/// %AppData%\NyxarConcord\identity.json. Assim o peer_id do usuário é o mesmo
/// toda vez que ele abre o app.
/// </summary>
public sealed class IdentityService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string ConfigDirectory { get; }
    public string ConfigPath { get; }

    public IdentityService()
    {
        ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NyxarConcord");
        ConfigPath = Path.Combine(ConfigDirectory, "identity.json");
    }

    public UserIdentity Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                var id = JsonSerializer.Deserialize<UserIdentity>(json);
                if (id is not null && !string.IsNullOrWhiteSpace(id.PeerId))
                    return id;
            }
        }
        catch
        {
            // Arquivo corrompido — recria abaixo.
        }

        var fresh = new UserIdentity();
        Save(fresh);
        return fresh;
    }

    public void Save(UserIdentity identity)
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(identity, JsonOpts));
        }
        catch
        {
            // Sem permissão de escrita — segue em memória.
        }
    }
}
