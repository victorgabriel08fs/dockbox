using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class PreviewLayoutTests
{
    [Fact]
    public void CabeTudo_TamanhoNormal()
    {
        var (factor, count) = PreviewLayout.Arrange(3, 240, 20, 1800);
        Assert.Equal(1, factor);
        Assert.Equal(3, count);
    }

    [Fact]
    public void MuitasJanelas_EncolheAntesDeEsconder()
    {
        // 8 cartões de 240 não cabem em 1500, mas cabem encolhendo (8 × 187,5).
        var (factor, count) = PreviewLayout.Arrange(8, 240, 20, 1500);
        Assert.InRange(factor, PreviewLayout.MinFactor, 0.99);
        Assert.Equal(8, count);
        Assert.True(count * (20 + 220 * factor) <= 1500.001);
    }

    [Fact]
    public void JanelasDemais_MostraAsQueCabemNoMenorTamanho()
    {
        var (factor, count) = PreviewLayout.Arrange(40, 240, 20, 1500);
        Assert.Equal(PreviewLayout.MinFactor, factor);
        double small = 20 + 220 * PreviewLayout.MinFactor;
        Assert.Equal((int)Math.Floor(1500 / small), count);
        Assert.InRange(count, 1, 39);
    }

    [Fact]
    public void TelaMinuscula_MostraPeloMenosUma() =>
        Assert.Equal(1, PreviewLayout.Arrange(5, 240, 20, 50).Count);

    [Fact]
    public void SemJanelas_Nada() => Assert.Equal(0, PreviewLayout.Arrange(0, 240, 20, 1000).Count);

    [Fact]
    public void Miniatura_MantemProporcaoECentraliza()
    {
        // Janela 1920×1080 numa caixa 220×138: limitada pela largura.
        var (x, y, w, h) = PreviewLayout.Fit(1920, 1080, 220, 138);
        Assert.Equal(0, x, 3);
        Assert.Equal(220, w, 3);
        Assert.Equal(220 * 1080 / 1920.0, h, 3);
        Assert.Equal((138 - h) / 2, y, 3);

        // Janela em pé: limitada pela altura.
        (x, _, w, h) = PreviewLayout.Fit(600, 1000, 220, 138);
        Assert.Equal(138, h, 3);
        Assert.Equal(138 * 0.6, w, 3);
        Assert.Equal((220 - w) / 2, x, 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 100)]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    public void TamanhoInvalido_OcupaACaixa(double sw, double sh) =>
        Assert.Equal((0d, 0d, 220d, 138d), PreviewLayout.Fit(sw, sh, 220, 138));

    [Fact]
    public void SoAbreComMaisDeUmaJanela() => Assert.Equal(2, PreviewLayout.MinWindows);
}
