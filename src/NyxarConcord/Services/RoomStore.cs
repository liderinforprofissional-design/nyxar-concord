using System.IO;
using System.Text.Json;
using NyxarConcord.Models;

namespace NyxarConcord.Services;

/// <summary>
/// Persiste os canais criados pelo usuário em
/// %AppData%\NyxarConcord\rooms.json, para que continuem existindo após reabrir.
/// </summary>
public sealed class RoomStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly string _path;

    public RoomStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NyxarConcord");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "rooms.json");
    }

    public List<Room> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var rooms = JsonSerializer.Deserialize<List<Room>>(File.ReadAllText(_path));
                if (rooms is not null) return rooms;
            }
        }
        catch { /* arquivo inválido */ }
        return new List<Room>();
    }

    public void Save(IEnumerable<Room> rooms)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(rooms, JsonOpts));
        }
        catch { /* sem permissão */ }
    }
}
