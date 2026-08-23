using System.IO;
using System.Text.Json;
using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Persiste a lista de amigos/contatos conhecidos em
/// %AppData%\NyxarConcord\friends.json (para mostrar também os offline).
/// </summary>
public sealed class FriendStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public FriendStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NyxarConcord");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "friends.json");
    }

    public List<FriendRecord> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var list = JsonSerializer.Deserialize<List<FriendRecord>>(File.ReadAllText(_path));
                if (list is not null) return list;
            }
        }
        catch { /* arquivo inválido */ }
        return new List<FriendRecord>();
    }

    public void Save(IEnumerable<FriendRecord> friends)
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(friends, JsonOpts)); }
        catch { /* sem permissão */ }
    }
}
