using System.Diagnostics;
using System.IO;
using System.Windows;
using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

public static class AppLauncher
{
    public static bool Launch(DockEntry entry, IEnumerable<string>? files = null)
    {
        try
        {
            string path = Environment.ExpandEnvironmentVariables(entry.Path);
            string args = entry.Arguments ?? "";
            if (files != null)
            {
                string extra = string.Join(" ", files.Select(f => $"\"{f}\""));
                args = string.IsNullOrEmpty(args) ? extra : args + " " + extra;
            }

            AllowSetForegroundWindow(ASFW_ANY);
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = args,
            };
            if (File.Exists(path) && !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                psi.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir \"{entry.Name}\":\n{ex.Message}", "Dockbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public static void ShowInExplorer(string path)
    {
        try
        {
            string full = Environment.ExpandEnvironmentVariables(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = true });
        }
        catch { }
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }
}
