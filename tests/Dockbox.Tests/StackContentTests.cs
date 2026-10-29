using System.Text.Json;
using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class StackContentTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockbox-stack-" + Guid.NewGuid().ToString("N"));

    public StackContentTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFileSystemEntries(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch { }
    }

    private string Touch(string name, DateTime? created = null, DateTime? modified = null)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        if (created is DateTime c) File.SetCreationTime(p, c);
        if (modified is DateTime m) File.SetLastWriteTime(p, m);
        return p;
    }

    private static StackItem Item(string name, bool folder = false, int addedDay = 1, int modifiedDay = 1, string kind = "") =>
        new(@"C:\x\" + name, name, folder, new DateTime(2026, 1, addedDay), new DateTime(2026, 1, modifiedDay), kind);

    [Fact]
    public void Lista_SemOcultosNemArquivosDeSistema()
    {
        Touch("foto.png");
        Touch("desktop.ini");
        string hidden = Touch("segredo.txt");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        Directory.CreateDirectory(Path.Combine(_dir, "Projetos"));
        Touch("Atalho.lnk");

        var items = StackContent.List(_dir);
        Assert.Equal(new[] { "Atalho", "foto.png", "Projetos" }, items.Select(i => i.Name).OrderBy(n => n));
        Assert.True(items.Single(i => i.Name == "Projetos").IsFolder);
        Assert.Equal(".png", items.Single(i => i.Name == "foto.png").Kind);
    }

    [Fact]
    public void PastaQueNaoExiste_ListaVazia() => Assert.Empty(StackContent.List(Path.Combine(_dir, "nao-existe")));

    [Fact]
    public void Ordena_PorNome_IgnorandoMaiusculas()
    {
        var sorted = StackContent.Sort(new[] { Item("banana"), Item("Abacaxi"), Item("caju") }, StackSort.Name);
        Assert.Equal(new[] { "Abacaxi", "banana", "caju" }, sorted.Select(i => i.Name));
    }

    [Fact]
    public void Ordena_PorDataDeAdicao_MaisNovoPrimeiro()
    {
        var sorted = StackContent.Sort(new[] { Item("a", addedDay: 1), Item("b", addedDay: 9), Item("c", addedDay: 5) }, StackSort.DateAdded);
        Assert.Equal(new[] { "b", "c", "a" }, sorted.Select(i => i.Name));
    }

    [Fact]
    public void Ordena_PorDataDeModificacao_MaisNovoPrimeiro()
    {
        var sorted = StackContent.Sort(new[] { Item("a", modifiedDay: 3), Item("b", modifiedDay: 1), Item("c", modifiedDay: 7) }, StackSort.DateModified);
        Assert.Equal(new[] { "c", "a", "b" }, sorted.Select(i => i.Name));
    }

    [Fact]
    public void Ordena_PorTipo_PastasPrimeiroDepoisExtensao()
    {
        var sorted = StackContent.Sort(new[]
        {
            Item("z.txt", kind: ".txt"), Item("Docs", folder: true), Item("a.pdf", kind: ".pdf"), Item("b.txt", kind: ".txt"),
        }, StackSort.Kind);
        Assert.Equal(new[] { "Docs", "a.pdf", "b.txt", "z.txt" }, sorted.Select(i => i.Name));
    }

    [Theory]
    [InlineData(StackView.Auto, 5, DockEdge.Bottom, false, StackView.Fan)]
    [InlineData(StackView.Auto, 30, DockEdge.Bottom, false, StackView.Grid)]
    [InlineData(StackView.Auto, 0, DockEdge.Bottom, false, StackView.Grid)]   // vazia: painel com "Pasta vazia"
    [InlineData(StackView.Auto, 5, DockEdge.Left, false, StackView.Grid)]     // leque só com o Dock embaixo
    [InlineData(StackView.Fan, 5, DockEdge.Top, false, StackView.Grid)]
    [InlineData(StackView.Fan, 5, DockEdge.Bottom, true, StackView.Grid)]     // dentro de subpasta
    [InlineData(StackView.Fan, 10, DockEdge.Bottom, false, StackView.Fan)]
    [InlineData(StackView.Fan, 11, DockEdge.Bottom, false, StackView.Grid)]
    [InlineData(StackView.List, 3, DockEdge.Bottom, false, StackView.List)]
    [InlineData(StackView.Grid, 3, DockEdge.Bottom, false, StackView.Grid)]
    public void JeitoDeMostrar(StackView view, int count, DockEdge edge, bool sub, StackView expected) =>
        Assert.Equal(expected, StackContent.Resolve(view, count, edge, sub));

    [Fact]
    public void MaisRecente_PelaDataDeAdicao()
    {
        Assert.Equal("b", StackContent.Latest(new[] { Item("a", addedDay: 2), Item("b", addedDay: 8), Item("c", addedDay: 4) })!.Name);
        Assert.Null(StackContent.Latest(Array.Empty<StackItem>()));
    }

    [Theory]
    [InlineData(@"C:\a\x.txt", @"C:\Downloads", false, false, true)]   // mesmo disco: move
    [InlineData(@"D:\a\x.txt", @"C:\Downloads", false, false, false)]  // outro disco: copia
    [InlineData(@"C:\a\x.txt", @"C:\Downloads", true, false, false)]   // Ctrl: copia
    [InlineData(@"D:\a\x.txt", @"C:\Downloads", false, true, true)]    // Shift: move
    public void SoltarNaPasta_MoverOuCopiar(string source, string folder, bool ctrl, bool shift, bool move) =>
        Assert.Equal(move, StackContent.ShouldMove(source, folder, ctrl, shift));

    [Theory]
    [InlineData(@"C:\Pasta", @"C:\Pasta", true)]
    [InlineData(@"C:\Pasta", @"C:\Pasta\Sub", true)]      // pasta para dentro de uma subpasta dela
    [InlineData(@"C:\Pasta\x.txt", @"C:\Pasta", false)]
    [InlineData(@"C:\Pasta2", @"C:\Pasta", false)]
    [InlineData(@"C:\Pa", @"C:\Pasta", false)]
    public void NaoSoltaAPastaDentroDelaMesma(string path, string folder, bool inside) =>
        Assert.Equal(inside, StackContent.IsInside(path, folder));

    [Fact]
    public void Downloads_PorPadrao_ComoPilhaDosMaisRecentes()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Downloads"));
        var e = SettingsStore.DownloadsEntry(_dir)!;
        Assert.Equal(Path.Combine(_dir, "Downloads"), e.Path);
        Assert.Equal(StackSort.DateAdded, e.StackSort);
        Assert.True(e.StackShowsLatest);
        Assert.Null(SettingsStore.DownloadsEntry(Path.Combine(_dir, "sem-downloads")));
    }

    [Fact]
    public void OpcoesDaPasta_SalvasEDevolvidas()
    {
        var s = new DockSettings();
        s.Items.Add(new DockEntry { Name = "Docs", Path = @"C:\Docs", StackView = StackView.List, StackSort = StackSort.Kind, StackShowsLatest = true });
        var again = JsonSerializer.Deserialize<DockSettings>(JsonSerializer.Serialize(s, SettingsStore.JsonOptions), SettingsStore.JsonOptions)!;
        var e = again.Items.Single();
        Assert.Equal(StackView.List, e.StackView);
        Assert.Equal(StackSort.Kind, e.StackSort);
        Assert.True(e.StackShowsLatest);
    }

    [Fact]
    public void ConfiguracaoAntiga_PastasEmAutomaticoPorNome()
    {
        var s = JsonSerializer.Deserialize<DockSettings>("""{ "Items": [ { "Name": "Docs", "Path": "C:\\Docs" } ] }""", SettingsStore.JsonOptions)!;
        Assert.Equal(StackView.Auto, s.Items[0].StackView);
        Assert.Equal(StackSort.Name, s.Items[0].StackSort);
        Assert.False(s.Items[0].StackShowsLatest);
    }
}
