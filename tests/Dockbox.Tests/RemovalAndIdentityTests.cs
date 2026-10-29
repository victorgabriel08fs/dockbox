using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class RemovalAndIdentityTests
{
    private static List<DockEntry> Items(params string[] names) =>
        names.Select(n => new DockEntry { Name = n, Path = $@"C:\{n}.exe" }).ToList();

    [Fact]
    public void RemoverEDesfazer_VoltaNaMesmaPosicao()
    {
        RemovalHistory.Clear();
        var items = Items("a", "b", "c");
        var b = items[1];
        Assert.True(RemovalHistory.Remove(items, b));
        Assert.Equal(new[] { "a", "c" }, items.Select(i => i.Name));

        Assert.Same(b, RemovalHistory.Undo(items));
        Assert.Equal(new[] { "a", "b", "c" }, items.Select(i => i.Name));
        Assert.False(RemovalHistory.CanUndo);
    }

    [Fact]
    public void DesfazerVariasVezes_SegueAOrdemInversa()
    {
        RemovalHistory.Clear();
        var items = Items("a", "b", "c");
        var a = items[0];
        var c = items[2];
        RemovalHistory.Remove(items, a);
        RemovalHistory.Remove(items, c);
        Assert.Same(c, RemovalHistory.Undo(items));
        Assert.Same(a, RemovalHistory.Undo(items));
        Assert.Equal(new[] { "a", "b", "c" }, items.Select(i => i.Name));
        Assert.Null(RemovalHistory.Undo(items));
    }

    [Fact]
    public void Desfazer_ComListaMenor_InsereNoFim()
    {
        RemovalHistory.Clear();
        var items = Items("a", "b", "c");
        var c = items[2];
        RemovalHistory.Remove(items, c);
        items.Clear();
        RemovalHistory.Undo(items);
        Assert.Single(items);
    }

    [Fact]
    public void RemoverItemQueNaoEstaNaLista_NaoRegistra()
    {
        RemovalHistory.Clear();
        var items = Items("a");
        Assert.False(RemovalHistory.Remove(items, new DockEntry { Name = "x", Path = "x" }));
        Assert.False(RemovalHistory.CanUndo);
    }

    [Theory]
    [InlineData(@"C:\Apps\Code.exe", @"c:\apps\CODE.EXE")]
    [InlineData(@"C:\Apps\Pasta\", @"C:\Apps\Pasta")]
    [InlineData(@"C:\Apps\x\..\Code.exe", @"C:\Apps\Code.exe")]
    [InlineData("\"C:\\Apps\\Code.exe\"", @"C:\Apps\Code.exe")]
    [InlineData("HTTPS://GitHub.com", "https://github.com")]
    public void Normalize_CaminhosEquivalentes(string a, string b) =>
        Assert.Equal(ItemIdentity.Normalize(b), ItemIdentity.Normalize(a));

    [Fact]
    public void Key_ArgumentosDiferentesSaoItensDiferentes()
    {
        Assert.NotEqual(ItemIdentity.Key(@"C:\chrome.exe", "--profile-directory=A"), ItemIdentity.Key(@"C:\chrome.exe", "--profile-directory=B"));
        Assert.Equal(ItemIdentity.Key(@"C:\chrome.exe"), ItemIdentity.Key(@"c:\CHROME.exe", "  "));
    }

    [Fact]
    public void Normalize_ExpandeVariaveis()
    {
        string windir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        Assert.Equal(ItemIdentity.Normalize(Path.Combine(windir, "notepad.exe")), ItemIdentity.Normalize(@"%SystemRoot%\notepad.exe"));
    }
}
