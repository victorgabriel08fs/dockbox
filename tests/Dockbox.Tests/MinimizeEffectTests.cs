using Dockbox.Core;
using Dockbox.Services;

namespace Dockbox.Tests;

public sealed class MinimizeMathTests
{
    private static readonly (double X, double Y, double W, double H) Win = (100, 100, 800, 600);
    private static readonly (double X, double Y, double W, double H) Icon = (900, 1000, 54, 54);

    [Theory]
    [InlineData(MinimizeEffect.Scale)]
    [InlineData(MinimizeEffect.Genie)]
    public void ComecaNaJanelaETerminaNoIcone(MinimizeEffect effect)
    {
        Assert.Equal(Win, MinimizeMath.At(effect, true, Win, Icon, 0));
        Assert.Equal(Icon, MinimizeMath.At(effect, true, Win, Icon, 1));
        Assert.Equal(Icon, MinimizeMath.At(effect, false, Win, Icon, 1));
    }

    [Fact]
    public void Escala_EncolheSempre()
    {
        double last = double.MaxValue;
        for (double t = 0; t <= 1.0001; t += 0.05)
        {
            var r = MinimizeMath.At(MinimizeEffect.Scale, true, Win, Icon, t);
            Assert.True(r.W <= last + 1e-9);
            last = r.W;
        }
    }

    [Fact]
    public void Genio_DockEmbaixo_EstreitaAntesDeDescer()
    {
        // No meio do caminho, a largura já andou bem mais que a altura.
        var r = MinimizeMath.At(MinimizeEffect.Genie, true, Win, Icon, 0.4);
        double widthDone = (Win.W - r.W) / (Win.W - Icon.W);
        double heightDone = (Win.H - r.H) / (Win.H - Icon.H);
        Assert.True(widthDone > 0.6, $"largura {widthDone}");
        Assert.True(heightDone < 0.2, $"altura {heightDone}");
    }

    [Fact]
    public void Genio_DockDoLado_EstreitaAAlturaAntes()
    {
        var r = MinimizeMath.At(MinimizeEffect.Genie, false, Win, Icon, 0.4);
        Assert.True((Win.H - r.H) / (Win.H - Icon.H) > 0.6);
        Assert.True((Win.W - r.W) / (Win.W - Icon.W) < 0.2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(double.NaN)]
    public void TempoForaDoIntervalo_FicaNosLimites(double t)
    {
        var r = MinimizeMath.At(MinimizeEffect.Scale, true, Win, Icon, t);
        Assert.True(r == Win || r == Icon);
    }

    [Fact]
    public void Duracao() 
    {
        Assert.Equal(0, MinimizeMath.Duration(MinimizeEffect.None));
        Assert.InRange(MinimizeMath.Duration(MinimizeEffect.Scale), 0.15, 0.4);
        Assert.True(MinimizeMath.Duration(MinimizeEffect.Genie) > MinimizeMath.Duration(MinimizeEffect.Scale));
    }

    [Fact]
    public void Opacidade_SomeUmPoucoSoNoFim()
    {
        Assert.Equal(1, MinimizeMath.Opacity(0));
        Assert.Equal(1, MinimizeMath.Opacity(0.7));
        Assert.Equal(0.4, MinimizeMath.Opacity(1), 3);
    }

    [Fact]
    public void RetanguloSalvo_SomaODeslocamentoDaBarraDeTarefas()
    {
        // Barra de tarefas à esquerda com 60 px: a área de trabalho começa em x = 60.
        var r = MinimizeMath.RestoredRect((10, 20, 410, 320), false, (0, 0, 1920, 1080), (60, 0, 1920, 1080));
        Assert.Equal((70d, 20d, 400d, 300d), r);
    }

    [Fact]
    public void RetanguloSalvo_Maximizada_OcupaAAreaDeTrabalho()
    {
        var r = MinimizeMath.RestoredRect((10, 20, 410, 320), true, (1920, 0, 3840, 1080), (1920, 0, 3840, 1040));
        Assert.Equal((1920d, 0d, 1920d, 1040d), r);
    }
}

[Collection(SettingsCollection.Name)]
public sealed class MinimizeEffectSettingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockbox-min-" + Guid.NewGuid().ToString("N"));

    public MinimizeEffectSettingTests()
    {
        Directory.CreateDirectory(_dir);
        SettingsStore.UseFolder(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Padrao_Escala() => Assert.Equal(MinimizeEffect.Scale, new Appearance().MinimizeEffect);

    [Theory]
    [InlineData("\"Genie\"", MinimizeEffect.Genie)]
    [InlineData("\"None\"", MinimizeEffect.None)]
    [InlineData("\"Scale\"", MinimizeEffect.Scale)]
    public void LidoDoArquivo(string json, MinimizeEffect expected)
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ \"Appearance\": { \"MinimizeEffect\": " + json + " } }");
        SettingsStore.Load();
        Assert.Equal(expected, SettingsStore.Current.Appearance.MinimizeEffect);
    }

    [Fact]
    public void AnimacaoDoWindows_ArquivoDeRecuperacaoNaPastaDoDockbox() =>
        Assert.StartsWith(_dir, SystemMinimizeAnimation.MarkerPath);
}
