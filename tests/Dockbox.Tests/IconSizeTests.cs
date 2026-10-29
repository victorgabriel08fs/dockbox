using Dockbox.Services;

namespace Dockbox.Tests;

public sealed class IconSizeTests
{
    [Theory]
    [InlineData(30, 1.0, 32)]
    [InlineData(30, 1.25, 40)]
    [InlineData(30, 1.5, 48)]
    [InlineData(30, 2.0, 64)]
    [InlineData(26, 1.0, 32)]
    [InlineData(26, 1.75, 48)]
    [InlineData(32, 1.0, 32)]
    [InlineData(54, 1.8, 128)]
    [InlineData(96, 2.6, 256)]
    [InlineData(200, 3.0, 256)]
    public void PixelsFor_MenorTamanhoPadraoSemAmpliar(double dips, double scale, int expected) =>
        Assert.Equal(expected, IconLoader.PixelsFor(dips, scale));

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(0, 1.0)]
    [InlineData(30, 0)]
    [InlineData(30, double.PositiveInfinity)]
    public void PixelsFor_ValoresInvalidosNaoQuebram(double dips, double scale) =>
        Assert.InRange(IconLoader.PixelsFor(dips, scale), 16, 256);
}
