using Dockbox.Core;

namespace Dockbox.Tests;

public class GlobalShortcutTests
{
    [Theory]
    [InlineData(Hotkey.ModControl | Hotkey.ModAlt | Hotkey.ModShift, 0x48u, "Ctrl+Alt+Shift+H")]
    [InlineData(Hotkey.ModWin | Hotkey.ModShift, 0x44u, "Win+Shift+D")]
    [InlineData(Hotkey.ModAlt, 0x20u, "Alt+Space")]
    [InlineData(Hotkey.ModWin | Hotkey.ModAlt, 0x31u, "Win+Alt+1")]
    [InlineData(Hotkey.ModControl, 0x7Bu, "Ctrl+F12")]
    public void Format_MontaTextoQueParseEntende(uint mods, uint vk, string expected)
    {
        Assert.Equal(expected, Hotkey.Format(mods, vk));
        Assert.Equal((mods, vk), Hotkey.Parse(expected));
    }

    [Theory]
    [InlineData(0u, 0x41u)]                 // sem modificador
    [InlineData(Hotkey.ModControl, 0x0Du)]  // Enter não é aceito
    [InlineData(Hotkey.ModControl, 0x7Cu)]  // F13
    public void Format_RecusaTeclaQueNaoServe(uint mods, uint vk) => Assert.Null(Hotkey.Format(mods, vk));

    [Fact]
    public void Same_IgnoraOrdemEMaiusculas()
    {
        Assert.True(Hotkey.Same("shift+ctrl+alt+h", "Ctrl+Alt+Shift+H"));
        Assert.False(Hotkey.Same("Ctrl+H", "Ctrl+Shift+H"));
        Assert.False(Hotkey.Same("", ""));
    }

    [Fact]
    public void ForItem()
    {
        Assert.Equal("Win+Alt+3", Hotkey.ForItem("Win+Alt", 3));
        Assert.Equal("", Hotkey.ForItem("", 3));
    }

    [Fact]
    public void Padroes_ToggleLigadoBuscaEItensDesligados()
    {
        var a = new Appearance();
        Assert.Equal(Hotkey.DefaultToggle, a.ToggleShortcut);
        Assert.NotNull(Hotkey.Parse(a.ToggleShortcut));
        Assert.Equal("", a.SearchShortcut);
        Assert.Equal("", a.ItemShortcutModifiers);
    }

    [Fact]
    public void Normalize_ConsertaAtalhosInvalidos()
    {
        var s = new DockSettings
        {
            Appearance = new Appearance { ToggleShortcut = "H", SearchShortcut = "Ctrl+Enter", ItemShortcutModifiers = "Ctrl" },
        };
        SettingsStore.Normalize(s);
        Assert.Equal(Hotkey.DefaultToggle, s.Appearance.ToggleShortcut);
        Assert.Equal("", s.Appearance.SearchShortcut);
        Assert.Equal("", s.Appearance.ItemShortcutModifiers);

        s.Appearance.ToggleShortcut = "";
        s.Appearance.ItemShortcutModifiers = "Win+Alt";
        SettingsStore.Normalize(s);
        Assert.Equal("", s.Appearance.ToggleShortcut); // desligado continua desligado
        Assert.Equal("Win+Alt", s.Appearance.ItemShortcutModifiers);
    }

    [Fact]
    public void Plano_PadraoTemFocoEMostrarOcultar()
    {
        var plan = ShortcutPlan.Build(new Appearance());
        Assert.Equal(new[] { ShortcutAction.Focus, ShortcutAction.ToggleDock }, plan.Shortcuts.Select(s => s.Action));
        Assert.Empty(plan.Duplicates);
    }

    [Fact]
    public void Plano_ItensDe1a9ComIdsDiferentes()
    {
        var plan = ShortcutPlan.Build(new Appearance { ItemShortcutModifiers = "Win+Alt", SearchShortcut = "Alt+Space" });
        var items = plan.Shortcuts.Where(s => s.Action == ShortcutAction.OpenItem).ToList();
        Assert.Equal(Enumerable.Range(1, 9), items.Select(i => i.Item));
        Assert.Equal("Win+Alt+9", items[^1].Text);
        Assert.Equal(plan.Shortcuts.Count, plan.Shortcuts.Select(s => s.Id).Distinct().Count());
        Assert.Contains(plan.Shortcuts, s => s.Action == ShortcutAction.Search && s.Text == "Alt+Space");
    }

    [Fact]
    public void Plano_AtalhoRepetido_FicaNoPrimeiroEAvisa()
    {
        var a = new Appearance { KeyboardShortcut = "Win+Shift+D", ToggleShortcut = "shift+win+d", SearchShortcut = "Win+Alt+2", ItemShortcutModifiers = "Win+Alt" };
        var plan = ShortcutPlan.Build(a);
        Assert.Single(plan.Shortcuts, s => s.Mods == (Hotkey.ModWin | Hotkey.ModShift) && s.Vk == 'D');
        Assert.DoesNotContain(plan.Shortcuts, s => s.Action == ShortcutAction.ToggleDock);
        Assert.DoesNotContain(plan.Shortcuts, s => s.Action == ShortcutAction.OpenItem && s.Item == 2);
        Assert.Equal(new[] { "shift+win+d", "Win+Alt+2" }, plan.Duplicates);
    }

    [Fact]
    public void Plano_AssinaturaMudaQuandoAtalhoMuda()
    {
        var a = new Appearance();
        string before = ShortcutPlan.Build(a).Signature;
        Assert.Equal(before, ShortcutPlan.Build(new Appearance()).Signature);
        a.ToggleShortcut = "Ctrl+Alt+Shift+J";
        Assert.NotEqual(before, ShortcutPlan.Build(a).Signature);
    }
}

public class SearchRankerTests
{
    [Theory]
    [InlineData("calc", "Calculadora", 800)]
    [InlineData("Calculadora", "calculadora", 1000)]
    [InlineData("studio", "Visual Studio Code", 600)]
    [InlineData("vsc", "Visual Studio Code", 500)]
    [InlineData("dio", "Visual Studio Code", 400)]
    [InlineData("vscd", "Visual Studio Code", 100)]
    [InlineData("xyz", "Visual Studio Code", 0)]
    [InlineData("", "Calculadora", 0)]
    [InlineData("config", "Configurações", 800)]
    [InlineData("configuracoes", "Configurações", 1000)]
    [InlineData("CONFIGURAÇÕES", "configuracoes", 1000)]
    public void Score(string q, string name, int expected) => Assert.Equal(expected, SearchRanker.Score(q, name));

    [Fact]
    public void Rank_OrdenaPorPontosDepoisTipoDepoisTamanho()
    {
        var items = new[]
        {
            new SearchItem("Relatório do Word.docx", @"C:\u\Documents\Relatório do Word.docx", SearchKind.File),
            new SearchItem("Word", @"C:\apps\word.lnk", SearchKind.App),
            new SearchItem("WordPad", @"C:\apps\wordpad.lnk", SearchKind.App),
            new SearchItem("Word", @"C:\Program Files\Office\WINWORD.EXE", SearchKind.Dock),
            new SearchItem("Paint", @"C:\apps\paint.lnk", SearchKind.App),
        };
        var r = SearchRanker.Rank("word", items);
        Assert.Equal(new[] { SearchKind.Dock, SearchKind.App, SearchKind.App, SearchKind.File }, r.Select(x => x.Kind));
        Assert.Equal("WordPad", r[2].Name);
        Assert.DoesNotContain(r, x => x.Name == "Paint");
    }

    [Fact]
    public void Rank_MesmoItemDuasVezes_ApareceUmaVezComoDock()
    {
        var items = new[]
        {
            new SearchItem("Bloco de Notas", @"C:\Windows\notepad.exe", SearchKind.App),
            new SearchItem("Bloco de notas", @"c:\windows\NOTEPAD.EXE", SearchKind.Dock),
        };
        var r = SearchRanker.Rank("bloco", items);
        Assert.Single(r);
        Assert.Equal(SearchKind.Dock, r[0].Kind);
    }

    [Fact]
    public void Rank_LimitaOsResultados()
    {
        var items = Enumerable.Range(0, 50).Select(i => new SearchItem($"Arquivo {i}", $@"C:\f\{i}", SearchKind.File));
        Assert.Equal(SearchRanker.MaxResults, SearchRanker.Rank("arq", items).Count);
    }

    [Theory]
    [InlineData("  Configurações  ", "configuracoes")]
    [InlineData("ÁÉÍÓÚ çã", "aeiou ca")]
    [InlineData("", "")]
    public void Normalize(string text, string expected) => Assert.Equal(expected, SearchRanker.Normalize(text));
}
