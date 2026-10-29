using System.IO;

namespace Dockbox.Services;

public sealed record InstalledApp(string Name, string Path);

/// <summary>Apps instalados: atalhos do Menu Iniciar e apps da Microsoft Store.</summary>
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

        foreach (var app in StoreApps())
            apps.TryAdd(app.Name, app); // se já existe atalho com o mesmo nome, fica o atalho

        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Apps da Store (Calculadora, Fotos, Configurações...) listados na pasta virtual shell:AppsFolder.
    /// Viram itens "shell:AppsFolder\&lt;AppUserModelID&gt;".
    /// </summary>
    public static List<InstalledApp> StoreApps()
    {
        var result = new List<InstalledApp>();
        // A API do Shell precisa de uma thread STA.
        var thread = new Thread(() =>
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                if (type == null) return;
                dynamic shell = Activator.CreateInstance(type)!;
                dynamic? folder = shell.NameSpace("shell:AppsFolder");
                if (folder == null) return;
                dynamic items = folder.Items();
                int count = items.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic item = items.Item(i);
                    string name = item.Name;
                    string id = item.Path;
                    // AUMID de app empacotado: "Pacote_hash!App". Caminhos de apps comuns já vêm do Menu Iniciar.
                    if (string.IsNullOrWhiteSpace(name) || !IsAumid(id)) continue;
                    if (SkipWords.Any(name.ToLowerInvariant().Contains)) continue;
                    result.Add(new InstalledApp(name, @"shell:AppsFolder\" + id));
                }
            }
            catch { /* sem a lista da Store, o seletor mostra só o Menu Iniciar */ }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(10));
        return result;
    }

    public static bool IsAumid(string? id) =>
        !string.IsNullOrEmpty(id) && id.Contains('!') && !id.Contains('\\') && !id.Contains('/');
}
