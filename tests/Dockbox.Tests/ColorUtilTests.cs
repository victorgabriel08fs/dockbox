using System.Windows.Media;
using Dockbox.Core;

namespace Dockbox.Tests;

public class ColorUtilTests
{
    [Theory]
    [InlineData("#0A84FF")]
    [InlineData("#fff")]
    [InlineData("#800A84FF")]
    [InlineData("Red")]
    public void IsValid_AceitaCoresWpf(string hex) => Assert.True(ColorUtil.IsValid(hex));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#GGGGGG")]
    [InlineData("0A84FF")] // sem # não é cor válida para o WPF
    [InlineData("#12345")]
    [InlineData("javascript:alert(1)")]
    public void IsValid_RejeitaLixo(string? hex) => Assert.False(ColorUtil.IsValid(hex));

    [Fact]
    public void Parse_UsaFallbackQuandoInvalido()
    {
        var fallback = Color.FromRgb(1, 2, 3);
        Assert.Equal(fallback, ColorUtil.Parse("nada", fallback));
        Assert.Equal(Color.FromRgb(0x0A, 0x84, 0xFF), ColorUtil.Parse("#0A84FF", fallback));
    }

    [Fact]
    public void ToHex_IgnoraAlfaEUsaMaiusculas() =>
        Assert.Equal("#0A84FF", ColorUtil.ToHex(Color.FromArgb(0x10, 0x0A, 0x84, 0xFF)));

    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(1.0, 255)]
    [InlineData(0.5, 128)]
    [InlineData(-3.0, 0)]
    [InlineData(7.0, 255)]
    public void WithAlpha_LimitaEntre0e1(double alpha, byte expected) =>
        Assert.Equal(expected, ColorUtil.WithAlpha(Colors.Red, alpha).A);

    [Fact]
    public void Mix_ExtremosDevolvemAsCoresOriginais()
    {
        var a = Color.FromArgb(255, 0, 0, 0);
        var b = Color.FromArgb(255, 200, 100, 50);
        Assert.Equal(a, ColorUtil.Mix(a, b, 0));
        Assert.Equal(b, ColorUtil.Mix(a, b, 1));
        Assert.Equal(Color.FromArgb(255, 100, 50, 25), ColorUtil.Mix(a, b, 0.5));
    }

    [Fact]
    public void Luminance_PretoZeroBrancoUm()
    {
        Assert.Equal(0, ColorUtil.Luminance(Colors.Black), 3);
        Assert.Equal(1, ColorUtil.Luminance(Colors.White), 3);
        Assert.True(ColorUtil.Luminance(Colors.Yellow) > ColorUtil.Luminance(Colors.Blue));
    }

    [Fact]
    public void Brush_VemCongelado() => Assert.True(ColorUtil.Brush(Colors.Red).IsFrozen);
}
