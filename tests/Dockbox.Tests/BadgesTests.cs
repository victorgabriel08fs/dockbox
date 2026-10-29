using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class BadgesTests
{
    [Theory]
    [InlineData("(3) WhatsApp", 3)]
    [InlineData("(12) Discord", 12)]
    [InlineData("  (1) Caixa de entrada", 1)]
    [InlineData("(99+) Slack", 99)]
    [InlineData("(1500) Muitas", 1500)]
    public void TituloComNumeroNoComeco(string title, int expected) => Assert.Equal(expected, Badges.ParseCount(title));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("WhatsApp")]
    [InlineData("Documento (1).docx - Word")]   // número no meio não conta
    [InlineData("(2).txt - Bloco de Notas")]     // nome de arquivo, sem espaço depois
    [InlineData("(2)")]                          // só o número
    [InlineData("(0) Nada")]
    [InlineData("(abc) Texto")]
    [InlineData("() Vazio")]
    [InlineData("(1234567) Grande demais")]
    [InlineData("(3 WhatsApp")]
    public void TituloSemContador(string? title) => Assert.Null(Badges.ParseCount(title));

    [Fact]
    public void VariasJanelas_UsaOMaior() =>
        Assert.Equal(7, Badges.CountFor(new[] { "(2) Conversa", "Outra", "(7) Grupo", null }));

    [Fact]
    public void NenhumaJanelaComContador_Null() => Assert.Null(Badges.CountFor(new[] { "A", "B" }));

    [Theory]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(1500, "99+")]
    public void Formato(int n, string expected) => Assert.Equal(expected, Badges.Format(n));
}

public sealed class AttentionTrackerTests
{
    private static readonly IntPtr Chrome1 = new(1), Chrome2 = new(2), Word = new(3), Other = new(9);

    private static Dictionary<IntPtr, string[]> Windows(params IntPtr[] open)
    {
        var all = new Dictionary<IntPtr, string[]>
        {
            [Chrome1] = new[] { @"c:\chrome.exe" },
            [Chrome2] = new[] { @"c:\chrome.exe" },
            [Word] = new[] { @"c:\winword.exe" },
            [Other] = new[] { @"c:\outro.exe", "aumid:outro" },
        };
        return open.ToDictionary(h => h, h => all[h]);
    }

    [Fact]
    public void JanelaQuePisca_PedeAtencaoAteOAppIrParaAFrente()
    {
        var t = new AttentionTracker();
        Assert.True(t.Flash(Chrome1, Word));
        Assert.Equal(new[] { @"c:\chrome.exe" }, t.Update(Windows(Chrome1, Chrome2, Word), Word));

        // Outra janela do mesmo app foi para a frente: o app já foi visto.
        Assert.Empty(t.Update(Windows(Chrome1, Chrome2, Word), Chrome2));
        Assert.Equal(0, t.Count);
    }

    [Fact]
    public void JanelaQueJaEstaNaFrente_NaoPedeAtencao()
    {
        var t = new AttentionTracker();
        Assert.False(t.Flash(Word, Word));
        Assert.False(t.Flash(IntPtr.Zero, Word));
        Assert.Equal(0, t.Count);
    }

    [Fact]
    public void JanelaFechada_SaiDaLista()
    {
        var t = new AttentionTracker();
        t.Flash(Chrome1, Word);
        Assert.Empty(t.Update(Windows(Word), Word));
        Assert.Equal(0, t.Count);
    }

    [Fact]
    public void DevolveTodasAsChavesDoApp()
    {
        var t = new AttentionTracker();
        t.Flash(Other, Word);
        var keys = t.Update(Windows(Other, Word), Word);
        Assert.Contains(@"c:\outro.exe", keys);
        Assert.Contains("aumid:outro", keys);
    }

    [Fact]
    public void PiscarDuasVezes_ContaUmaJanela()
    {
        var t = new AttentionTracker();
        Assert.True(t.Flash(Chrome1, Word));
        Assert.False(t.Flash(Chrome1, Word));
        Assert.Equal(1, t.Count);
    }
}
