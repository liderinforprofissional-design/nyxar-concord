using System.IO;
using System.Text.Json;
using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Persiste os servidores (com seus canais) em
/// %AppData%\NyxarConcord\servers.json.
/// </summary>
public sealed class ServerStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public ServerStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NyxarConcord");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "servers.json");
    }

    public List<Server> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var servers = JsonSerializer.Deserialize<List<Server>>(File.ReadAllText(_path));
                if (servers is not null) return servers;
            }
        }
        catch { /* arquivo inválido */ }
        return new List<Server>();
    }

    public void Save(IEnumerable<Server> servers)
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(servers, JsonOpts)); }
        catch { /* sem permissão */ }
    }
}
