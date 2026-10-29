using Dockbox.Core;

namespace Dockbox.Services;

/// <summary>Ajuda a comparar itens do Dock olhando para onde os atalhos apontam.</summary>
public static class DockItems
{
    /// <summary>Identidade de um item: um atalho vale pelo destino e argumentos dele.</summary>
    public static string Identity(string path, string? arguments = null)
    {
        string full = Environment.ExpandEnvironmentVariables(path ?? "").Trim().Trim('"');
        if (full.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (target, linkArgs) = ShellLinks.Read(full);
            if (!string.IsNullOrWhiteSpace(target))
            {
                string args = string.Join(" ", new[] { linkArgs, arguments }.Where(a => !string.IsNullOrWhiteSpace(a)));
                return ItemIdentity.Key(target, args);
            }
        }
        return ItemIdentity.Key(full, arguments);
    }

    /// <summary>Item já fixado que equivale a <paramref name="path"/>, ou null.</summary>
    public static DockEntry? FindDuplicate(IEnumerable<DockEntry> items, string path, string? arguments = null)
    {
        string key = Identity(path, arguments);
        return items.FirstOrDefault(e => Identity(e.Path, e.Arguments) == key);
    }
}
