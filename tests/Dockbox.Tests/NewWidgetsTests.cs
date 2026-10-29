using System.Globalization;
using Dockbox.Core;

namespace Dockbox.Tests;

public class WidgetMathTests
{
    [Theory]
    [InlineData(0.50, 120, 0.52)]
    [InlineData(0.50, -120, 0.48)]
    [InlineData(0.50, 360, 0.56)]
    [InlineData(0.99, 120, 1.0)]
    [InlineData(0.01, -120, 0.0)]
    [InlineData(0.0, -120, 0.0)]
    [InlineData(1.0, 120, 1.0)]
    [InlineData(0.333, 120, 0.36)] // arredonda ao passo de 2%
    public void StepVolume(double current, int delta, double expected) =>
        Assert.Equal(expected, WidgetMath.StepVolume(current, delta), 4);

    [Fact]
    public void StepVolume_ValorInvalido_ComecaDoZero() =>
        Assert.Equal(0.02, WidgetMath.StepVolume(double.NaN, 120), 4);

    [Fact]
    public void VolumeGlyph_MudoTemGlifoProprio()
    {
        Assert.Equal("", WidgetMath.VolumeGlyph(0.8, muted: true));
        Assert.NotEqual(WidgetMath.VolumeGlyph(0.1, false), WidgetMath.VolumeGlyph(0.9, false));
    }

    [Theory]
    [InlineData(1000, 3000, 2.0, 1000)]
    [InlineData(5000, 1000, 1.0, 0)]  // contador reiniciou
    [InlineData(1000, 2000, 0.0, 0)]  // sem tempo medido
    public void Rate(long before, long now, double seconds, double expected) =>
        Assert.Equal(expected, WidgetMath.Rate(before, now, seconds));

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(0.4, "0 B/s")]
    [InlineData(512, "512 B/s")]
    [InlineData(2048, "2.0 KB/s")]
    [InlineData(820 * 1024, "820 KB/s")]
    [InlineData(1.3 * 1024 * 1024, "1.3 MB/s")]
    [InlineData(12 * 1024 * 1024, "12 MB/s")]
    [InlineData(3.0 * 1024 * 1024 * 1024, "3.0 GB/s")]
    [InlineData(double.NaN, "0 B/s")]
    public void FormatSpeed(double bps, string expected) =>
        Assert.Equal(expected, WidgetMath.FormatSpeed(bps, CultureInfo.InvariantCulture));

    [Fact]
    public void FormatSpeed_UsaVirgulaEmPortugues() =>
        Assert.Equal("1,5 MB/s", WidgetMath.FormatSpeed(1.5 * 1024 * 1024, new CultureInfo("pt-BR")));
}

public class FocusTimerTests
{
    private static readonly DateTime T0 = new(2026, 10, 29, 10, 0, 0);

    [Fact]
    public void ComecaParadoCom25Minutos()
    {
        var t = new FocusTimer();
        Assert.True(t.IsIdle);
        Assert.False(t.Running);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Remaining(T0));
        Assert.Equal(0, t.Fraction(T0));
    }

    [Fact]
    public void ContaPausaERetoma()
    {
        var t = new FocusTimer();
        t.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining(T0.AddMinutes(10)));
        t.Pause(T0.AddMinutes(10));
        Assert.False(t.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(15), t.Remaining(T0.AddHours(3))); // pausado não anda
        t.Start(T0.AddHours(3));
        Assert.Equal(TimeSpan.FromMinutes(10), t.Remaining(T0.AddHours(3).AddMinutes(5)));
        Assert.Equal(0.6, t.Fraction(T0.AddHours(3).AddMinutes(5)), 6);
    }

    [Fact]
    public void TerminaENaoPassaDoFim()
    {
        var t = new FocusTimer();
        t.SetMinutes(5);
        t.Start(T0);
        Assert.False(t.IsDone(T0.AddMinutes(4)));
        Assert.True(t.IsDone(T0.AddMinutes(5)));
        Assert.Equal(TimeSpan.Zero, t.Remaining(T0.AddMinutes(9)));
        Assert.Equal(1, t.Fraction(T0.AddMinutes(9)));
    }

    [Fact]
    public void IniciarDepoisDeTerminar_RecomecaDoZero()
    {
        var t = new FocusTimer();
        t.SetMinutes(5);
        t.Start(T0);
        t.Pause(T0.AddMinutes(6));
        t.Start(T0.AddMinutes(7));
        Assert.Equal(TimeSpan.FromMinutes(5), t.Remaining(T0.AddMinutes(7)));
    }

    [Fact]
    public void RelogioQueVolta_NaoDaTempoNegativo()
    {
        var t = new FocusTimer();
        t.Start(T0);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Remaining(T0.AddMinutes(-3)));
    }

    [Fact]
    public void SetMinutes_ZeraELimita()
    {
        var t = new FocusTimer();
        t.Start(T0);
        t.SetMinutes(50);
        Assert.True(t.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(50), t.Duration);
        t.SetMinutes(0);
        Assert.Equal(TimeSpan.FromMinutes(1), t.Duration);
        t.SetMinutes(10_000);
        Assert.Equal(TimeSpan.FromMinutes(180), t.Duration);
    }

    [Fact]
    public void Reset_VoltaAoInicio()
    {
        var t = new FocusTimer();
        t.Start(T0);
        t.Reset();
        Assert.True(t.IsIdle);
        Assert.Equal(TimeSpan.FromMinutes(25), t.Remaining(T0.AddMinutes(10)));
    }

    [Theory]
    [InlineData(1500, "25:00")]
    [InlineData(299.2, "5:00")]   // arredonda para cima
    [InlineData(0.4, "0:01")]
    [InlineData(0, "0:00")]
    [InlineData(-5, "0:00")]
    [InlineData(3900, "1:05:00")]
    public void Format(double seconds, string expected) =>
        Assert.Equal(expected, FocusTimer.Format(TimeSpan.FromSeconds(seconds)));
}

public class ClipboardHistoryTests
{
    [Fact]
    public void MaisNovoPrimeiro_ERepetidoSobe()
    {
        var h = new ClipboardHistory();
        h.Add("a");
        h.Add("b");
        h.Add("c");
        h.Add("a");
        Assert.Equal(new[] { "a", "c", "b" }, h.Items);
    }

    [Fact]
    public void GuardaNoMaximo10()
    {
        var h = new ClipboardHistory();
        for (int i = 0; i < 25; i++) h.Add("item " + i);
        Assert.Equal(ClipboardHistory.MaxItems, h.Items.Count);
        Assert.Equal("item 24", h.Items[0]);
        Assert.Equal("item 15", h.Items[^1]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t")]
    public void IgnoraVazio(string? text)
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add(text));
        Assert.Empty(h.Items);
    }

    [Fact]
    public void IgnoraTextoEnorme()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add(new string('x', ClipboardHistory.MaxLength + 1)));
        Assert.True(h.Add(new string('x', ClipboardHistory.MaxLength)));
    }

    [Fact]
    public void Clear_Esvazia()
    {
        var h = new ClipboardHistory();
        h.Add("segredo");
        h.Clear();
        Assert.Empty(h.Items);
    }

    [Theory]
    [InlineData("curto", "curto")]
    [InlineData("  várias\r\n  linhas\tcom   espaços ", "várias linhas com espaços")]
    [InlineData("0123456789abcdefghij", "012345678…")]
    public void Preview(string text, string expected) =>
        Assert.Equal(expected, ClipboardHistory.Preview(text, 10));
}

[Collection(SettingsCollection.Name)]
public sealed class QuickNotesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockbox-notes-" + Guid.NewGuid().ToString("N"));

    public QuickNotesTests() => SettingsStore.UseFolder(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void SemArquivo_Vazio() => Assert.Equal("", QuickNotes.Load());

    [Fact]
    public void SalvaELe_ComAcentosEQuebras()
    {
        const string text = "Comprar pão\r\nLigar para a Ana ✓\n";
        Assert.True(QuickNotes.Save(text));
        Assert.Equal(text, QuickNotes.Load());
        Assert.False(File.Exists(QuickNotes.FilePath + ".tmp"));
    }

    [Fact]
    public void TextoEnorme_ECortado()
    {
        QuickNotes.Save(new string('a', QuickNotes.MaxLength + 50));
        Assert.Equal(QuickNotes.MaxLength, QuickNotes.Load().Length);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("\n\n  Primeira  \nSegunda", "Primeira")]
    [InlineData("uma linha bem comprida que passa do limite", "uma linha…")]
    public void FirstLine(string text, string expected) => Assert.Equal(expected, QuickNotes.FirstLine(text, 10));
}

[Collection(SettingsCollection.Name)]
public sealed class NewWidgetsSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockbox-nw-" + Guid.NewGuid().ToString("N"));

    public NewWidgetsSettingsTests()
    {
        Directory.CreateDirectory(_dir);
        SettingsStore.UseFolder(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void WidgetsNovos_VemDesligados_EAntesDaLixeira()
    {
        // Configuração de uma versão antiga: Lixeira no fim.
        File.WriteAllText(SettingsStore.FilePath, """
        { "Widgets": [ { "Kind": "clock", "Enabled": true }, { "Kind": "calendar" }, { "Kind": "weather" },
                       { "Kind": "system", "Enabled": false }, { "Kind": "battery" }, { "Kind": "media" },
                       { "Kind": "trash", "Enabled": true } ] }
        """);
        SettingsStore.Load();
        var w = SettingsStore.Current.Widgets;
        Assert.Equal("trash", w[^1].Kind);
        foreach (var kind in new[] { "volume", "network", "notes", "timer", "clipboard" })
        {
            var cfg = Assert.Single(w, x => x.Kind == kind);
            Assert.False(cfg.Enabled);
        }
    }

    [Fact]
    public void InstalacaoNova_LixeiraNoFim()
    {
        SettingsStore.Load();
        Assert.Equal("trash", SettingsStore.Current.Widgets[^1].Kind);
    }
}
