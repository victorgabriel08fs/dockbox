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
    public void AtalhoInexistente_NaoQuebra()
    {
        Assert.Null(ShellLinks.ResolveTarget(@"C:\nao\existe.lnk"));
        Assert.Null(ShellLinks.ExecutableKey(@"C:\nao\existe.lnk"));
    }
}
