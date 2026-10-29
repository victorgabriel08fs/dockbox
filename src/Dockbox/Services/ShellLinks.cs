using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

/// <summary>Descobre para onde um atalho .lnk aponta (com cache).</summary>
public static class ShellLinks
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? ResolveTarget(string lnkPath) => Cache.GetOrAdd(lnkPath, Resolve);

    private static string? Resolve(string lnkPath)
    {
        if (!File.Exists(lnkPath)) return null;
        object? obj = null;
        try
        {
            obj = new ShellLinkCoClass();
            ((IPersistFile)obj).Load(lnkPath, 0);
            var sb = new StringBuilder(1024);
            ((IShellLinkW)obj).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string target = sb.ToString();
            return string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target);
        }
        catch
        {
            return null;
        }
        finally
        {
            if (obj != null) Marshal.ReleaseComObject(obj);
        }
    }

    /// <summary>Caminho do executável que o item representa (resolve .lnk), em minúsculas.</summary>
    public static string? ExecutableKey(string path)
    {
        string full = Environment.ExpandEnvironmentVariables(path);
        if (full.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            full = ResolveTarget(full) ?? full;
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        try { return Path.GetFullPath(full).ToLowerInvariant(); }
        catch { return full.ToLowerInvariant(); }
    }
}
