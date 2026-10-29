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
        if (!IsAllowed(entry)) return false;
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
            MessageBox.Show(Loc.F("Msg_OpenFailed", entry.Name, ex.Message), "Dockbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Avalia o risco de um caminho, olhando dentro de atalhos .lnk e arquivos .url.</summary>
    public static LaunchRisk Assess(string path, string? arguments)
    {
        path = Environment.ExpandEnvironmentVariables(path ?? "").Trim().Trim('"');
        string? linkTarget = null, urlTarget = null, args = arguments;
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var (target, linkArgs) = ShellLinks.Read(path);
            linkTarget = target;
            args = string.Join(" ", new[] { linkArgs, arguments }.Where(a => !string.IsNullOrWhiteSpace(a)));
        }
        else if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
        {
            try { urlTarget = LaunchPolicy.ParseUrlFile(File.ReadAllText(path)); }
            catch { }
        }
        return LaunchPolicy.Classify(path, args, linkTarget, urlTarget);
    }

    /// <summary>
    /// Bloqueia protocolos perigosos e pede confirmação (uma vez por item) para scripts e
    /// protocolos desconhecidos. Apps, pastas e links da web passam direto.
    /// </summary>
    public static bool IsAllowed(DockEntry entry)
    {
        var risk = Assess(entry.Path, entry.Arguments);
        if (risk == LaunchRisk.Blocked)
        {
            MessageBox.Show(Loc.F("Msg_Blocked", entry.Name, entry.Path),
                "Dockbox", MessageBoxButton.OK, MessageBoxImage.Stop);
            return false;
        }
        if (risk == LaunchRisk.Safe || entry.Trusted) return true;

        if (!ConfirmRisky(entry.Name, entry.Path, entry.Arguments, pinning: false)) return false;
        entry.Trusted = true;
        SettingsStore.Commit();
        return true;
    }

    /// <summary>Pergunta se o usuário quer mesmo abrir (ou fixar) um item que executa código.</summary>
    public static bool ConfirmRisky(string name, string path, string? arguments, bool pinning)
    {
        string what = string.IsNullOrWhiteSpace(arguments) ? path : $"{path} {arguments}";
        string question = Loc.T(pinning ? "Msg_Risky_PinQuestion" : "Msg_Risky_OpenQuestion");
        var answer = MessageBox.Show(
            Loc.F("Msg_Risky", name, what, question),
            "Dockbox", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return answer == MessageBoxResult.Yes;
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
