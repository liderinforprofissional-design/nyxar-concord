using System.IO;
using System.Text.Json;

namespace NyxarConcord.Services;

/// <summary>Uma mensagem guardada no histórico (formato leve, sem os bytes de arquivo).</summary>
public sealed class StoredMessage
{
    public string SenderId { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string Text { get; set; } = "";
    public long Ts { get; set; }           // Timestamp UTC em ticks
    public bool Mine { get; set; }
    public bool File { get; set; }
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
}

/// <summary>
/// Guarda o histórico das conversas (DMs e anotações) em disco, uma conversa por
/// arquivo em %AppData%\NyxarConcord\history\. Mantém as últimas mensagens de cada
/// conversa (não guarda os bytes de arquivos, só o nome/tamanho).
/// </summary>
public sealed class MessageStore
{
    private const int MaxPerConversation = 400;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly string _dir;
    private readonly Dictionary<string, List<StoredMessage>> _cache = new();
    private readonly object _lock = new();

    public MessageStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NyxarConcord", "history");
        try { Directory.CreateDirectory(_dir); } catch { }
    }

    private static string Sanitize(string key)
    {
        var chars = key.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray();
        var s = new string(chars);
        return s.Length > 80 ? s[..80] : s;
    }

    private string PathFor(string key) => Path.Combine(_dir, Sanitize(key) + ".json");

    private List<StoredMessage> Get(string key)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var list)) return list;
            list = new List<StoredMessage>();
            try
            {
                var p = PathFor(key);
                if (File.Exists(p))
                    list = JsonSerializer.Deserialize<List<StoredMessage>>(File.ReadAllText(p)) ?? new();
            }
            catch { list = new List<StoredMessage>(); }
            _cache[key] = list;
            return list;
        }
    }

    /// <summary>Mensagens guardadas dessa conversa (cópia segura para a UI).</summary>
    public List<StoredMessage> Load(string key)
    {
        lock (_lock) return new List<StoredMessage>(Get(key));
    }

    /// <summary>Acrescenta uma mensagem e salva no disco (mantém só as últimas N).</summary>
    public void Append(string key, StoredMessage m)
    {
        lock (_lock)
        {
            var list = Get(key);
            list.Add(m);
            if (list.Count > MaxPerConversation) list.RemoveRange(0, list.Count - MaxPerConversation);
            try { File.WriteAllText(PathFor(key), JsonSerializer.Serialize(list, JsonOpts)); } catch { }
        }
    }

    /// <summary>Apaga o histórico de uma conversa.</summary>
    public void Clear(string key)
    {
        lock (_lock)
        {
            _cache.Remove(key);
            try { File.Delete(PathFor(key)); } catch { }
        }
    }
}
