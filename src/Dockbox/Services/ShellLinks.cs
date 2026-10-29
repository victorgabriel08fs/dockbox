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

    public static string? ResolveTarget(string lnkPath) => Cache.GetOrAdd(lnkPath, p => Read(p).Target);

    /// <summary>Lê destino e argumentos do atalho, sem cache (usado na checagem de segurança).</summary>
    public static (string? Target, string? Arguments) Read(string lnkPath)
    {
        if (!File.Exists(lnkPath)) return (null, null);
        object? obj = null;
        try
        {
            obj = new ShellLinkCoClass();
            ((IPersistFile)obj).Load(lnkPath, 0);
            var link = (IShellLinkW)obj;
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string target = sb.ToString();
            var args = new StringBuilder(4096);
            link.GetArguments(args, args.Capacity);
            return (string.IsNullOrWhiteSpace(target) ? null : Environment.ExpandEnvironmentVariables(target),
                    args.Length == 0 ? null : args.ToString());
        }
        catch
        {
            return (null, null);
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
        // App da Store fixado como shell:AppsFolder\<AppUserModelID>
        const string appsFolder = @"shell:AppsFolder\";
        if (full.StartsWith(appsFolder, StringComparison.OrdinalIgnoreCase))
        {
            string id = full[appsFolder.Length..];
            return id.Contains('!') ? RunningApps.AumidKey(id) : null;
        }
        if (full.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            full = ResolveTarget(full) ?? full;
        if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        try { return Path.GetFullPath(full).ToLowerInvariant(); }
        catch { return full.ToLowerInvariant(); }
    }
}
