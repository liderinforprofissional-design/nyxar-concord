using System.IO;
using System.Text;

namespace NyxarConcord.Services;

/// <summary>
/// Diagnóstico do app: grava um log de tudo que importa (rede, presença, voz,
/// arquivos, erros) num arquivo de texto, para investigar problemas.
/// Arquivo: %AppData%\NyxarConcord\logs\nyxar-diag.log
/// </summary>
public static class Diag
{
    private static readonly object _lock = new();
    private static bool _enabled;

    public static string LogDir { get; private set; } = "";
    public static string LogPath { get; private set; } = "";

    static Diag()
    {
        try
        {
            LogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NyxarConcord", "logs");
            Directory.CreateDirectory(LogDir);
            LogPath = Path.Combine(LogDir, "nyxar-diag.log");

            // Reinicia o arquivo se ficar grande (>5 MB).
            try { if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5_000_000) File.Delete(LogPath); }
            catch { }

            _enabled = true;
            Log("APP", $"==== Nyxar Concord iniciado (v{UpdateService.CurrentVersion}) ====");
        }
        catch { _enabled = false; }
    }

    /// <summary>Registra uma linha no log. category = área (RELAY, PRESENCE, VOICE, FILE...).</summary>
    public static void Log(string category, string message)
    {
        if (!_enabled) return;
        try
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}";
            lock (_lock) { File.AppendAllText(LogPath, line, Encoding.UTF8); }
        }
        catch { }
    }
}
