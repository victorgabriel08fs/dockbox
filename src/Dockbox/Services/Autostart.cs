using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Dockbox.Services;

/// <summary>Liga/desliga "Iniciar com o Windows" (chave Run do usuário).</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Dockbox";

    /// <summary>Onde o Dockbox fica "instalado" quando o usuário aceita mover o exe.</summary>
    public static string InstallPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Dockbox", "Dockbox.exe");

    public static bool IsEnabled => RegisteredPath() != null;

    /// <summary>Caminho gravado no registro (sem aspas), ou null.</summary>
    public static string? RegisteredPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string v ? ParseCommand(v) : null;
        }
        catch { return null; }
    }

    /// <summary>Tira as aspas e argumentos do comando gravado no Run.</summary>
    public static string ParseCommand(string command)
    {
        string c = command.Trim();
        if (c.StartsWith('"'))
        {
            int end = c.IndexOf('"', 1);
            return end > 0 ? c[1..end] : c.Trim('"');
        }
        int space = c.IndexOf(' ');
        return space > 0 && c[..space].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? c[..space] : c;
    }

    public static void Set(bool enabled, string? exe = null)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                exe ??= Environment.ProcessPath;
                if (exe != null) key.SetValue(ValueName, $"\"{exe}\""); // entre aspas: caminhos com espaço
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch { }
    }

    /// <summary>
    /// Na inicialização: se o autostart aponta para um exe que não existe mais (foi movido),
    /// passa a apontar para o exe atual. Um caminho que ainda existe não é mexido.
    /// </summary>
    public static void RepairIfMoved()
    {
        string? registered = RegisteredPath();
        string? current = Environment.ProcessPath;
        if (registered == null || current == null) return;
        if (string.Equals(registered, current, StringComparison.OrdinalIgnoreCase)) return;
        if (!File.Exists(registered)) Set(true, current);
    }

    /// <summary>Pastas de onde não é bom iniciar com o Windows: Downloads, Temp e Área de Trabalho.</summary>
    public static IEnumerable<string> VolatileFolders()
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(profile, "Downloads");
        yield return Path.GetTempPath();
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    /// <summary>True se o exe está dentro de uma das pastas (ou subpastas) passadas.</summary>
    public static bool IsInFolders(string exe, IEnumerable<string> folders)
    {
        string full;
        try { full = Path.GetFullPath(exe); }
        catch { return false; }
        foreach (var f in folders)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            string root = Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>O exe atual é um arquivo único (pode ser copiado sozinho)?</summary>
    public static bool IsSingleFile(string exe) =>
        !File.Exists(Path.Combine(Path.GetDirectoryName(exe) ?? "", "Dockbox.dll"));

    /// <summary>Copia o exe para <see cref="InstallPath"/>, liga o autostart de lá e abre a cópia.</summary>
    public static bool InstallAndRelaunch(string exe)
    {
        string target = InstallPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(exe, target, overwrite: true);
        Set(true, target);
        // A cópia espera este processo fechar antes de pegar a trava de instância única.
        Process.Start(new ProcessStartInfo(target, $"--after-pid {Environment.ProcessId}") { UseShellExecute = false });
        return true;
    }
}
