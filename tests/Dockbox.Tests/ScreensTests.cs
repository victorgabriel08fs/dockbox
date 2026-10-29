using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Tests;

[Collection(SettingsCollection.Name)] // usa o idioma (Loc), que é global
public sealed class ScreensTests
{
    private static RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    // Principal 1920×1080 à esquerda; secundário 2560×1440 à direita, com escala 150%.
    private static readonly ScreenInfo Main = new(@"\\.\DISPLAY1", R(0, 0, 1920, 1080), R(0, 0, 1920, 1040), true, 1.0);
    private static readonly ScreenInfo Second = new(@"\\.\DISPLAY2", R(1920, 0, 4480, 1440), R(1920, 0, 4480, 1440), false, 1.5);
    private static readonly List<ScreenInfo> Two = new() { Main, Second };

    [Theory]
    [InlineData(@"\\.\DISPLAY2", 2)]
    [InlineData(@"\\.\DISPLAY10", 10)]
    [InlineData("", 0)]
    [InlineData("sem-numero", 0)]
    public void NumberOf_LeONumeroDoFim(string device, int expected) =>
        Assert.Equal(expected, Screens.NumberOf(device));

    [Fact]
    public void Label_MostraNumeroResolucaoEPrincipal()
    {
        Loc.Apply("pt-BR");
        Assert.Equal("1 · 1920×1080 (principal)", Main.Label);
        Assert.Equal("2 · 2560×1440", Second.Label);
    }

    [Fact]
    public void Pick_PrincipalPorPadrao() =>
        Assert.Same(Main, Screens.Pick(Two, MonitorMode.Primary, @"\\.\DISPLAY2", null));

    [Fact]
    public void Pick_MonitorEscolhido() =>
        Assert.Same(Second, Screens.Pick(Two, MonitorMode.Choose, @"\\.\display2", null));

    [Fact]
    public void Pick_MonitorEscolhidoDesconectadoVoltaParaOPrincipal() =>
        Assert.Same(Main, Screens.Pick(Two, MonitorMode.Choose, @"\\.\DISPLAY3", null));

    [Fact]
    public void Pick_SeguirOMouseUsaOUltimoMonitor()
    {
        Assert.Same(Second, Screens.Pick(Two, MonitorMode.FollowMouse, null, @"\\.\DISPLAY2"));
        Assert.Same(Main, Screens.Pick(Two, MonitorMode.FollowMouse, null, null));
    }

    [Fact]
    public void Pick_SemPrincipalUsaOPrimeiro() =>
        Assert.Same(Second, Screens.Pick(new List<ScreenInfo> { Second }, MonitorMode.Primary, null, null));

    [Fact]
    public void Pick_SemMonitores() =>
        Assert.Null(Screens.Pick(new List<ScreenInfo>(), MonitorMode.Primary, null, null));

    [Theory]
    [InlineData(3000, 1439, DockEdge.Bottom, @"\\.\DISPLAY2")]
    [InlineData(500, 1079, DockEdge.Bottom, @"\\.\DISPLAY1")]
    [InlineData(500, 1000, DockEdge.Bottom, null)]
    [InlineData(3000, 0, DockEdge.Top, @"\\.\DISPLAY2")]
    [InlineData(0, 500, DockEdge.Left, @"\\.\DISPLAY1")]
    [InlineData(4479, 500, DockEdge.Right, @"\\.\DISPLAY2")]
    // A borda direita do principal encosta no secundário: o mouse só passa por ali.
    [InlineData(1919, 500, DockEdge.Right, null)]
    [InlineData(1920, 500, DockEdge.Left, null)]
    [InlineData(9999, 9999, DockEdge.Bottom, null)]
    public void AtEdge_AchaOMonitorDaBorda(int x, int y, DockEdge edge, string? expected) =>
        Assert.Equal(expected, Screens.AtEdge(Two, x, y, edge)?.Device);
}
