using Dockbox.Services;

namespace Dockbox.Tests;

public sealed class AutostartTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\Dockbox\\Dockbox.exe\"", @"C:\Program Files\Dockbox\Dockbox.exe")]
    [InlineData("\"C:\\Apps\\Dockbox.exe\" --minimizado", @"C:\Apps\Dockbox.exe")]
    [InlineData(@"C:\Apps\Dockbox.exe", @"C:\Apps\Dockbox.exe")]
    [InlineData(@"C:\Apps\Dockbox.exe --x", @"C:\Apps\Dockbox.exe")]
    public void ParseCommand_TiraAspasEArgumentos(string command, string expected) =>
        Assert.Equal(expected, Autostart.ParseCommand(command));

    [Theory]
    [InlineData(@"C:\Users\ana\Downloads\Dockbox.exe", true)]
    [InlineData(@"C:\Users\ana\Downloads\v0.3\Dockbox.exe", true)]
    [InlineData(@"C:\Users\ana\AppData\Local\Temp\x\Dockbox.exe", true)]
    [InlineData(@"C:\Users\ana\AppData\Local\Programs\Dockbox\Dockbox.exe", false)]
    [InlineData(@"C:\Users\ana\DownloadsAntigos\Dockbox.exe", false)]
    public void IsInFolders_ReconheceDownloadsETemp(string exe, bool expected)
    {
        var folders = new[] { @"C:\Users\ana\Downloads", @"C:\Users\ana\AppData\Local\Temp\", @"C:\Users\ana\Desktop" };
        Assert.Equal(expected, Autostart.IsInFolders(exe, folders));
    }

    [Fact]
    public void InstallPath_FicaEmLocalAppDataPrograms()
    {
        Assert.EndsWith(Path.Combine("Programs", "Dockbox", "Dockbox.exe"), Autostart.InstallPath);
    }
}
