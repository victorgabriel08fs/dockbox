using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class DefaultItemsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dockbox-term-" + Guid.NewGuid().ToString("N"));
    private string Sys => Path.Combine(_root, "System32");
    private string Local => Path.Combine(_root, "Local");

    public DefaultItemsTests()
    {
        Directory.CreateDirectory(Sys);
        Directory.CreateDirectory(Local);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    [Fact]
    public void Terminal_ComWindowsTerminal_AbreOWindowsTerminal()
    {
        Touch(Path.Combine(Local, @"Microsoft\WindowsApps\wt.exe"));
        Touch(Path.Combine(Sys, @"WindowsPowerShell\v1.0\powershell.exe"));
        var e = SettingsStore.TerminalEntry(Sys, Local);
        Assert.Equal(@"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", e.Path);
    }

    [Fact]
    public void Terminal_SemWindowsTerminal_UsaPowerShell()
    {
        Touch(Path.Combine(Sys, @"WindowsPowerShell\v1.0\powershell.exe"));
        var e = SettingsStore.TerminalEntry(Sys, Local);
        Assert.EndsWith(@"powershell.exe", e.Path);
    }

    [Fact]
    public void Terminal_SemNada_CaiParaCmd()
    {
        var e = SettingsStore.TerminalEntry(Sys, Local);
        Assert.EndsWith("cmd.exe", e.Path);
        Assert.Equal("Terminal", e.Name);
    }
}
