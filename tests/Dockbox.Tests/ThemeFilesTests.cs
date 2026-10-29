using System.Text.Json.Nodes;
using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class ThemeFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dockbox-themes-" + Guid.NewGuid().ToString("N"));

    public ThemeFilesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static Appearance Styled() => new()
    {
        Theme = ThemeMode.Custom, CustomColor = "#112233", AccentColor = "#FF8800", Opacity = 0.4, Blur = false,
        CornerRadius = 7, IconSize = 70, Magnify = false, Magnification = 2.2,
        Edge = DockEdge.Left, AutoHide = true,
    };

    // ================= Tema =================

    [Fact]
    public void Tema_IdaEVolta_ReproduzAMesmaAparencia()
    {
        var json = ThemeFiles.ExportTheme(Styled(), "Meu tema");
        var t = ThemeFiles.ParseTheme(json);
        var a = new Appearance();
        t.ApplyTo(a);

        Assert.Equal("Meu tema", t.Name);
        Assert.Equal(ThemeMode.Custom, a.Theme);
        Assert.Equal("#112233", a.CustomColor);
        Assert.Equal("#FF8800", a.AccentColor);
        Assert.Equal(0.4, a.Opacity);
        Assert.False(a.Blur);
        Assert.Equal(7, a.CornerRadius);
        Assert.Equal(70, a.IconSize);
        Assert.False(a.Magnify);
        Assert.Equal(2.2, a.Magnification);
    }

    [Fact]
    public void Tema_NaoMexeEmPosicaoNemComportamento()
    {
        var t = ThemeFiles.ParseTheme(ThemeFiles.ExportTheme(Styled(), "x"));
        var a = new Appearance { Edge = DockEdge.Top, AutoHide = false, ShowLabels = false };
        t.ApplyTo(a);
        Assert.Equal(DockEdge.Top, a.Edge);
        Assert.False(a.AutoHide);
        Assert.False(a.ShowLabels);
    }

    [Fact]
    public void Tema_GravaEnumsPorNome()
    {
        var json = ThemeFiles.ExportTheme(Styled(), "x");
        Assert.Contains("\"Custom\"", json);
    }

    [Fact]
    public void Tema_ValoresForaDoIntervalo_SaoAjustados()
    {
        const string json = """
        { "format": "dockbox-theme", "version": 1,
          "theme": { "name": "  Exagerado  ", "theme": "Dark", "opacity": 9, "cornerRadius": -5, "iconSize": 500,
                     "magnification": 99, "customColor": "azul", "accentColor": "#12" } }
        """;
        var t = ThemeFiles.ParseTheme(json);
        Assert.Equal("Exagerado", t.Name);
        Assert.Equal(ThemeMode.Dark, t.Theme);
        Assert.Equal(1.0, t.Opacity);
        Assert.Equal(0, t.CornerRadius);
        Assert.Equal(96, t.IconSize);
        Assert.Equal(2.6, t.Magnification);
        Assert.Equal("#2A2D3A", t.CustomColor);
        Assert.Equal("#0A84FF", t.AccentColor);
    }

    [Fact]
    public void Tema_CamposQueFaltam_FicamNoPadrao()
    {
        var t = ThemeFiles.ParseTheme("""{ "format": "dockbox-theme", "version": 1, "theme": { "theme": "Light" } }""");
        Assert.Equal(ThemeMode.Light, t.Theme);
        Assert.Equal(new Appearance().Opacity, t.Opacity);
        Assert.Equal("", t.Name);
    }

    [Fact]
    public void Tema_NomeMuitoLongo_ECortado()
    {
        var t = ThemeFiles.ParseTheme(ThemeFiles.ExportTheme(new Appearance(), new string('a', 300)));
        Assert.Equal(80, t.Name.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"format\": \"outra-coisa\", \"version\": 1 }")]
    [InlineData("{ \"format\": \"dockbox-theme\" }")]
    [InlineData("{ \"format\": \"dockbox-theme\", \"version\": \"um\" }")]
    [InlineData("{ \"format\": 42, \"version\": 1 }")]
    [InlineData("{ \"format\": \"dockbox-theme\", \"version\": 1 }")]
    [InlineData("{ \"format\": \"dockbox-theme\", \"version\": 1, \"theme\": { \"theme\": \"Roxo\" } }")]
    [InlineData("{ \"format\": \"dockbox-theme\", \"version\": 1, \"theme\": { \"theme\": 2 } }")]
    [InlineData("{ \"format\": \"dockbox-theme\", \"version\": 1, \"theme\": { \"opacity\": \"muito\" } }")]
    [InlineData("{ \"format\": \"dockbox-theme\", \"version\": 1, \"theme\": [] }")]
    public void Tema_Invalido_DaErroClaro(string json)
    {
        var ex = Assert.Throws<ThemeFileException>(() => ThemeFiles.ParseTheme(json));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Tema_DeVersaoMaisNova_AvisaParaAtualizar()
    {
        var ex = Assert.Throws<ThemeFileException>(() =>
            ThemeFiles.ParseTheme("""{ "format": "dockbox-theme", "version": 2, "theme": {} }"""));
        Assert.Equal(Loc.T("ThemeFile_Newer"), ex.Message);
    }

    [Fact]
    public void Tema_RecebendoBackup_DizQueEBackup()
    {
        var backup = ThemeFiles.ExportBackup(new DockSettings(), _dir);
        var ex = Assert.Throws<ThemeFileException>(() => ThemeFiles.ParseTheme(backup));
        Assert.Equal(Loc.T("ThemeFile_IsBackup"), ex.Message);
    }

    [Fact]
    public void TemasProntos_TemNomesTraduzidosEValoresValidos()
    {
        Assert.Equal(4, ThemeFiles.BuiltIn.Count);
        foreach (var (key, theme) in ThemeFiles.BuiltIn)
        {
            Assert.NotEqual(key, Loc.T(key)); // existe tradução
            var round = ThemeFiles.ParseTheme(ThemeFiles.ExportTheme(ApplyFresh(theme), key));
            Assert.Equal(theme.Theme, round.Theme);
            Assert.Equal(theme.Opacity, round.Opacity);
            Assert.Equal(theme.AccentColor, round.AccentColor);
        }
    }

    [Fact]
    public void TemaPronto_SemTamanho_MantemTamanhoEAmpliacao()
    {
        var a = new Appearance { IconSize = 80, Magnify = false, Magnification = 1.3 };
        ThemeFiles.BuiltIn[1].Theme.ApplyTo(a, includeSize: false);
        Assert.Equal(ThemeMode.Dark, a.Theme);
        Assert.Equal(80, a.IconSize);
        Assert.False(a.Magnify);
        Assert.Equal(1.3, a.Magnification);
    }

    private static Appearance ApplyFresh(ThemeData t)
    {
        var a = new Appearance();
        t.ApplyTo(a);
        return a;
    }

    // ================= Backup =================

    private DockSettings Full()
    {
        var s = new DockSettings
        {
            Items =
            {
                new DockEntry { Name = "Bloco", Path = @"C:\Windows\notepad.exe", Trusted = true, Icon = "a.png" },
                new DockEntry { Name = "Script", Path = @"C:\x\run.bat", Arguments = "--fast", Trusted = true },
                new DockEntry { Name = "Downloads", Path = @"C:\Users\v\Downloads", StackView = StackView.Grid, StackSort = StackSort.DateAdded, StackShowsLatest = true },
            },
            Appearance = Styled(),
            Language = "en",
            WeatherCity = "Recife",
            Clock24h = false,
            WeatherAutoLocate = true,
        };
        SettingsStore.Normalize(s);
        s.Widgets.First(w => w.Kind == "clock").Enabled = false;
        return s;
    }

    [Fact]
    public void Backup_IdaEVolta_ReproduzOMesmoDock()
    {
        File.WriteAllBytes(Path.Combine(_dir, "a.png"), new byte[] { 1, 2, 3, 4 });
        var original = Full();
        var b = ThemeFiles.ParseBackup(ThemeFiles.ExportBackup(original, _dir));

        var target = new DockSettings();
        ThemeFiles.ApplyBackup(target, b);
        Assert.Equal(3, target.Items.Count);
        Assert.Equal("Bloco", target.Items[0].Name);
        Assert.Equal("a.png", target.Items[0].Icon);
        Assert.Equal("--fast", target.Items[1].Arguments);
        Assert.Equal(StackView.Grid, target.Items[2].StackView);
        Assert.Equal(StackSort.DateAdded, target.Items[2].StackSort);
        Assert.True(target.Items[2].StackShowsLatest);
        Assert.False(target.Widgets.First(w => w.Kind == "clock").Enabled);
        Assert.Equal(DockEdge.Left, target.Appearance.Edge);
        Assert.True(target.Appearance.AutoHide);
        Assert.Equal("#112233", target.Appearance.CustomColor);
        Assert.Equal("en", target.Language);
        Assert.Equal("Recife", target.WeatherCity);
        Assert.False(target.Clock24h);
        Assert.True(target.WeatherAutoLocate);

        // Ícone embutido e gravado na pasta nova (outro PC).
        string other = Path.Combine(_dir, "outro-pc");
        ThemeFiles.WriteIcons(b, other);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(Path.Combine(other, "a.png")));
    }

    [Fact]
    public void Backup_ApplyBackup_CopiaTodosOsCampos()
    {
        // Se alguém criar um campo novo em DockSettings, este teste lembra de levá-lo no backup.
        var b = ThemeFiles.ParseBackup(ThemeFiles.ExportBackup(Full(), _dir));
        var target = new DockSettings();
        ThemeFiles.ApplyBackup(target, b);
        foreach (var p in typeof(DockSettings).GetProperties())
        {
            var expected = p.GetValue(b.Settings);
            var actual = p.GetValue(target);
            if (p.PropertyType.IsValueType || p.PropertyType == typeof(string)) Assert.Equal(expected, actual);
            else Assert.Same(expected, actual);
        }
    }

    [Fact]
    public void Backup_ItensVoltamAPedirConfirmacao()
    {
        var b = ThemeFiles.ParseBackup(ThemeFiles.ExportBackup(Full(), _dir));
        Assert.All(b.Settings.Items, e => Assert.False(e.Trusted));
    }

    [Fact]
    public void Backup_IconeQueNaoVeio_VoltaAoDoApp()
    {
        // a.png não existe na pasta: o arquivo sai sem ele, e o item volta ao ícone do app.
        var b = ThemeFiles.ParseBackup(ThemeFiles.ExportBackup(Full(), _dir));
        Assert.Null(b.Settings.Items[0].Icon);
        Assert.Empty(b.Icons);
    }

    [Fact]
    public void Backup_NomesDeIconePerigosos_SaoIgnoradosOuLimpos()
    {
        var root = JsonNode.Parse(ThemeFiles.ExportBackup(Full(), _dir))!.AsObject();
        root["icons"] = new JsonObject
        {
            [@"..\..\Windows\evil.png"] = Convert.ToBase64String(new byte[] { 9 }),
            ["script.exe"] = Convert.ToBase64String(new byte[] { 9 }),
            ["quebrado.png"] = "%%%não é base64%%%",
        };
        var b = ThemeFiles.ParseBackup(root.ToJsonString());
        Assert.Equal(new[] { "evil.png" }, b.Icons.Keys.ToArray());

        ThemeFiles.WriteIcons(b, Path.Combine(_dir, "icons"));
        Assert.True(File.Exists(Path.Combine(_dir, "icons", "evil.png")));
        Assert.False(File.Exists(Path.Combine(_dir, "script.exe")));
    }

    [Fact]
    public void Backup_ValoresInvalidos_SaoNormalizados()
    {
        var root = JsonNode.Parse(ThemeFiles.ExportBackup(Full(), _dir))!.AsObject();
        var settings = root["settings"]!.AsObject();
        settings["Appearance"]!["IconSize"] = 1000;
        settings["Language"] = "klingon";
        settings["Widgets"] = new JsonArray(new JsonObject { ["Kind"] = "nao-existe" });
        var b = ThemeFiles.ParseBackup(root.ToJsonString());
        Assert.Equal(96, b.Settings.Appearance.IconSize);
        Assert.Equal("", b.Settings.Language);
        Assert.DoesNotContain(b.Settings.Widgets, w => w.Kind == "nao-existe");
        Assert.Equal(SettingsStore.AllWidgetKinds.Length, b.Settings.Widgets.Count);
    }

    [Theory]
    [InlineData("nada")]
    [InlineData("{ \"format\": \"dockbox-backup\", \"version\": 1 }")]
    [InlineData("{ \"format\": \"dockbox-backup\", \"version\": 1, \"settings\": { \"items\": 5 } }")]
    [InlineData("{ \"format\": \"dockbox-backup\", \"version\": 1, \"settings\": { \"appearance\": { \"edge\": \"Meio\" } } }")]
    public void Backup_Invalido_DaErroClaro(string json)
    {
        var ex = Assert.Throws<ThemeFileException>(() => ThemeFiles.ParseBackup(json));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void Backup_RecebendoTema_DizQueETema()
    {
        var ex = Assert.Throws<ThemeFileException>(() => ThemeFiles.ParseBackup(ThemeFiles.ExportTheme(new Appearance(), "x")));
        Assert.Equal(Loc.T("ThemeFile_IsTheme"), ex.Message);
    }

    // ================= Arquivo =================

    [Fact]
    public void ReadFile_ArquivoQueNaoExiste_DaErroClaro()
    {
        var ex = Assert.Throws<ThemeFileException>(() => ThemeFiles.ReadFile(Path.Combine(_dir, "sumiu.dockbox-theme")));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void ReadFile_ArquivoGrandeDemais_Recusa()
    {
        string path = Path.Combine(_dir, "enorme.dockbox-backup");
        using (var f = File.Create(path)) f.SetLength(ThemeFiles.MaxFileSize + 1);
        var ex = Assert.Throws<ThemeFileException>(() => ThemeFiles.ReadFile(path));
        Assert.Equal(Loc.T("ThemeFile_TooBig"), ex.Message);
    }
}
