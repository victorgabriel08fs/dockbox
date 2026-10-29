using System.IO;
using System.Reflection;

namespace Dockbox.Core;

/// <summary>
/// erros.log na pasta de configurações. Cada registro leva data/hora e versão; quando passa de
/// ~1 MB, o arquivo vira erros.1.log (o anterior é descartado) e um novo começa.
/// </summary>
public static class ErrorLog
{
    public const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(SettingsStore.FolderPath, "erros.log");
    public static string PreviousPath => Path.Combine(SettingsStore.FolderPath, "erros.1.log");

    private static readonly string Version =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? typeof(ErrorLog).Assembly.GetName().Version?.ToString(3) ?? "?";

    public static void Write(string context, Exception? ex = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(SettingsStore.FolderPath);
                RotateIfNeeded();
                string body = ex == null ? context : $"{context}: {ex}";
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [v{Version}] {body}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch { /* registrar erro nunca pode derrubar o Dock */ }
    }

    private static void RotateIfNeeded()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxBytes) return;
        File.Copy(FilePath, PreviousPath, overwrite: true);
        File.Delete(FilePath);
    }
}
