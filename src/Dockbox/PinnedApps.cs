using System.IO;
using System.Text.Json;

namespace Dockbox;

public sealed class PinnedApp
{
    public string Name { get; set; } = "";
    /// <summary>Caminho de um .exe, atalho .lnk, pasta ou arquivo. Aceita %VARIAVEIS%.</summary>
    public string Path { get; set; } = "";
    public string? Arguments { get; set; }
}

/// <summary>Lê e grava a lista de apps fixados em %APPDATA%\Dockbox\pinned.json.</summary>
public static class PinnedAppsStore
{
    public static string FolderPath { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dockbox");

    public static string FilePath { get; } = System.IO.Path.Combine(FolderPath, "pinned.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static List<PinnedApp> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<PinnedApp>>(File.ReadAllText(FilePath), JsonOptions);
                if (list != null) return list.Where(a => !string.IsNullOrWhiteSpace(a.Path)).ToList();
            }
        }
        catch
        {
            // JSON inválido: cai nos padrões, sem sobrescrever o arquivo do usuário.
            return Defaults();
        }

        var defaults = Defaults();
        Save(defaults);
        return defaults;
    }

    public static void Save(List<PinnedApp> apps)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(apps, JsonOptions));
        }
        catch
        {
            // Sem permissão de escrita: ignora, o Dock continua funcionando.
        }
    }

    private static List<PinnedApp> Defaults()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys = Environment.SystemDirectory;
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidates = new List<PinnedApp>
        {
            new() { Name = "Explorador de Arquivos", Path = System.IO.Path.Combine(win, "explorer.exe") },
            new() { Name = "Microsoft Edge", Path = System.IO.Path.Combine(pf86, @"Microsoft\Edge\Application\msedge.exe") },
            new() { Name = "Google Chrome", Path = System.IO.Path.Combine(pf, @"Google\Chrome\Application\chrome.exe") },
            new() { Name = "Google Chrome", Path = System.IO.Path.Combine(local, @"Google\Chrome\Application\chrome.exe") },
            new() { Name = "Bloco de Notas", Path = System.IO.Path.Combine(sys, "notepad.exe") },
            new() { Name = "Calculadora", Path = System.IO.Path.Combine(sys, "calc.exe") },
            new() { Name = "Paint", Path = System.IO.Path.Combine(sys, "mspaint.exe") },
            new() { Name = "Prompt de Comando", Path = System.IO.Path.Combine(sys, "cmd.exe") },
        };

        return candidates
            .Where(a => File.Exists(a.Path))
            .GroupBy(a => a.Name)
            .Select(g => g.First())
            .ToList();
    }
}
