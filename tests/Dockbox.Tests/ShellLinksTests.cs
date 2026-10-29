using Dockbox.Services;

namespace Dockbox.Tests;

public class ShellLinksTests
{
    [Theory]
    [InlineData(@"C:\Windows\notepad.EXE", @"c:\windows\notepad.exe")]
    [InlineData(@"C:\Program Files\App\..\App\app.exe", @"c:\program files\app\app.exe")]
    public void ExecutableKey_NormalizaCaminhoEMinusculas(string input, string expected) =>
        Assert.Equal(expected, ShellLinks.ExecutableKey(input));

    [Fact]
    public void ExecutableKey_ExpandeVariaveis()
    {
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe").ToLowerInvariant();
        Assert.Equal(expected, ShellLinks.ExecutableKey(@"%SystemRoot%\notepad.exe"));
    }

    [Theory]
    [InlineData(@"C:\Users\x\Documents")]
    [InlineData(@"C:\Users\x\nota.txt")]
    [InlineData(@"C:\Users\x\script.ps1")]
    [InlineData("https://example.com")]
    public void ExecutableKey_NaoExeDevolveNull(string path) => Assert.Null(ShellLinks.ExecutableKey(path));

    [Fact]
    public void ExecutableKey_AppDaStoreUsaOAumid()
    {
        Assert.Equal("aumid:microsoft.windowscalculator_8wekyb3d8bbwe!app",
            ShellLinks.ExecutableKey(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"));
        Assert.Null(ShellLinks.ExecutableKey(@"shell:AppsFolder\{6D809377-6AF0-444B-8957-A3773F02200E}\App\app.exe"));
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", true)]
    [InlineData("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel", true)]
    [InlineData(@"{6D809377-6AF0-444B-8957-A3773F02200E}\App\app.exe", false)]
    [InlineData("Microsoft.Windows.Explorer", false)]
    [InlineData("", false)]
    public void IsAumid_ReconheceAppsEmpacotados(string id, bool expected) =>
        Assert.Equal(expected, StartMenuApps.IsAumid(id));

    [Fact]
    public void AtalhoInexistente_NaoQuebra()
    {
        Assert.Null(ShellLinks.ResolveTarget(@"C:\nao\existe.lnk"));
        Assert.Null(ShellLinks.ExecutableKey(@"C:\nao\existe.lnk"));
    }
}
