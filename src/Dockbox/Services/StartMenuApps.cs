using System.IO;

namespace Dockbox.Services;

public sealed record InstalledApp(string Name, string Path);

/// <summary>Apps instalados, a partir dos atalhos do Menu Iniciar.</summary>
public static class StartMenuApps
{
    private static readonly string[] SkipWords =
        { "uninstall", "desinstalar", "readme", "leia-me", "help", "ajuda", "website", "documentation" };

    public static List<InstalledApp> Enumerate()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        };

        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                });
            }
            catch { continue; }

            foreach (var f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                string lower = name.ToLowerInvariant();
                if (SkipWords.Any(lower.Contains)) continue;
                apps.TryAdd(name, new InstalledApp(name, f));
            }
        }

        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
