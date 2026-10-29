using System.IO;
using System.Text;

namespace Dockbox.Core;

/// <summary>O texto do widget de notas rápidas, em %APPDATA%\Dockbox\notas.txt.</summary>
public static class QuickNotes
{
    /// <summary>Limite para não travar a janela com um arquivo enorme.</summary>
    public const int MaxLength = 1_000_000;

    public static string FilePath => Path.Combine(SettingsStore.FolderPath, "notas.txt");

    public static string Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return "";
            string text = File.ReadAllText(FilePath, Encoding.UTF8);
            return text.Length > MaxLength ? text[..MaxLength] : text;
        }
        catch { return ""; }
    }

    /// <summary>Grava num arquivo temporário e troca, para não perder a nota se o PC desligar no meio.</summary>
    public static bool Save(string text)
    {
        try
        {
            if (text.Length > MaxLength) text = text[..MaxLength];
            Directory.CreateDirectory(SettingsStore.FolderPath);
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Primeira linha com texto, para o rótulo do widget.</summary>
    public static string FirstLine(string text, int max = 40)
    {
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            return line.Length <= max ? line : line[..(max - 1)].TrimEnd() + "…";
        }
        return "";
    }
}
