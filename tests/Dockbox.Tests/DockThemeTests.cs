using System.Windows.Media;
using Dockbox.Core;

namespace Dockbox.Tests;

public class DockThemeTests
{
    [Fact]
    public void TemaClaroEEscuroFixos()
    {
        Assert.True(DockTheme.From(new Appearance { Theme = ThemeMode.Light }).IsLight);
        Assert.False(DockTheme.From(new Appearance { Theme = ThemeMode.Dark }).IsLight);
    }

    [Theory]
    [InlineData("#FFFFFF", true)]
    [InlineData("#F6EEE6", true)]
    [InlineData("#000000", false)]
    [InlineData("#1F2A44", false)]
    public void TemaPersonalizado_DecidePelaLuminancia(string color, bool light) =>
        Assert.Equal(light, DockTheme.From(new Appearance { Theme = ThemeMode.Custom, CustomColor = color }).IsLight);

    [Fact]
    public void CorPersonalizadaInvalida_NaoQuebra()
    {
        var t = DockTheme.From(new Appearance { Theme = ThemeMode.Custom, CustomColor = "xyz", AccentColor = "abc" });
        Assert.Equal(Color.FromRgb(0x0A, 0x84, 0xFF), t.Accent);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.72)]
    [InlineData(1.0)]
    public void OpacidadeVaiParaOAlfaDoFundo(double opacity)
    {
        var t = DockTheme.From(new Appearance { Theme = ThemeMode.Dark, Opacity = opacity });
        var bg = Assert.IsType<LinearGradientBrush>(t.Background);
        Assert.All(bg.GradientStops, s => Assert.Equal((byte)Math.Round(opacity * 255), s.Color.A));
        Assert.True(bg.IsFrozen);
    }

    [Fact]
    public void ContornoContinuaVisivelComOpacidadeZero()
    {
        var t = DockTheme.From(new Appearance { Theme = ThemeMode.Dark, Opacity = 0 });
        var border = Assert.IsType<SolidColorBrush>(t.Border);
        Assert.True(border.Color.A > 0);
    }
}
