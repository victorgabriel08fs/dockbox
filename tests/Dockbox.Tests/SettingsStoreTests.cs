using System.Text.Json;
using Dockbox.Core;

namespace Dockbox.Tests;

/// <summary>
/// SettingsStore e Loc são estáticos, então estes testes não rodam em paralelo
/// e cada um usa uma pasta temporária própria (nunca o %APPDATA% real).
/// </summary>
[Collection(SettingsCollection.Name)]
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dockbox-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        SettingsStore.UseFolder(_dir);
        Loc.Apply("pt-BR"); // mensagens conferidas em português
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string SettingsFile => Path.Combine(_dir, "settings.json");

    private void WriteSettings(string json) => File.WriteAllText(SettingsFile, json);

    // ---------- Primeira execução ----------

    [Fact]
    public void SemArquivo_CriaSettingsComPadroes()
    {
        SettingsStore.Load();

        Assert.True(File.Exists(SettingsFile));
        var s = SettingsStore.Current;
        Assert.Equal(SettingsStore.AllWidgetKinds.Length, s.Widgets.Count);
        Assert.Equal(54, s.Appearance.IconSize);
        Assert.Equal(DockEdge.Bottom, s.Appearance.Edge);
    }

    [Fact]
    public void WidgetsPadrao_RelogioCalendarioClimaLixeiraLigados()
    {
        SettingsStore.Load();
        var on = SettingsStore.Current.Widgets.Where(w => w.Enabled).Select(w => w.Kind).OrderBy(k => k);
        Assert.Equal(new[] { "calendar", "clock", "trash", "weather" }, on);
    }

    // ---------- Ida e volta ----------

    [Fact]
    public void SalvarECarregar_PreservaTudo()
    {
        SettingsStore.Load();
        var s = SettingsStore.Current;
        s.Items.Clear();
        s.Items.Add(new DockEntry { Name = "Meu App", Path = @"C:\Apps\meu.exe", Arguments = "--x \"y z\"" });
        s.Appearance.Edge = DockEdge.Left;
        s.Appearance.AccentColor = "#FF375F";
        s.WeatherCity = "São Paulo";
        s.Clock24h = false;
        SettingsStore.Commit();

        SettingsStore.Load();
        var r = SettingsStore.Current;
        var item = Assert.Single(r.Items);
        Assert.Equal("Meu App", item.Name);
        Assert.Equal("--x \"y z\"", item.Arguments);
        Assert.Equal(DockEdge.Left, r.Appearance.Edge);
        Assert.Equal("#FF375F", r.Appearance.AccentColor);
        Assert.Equal("São Paulo", r.WeatherCity);
        Assert.False(r.Clock24h);
    }

    [Fact]
    public void EnumsSaoGravadosComoTexto()
    {
        SettingsStore.Load();
        SettingsStore.Current.Appearance.Edge = DockEdge.Right;
        SettingsStore.Commit();
        Assert.Contains("\"Right\"", File.ReadAllText(SettingsFile));
    }

    [Fact]
    public void Commit_DisparaEventoChanged()
    {
        SettingsStore.Load();
        int calls = 0;
        void Handler() => calls++;
        SettingsStore.Changed += Handler;
        try { SettingsStore.Commit(); }
        finally { SettingsStore.Changed -= Handler; }
        Assert.Equal(1, calls);
    }

    // ---------- Arquivo corrompido (bug do MVP) ----------

    [Fact]
    public void JsonInvalido_GuardaCopiaComOConteudoOriginal()
    {
        const string broken = "{ \"Items\": [ { \"Name\": \"Meu App\", \"Path\": \"C:/x.exe\" }, ] x";
        WriteSettings(broken);

        SettingsStore.Load();

        var backup = Assert.Single(Directory.GetFiles(_dir, "settings.json.corrompido-*"));
        Assert.Equal(broken, File.ReadAllText(backup));
    }

    [Fact]
    public void JsonInvalido_DuasVezes_NaoApagaACopiaAnterior()
    {
        WriteSettings("{ quebrado");
        SettingsStore.Load();
        var first = Directory.GetFiles(_dir, "settings.json.corrompido-*").Single();

        // Usuário erra de novo no mesmo segundo: a primeira cópia não pode ser perdida.
        WriteSettings("{ quebrado de novo");
        SettingsStore.Load();

        Assert.True(File.Exists(first));
        Assert.Equal("{ quebrado", File.ReadAllText(first));
    }

    [Fact]
    public void TipoErradoNumCampo_TambemContaComoCorrompido()
    {
        WriteSettings("{ \"Appearance\": { \"IconSize\": \"grande\" } }");
        SettingsStore.Load();
        Assert.Single(Directory.GetFiles(_dir, "settings.json.corrompido-*"));
        Assert.Equal(54, SettingsStore.Current.Appearance.IconSize);
    }

    [Fact]
    public void RaizNull_VoltaAosPadroesSemExcecao()
    {
        WriteSettings("null");
        SettingsStore.Load();
        Assert.NotNull(SettingsStore.Current.Appearance);
    }

    // ---------- Escrita atômica ----------

    [Fact]
    public void SegundoSave_GeraBakComAVersaoAnterior()
    {
        SettingsStore.Load();
        SettingsStore.Current.WeatherCity = "Recife";
        SettingsStore.Commit();
        SettingsStore.Current.WeatherCity = "Natal";
        SettingsStore.Commit();

        string bak = SettingsFile + ".bak";
        Assert.True(File.Exists(bak));
        Assert.Contains("Recife", File.ReadAllText(bak));
        Assert.Contains("Natal", File.ReadAllText(SettingsFile));
        Assert.False(File.Exists(SettingsFile + ".tmp"));
    }

    [Fact]
    public void ArquivoSalvo_EJsonValido()
    {
        SettingsStore.Load();
        SettingsStore.Commit();
        using var doc = JsonDocument.Parse(File.ReadAllText(SettingsFile));
        Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
    }

    // ---------- Migração do pinned.json do MVP ----------

    [Fact]
    public void PinnedJsonAntigo_EImportado()
    {
        File.WriteAllText(Path.Combine(_dir, "pinned.json"),
            "[ { \"Name\": \"Antigo\", \"Path\": \"C:\\\\antigo.exe\" } ]");

        SettingsStore.Load();

        var item = Assert.Single(SettingsStore.Current.Items);
        Assert.Equal("Antigo", item.Name);
    }

    [Fact]
    public void PinnedJsonAntigoInvalido_UsaPadroesSemExcecao()
    {
        File.WriteAllText(Path.Combine(_dir, "pinned.json"), "[ ruim");
        SettingsStore.Load();
        Assert.NotNull(SettingsStore.Current.Items);
    }

    [Fact]
    public void SettingsJsonExistente_TemPrioridadeSobrePinnedJson()
    {
        WriteSettings("{ \"Items\": [ { \"Name\": \"Novo\", \"Path\": \"C:\\\\novo.exe\" } ] }");
        File.WriteAllText(Path.Combine(_dir, "pinned.json"), "[ { \"Name\": \"Antigo\", \"Path\": \"C:\\\\antigo.exe\" } ]");

        SettingsStore.Load();

        Assert.Equal("Novo", Assert.Single(SettingsStore.Current.Items).Name);
    }

    [Fact]
    public void SettingsAntigoSemReserveSpace_ReservaEspacoPorPadrao()
    {
        WriteSettings("""
        { "Appearance": { "IconSize": 48 } }
        """);
        SettingsStore.Load();
        Assert.True(SettingsStore.Current.Appearance.ReserveSpace);
        Assert.False(SettingsStore.Current.Appearance.AutoHide);
        Assert.True(SettingsStore.Current.Appearance.ShowRunningUnpinned);
        Assert.Equal(0.3, SettingsStore.Current.Appearance.AutoHideDelay);
    }

    [Fact]
    public void AtrasoDoAutoOcultar_ELimitado()
    {
        WriteSettings("""
        { "Appearance": { "AutoHide": true, "AutoHideDelay": 30 } }
        """);
        SettingsStore.Load();
        Assert.True(SettingsStore.Current.Appearance.AutoHide);
        Assert.Equal(2, SettingsStore.Current.Appearance.AutoHideDelay);
    }

    // ---------- Falha ao salvar ----------

    [Fact]
    public void ArquivoSomenteLeitura_AvisaUmaVezEGuardaOErro()
    {
        SettingsStore.Load();
        File.SetAttributes(SettingsFile, FileAttributes.ReadOnly);
        var messages = new List<string>();
        void OnFail(string m) => messages.Add(m);
        SettingsStore.SaveFailed += OnFail;
        try
        {
            SettingsStore.Current.Clock24h = !SettingsStore.Current.Clock24h;
            Assert.False(SettingsStore.Save());
            Assert.False(SettingsStore.Save()); // segunda falha seguida não avisa de novo
            Assert.Single(messages);
            Assert.Contains("somente leitura", messages[0]);
            Assert.Contains(SettingsFile, messages[0]);
            Assert.Equal(messages[0], SettingsStore.LastSaveError);

            // A mudança continua em memória e é gravada quando o arquivo volta a aceitar escrita.
            bool wanted = SettingsStore.Current.Clock24h;
            File.SetAttributes(SettingsFile, FileAttributes.Normal);
            Assert.True(SettingsStore.Save());
            Assert.Null(SettingsStore.LastSaveError);
            Assert.Contains($"\"Clock24h\": {wanted.ToString().ToLowerInvariant()}", File.ReadAllText(SettingsFile));
        }
        finally
        {
            SettingsStore.SaveFailed -= OnFail;
            File.SetAttributes(SettingsFile, FileAttributes.Normal);
        }
    }

    // ---------- Normalização de valores editados à mão ----------

    [Fact]
    public void ValoresForaDaFaixa_SaoLimitados()
    {
        WriteSettings("""
        { "Appearance": { "IconSize": 500, "Magnification": 0, "Opacity": 7, "CornerRadius": -4 } }
        """);
        SettingsStore.Load();
        var a = SettingsStore.Current.Appearance;
        Assert.Equal(96, a.IconSize);
        Assert.Equal(1.0, a.Magnification);
        Assert.Equal(1.0, a.Opacity);
        Assert.Equal(0, a.CornerRadius);
    }

    [Fact]
    public void CoresInvalidas_VoltamAoPadrao()
    {
        WriteSettings("""{ "Appearance": { "CustomColor": "roxo-ish", "AccentColor": "" } }""");
        SettingsStore.Load();
        Assert.Equal("#2A2D3A", SettingsStore.Current.Appearance.CustomColor);
        Assert.Equal("#0A84FF", SettingsStore.Current.Appearance.AccentColor);
    }

    [Fact]
    public void ItensSemCaminhoOuNulos_SaoRemovidos()
    {
        WriteSettings("""
        { "Items": [ null, { "Name": "Vazio", "Path": "  " }, { "Name": "Sem path" }, { "Name": "Ok", "Path": "C:\\ok.exe" } ] }
        """);
        SettingsStore.Load();
        Assert.Equal("Ok", Assert.Single(SettingsStore.Current.Items).Name);
    }

    [Fact]
    public void WidgetsDesconhecidosEDuplicados_SaoLimpos()
    {
        WriteSettings("""
        { "Widgets": [ { "Kind": "clock", "Enabled": false }, { "Kind": "clock", "Enabled": true },
                       { "Kind": "minerador", "Enabled": true }, null ] }
        """);
        SettingsStore.Load();
        var w = SettingsStore.Current.Widgets;
        Assert.Equal(SettingsStore.AllWidgetKinds.OrderBy(k => k), w.Select(x => x.Kind).OrderBy(k => k));
        Assert.False(w.Single(x => x.Kind == "clock").Enabled); // a primeira ocorrência vence
    }

    [Fact]
    public void OrdemDosWidgets_EPreservada()
    {
        WriteSettings("""{ "Widgets": [ { "Kind": "trash" }, { "Kind": "clock" } ] }""");
        SettingsStore.Load();
        var kinds = SettingsStore.Current.Widgets.Select(w => w.Kind).ToList();
        Assert.Equal("trash", kinds[0]);
        Assert.Equal("clock", kinds[1]);
    }

    [Fact]
    public void SecoesNulas_SaoRecriadas()
    {
        WriteSettings("""{ "Items": null, "Widgets": null, "Appearance": null, "WeatherCity": null }""");
        SettingsStore.Load();
        var s = SettingsStore.Current;
        Assert.NotNull(s.Items);
        Assert.NotNull(s.Appearance);
        Assert.Equal("", s.WeatherCity);
        Assert.Equal(SettingsStore.AllWidgetKinds.Length, s.Widgets.Count);
    }

    [Fact]
    public void PropriedadesSemDiferenciarMaiusculas()
    {
        WriteSettings("""{ "appearance": { "edge": "top", "iconsize": 40 } }""");
        SettingsStore.Load();
        Assert.Equal(DockEdge.Top, SettingsStore.Current.Appearance.Edge);
        Assert.Equal(40, SettingsStore.Current.Appearance.IconSize);
    }

    /// <summary>
    /// Achado da revisão (corrigido): o conversor de enum aceitava números, então "Edge": 99
    /// deixava o Dock sem posição. Agora o valor nunca pode sair da lista conhecida.
    /// </summary>
    [Fact]
    public void BordaNumericaInvalida_NuncaFicaForaDaLista()
    {
        WriteSettings("""{ "Appearance": { "Edge": 99, "Theme": 42 } }""");
        SettingsStore.Load();
        Assert.True(Enum.IsDefined(SettingsStore.Current.Appearance.Edge));
        Assert.True(Enum.IsDefined(SettingsStore.Current.Appearance.Theme));
    }

    [Fact]
    public void BordaComNomeDesconhecido_ContaComoCorrompido()
    {
        WriteSettings("""{ "Appearance": { "Edge": "Diagonal" } }""");
        SettingsStore.Load();
        Assert.Equal(DockEdge.Bottom, SettingsStore.Current.Appearance.Edge);
        Assert.Single(Directory.GetFiles(_dir, "settings.json.corrompido-*"));
    }

    [Fact]
    public void ArquivoCorrompido_GeraAvisoComOLocalDaCopia()
    {
        WriteSettings("{ quebrado");
        SettingsStore.Load();
        var copy = Directory.GetFiles(_dir, "settings.json.corrompido-*").Single();
        Assert.NotNull(SettingsStore.LoadWarning);
        Assert.Contains(copy, SettingsStore.LoadWarning);
    }

    /// <summary>Privacidade: sem opt-in, o widget de clima não manda o IP para o ipwho.is.</summary>
    [Fact]
    public void LocalizacaoPorIp_DesligadaPorPadrao()
    {
        SettingsStore.Load();
        Assert.False(SettingsStore.Current.WeatherAutoLocate);
        Assert.Equal("", SettingsStore.Current.WeatherCity);
    }

    [Fact]
    public void ArquivoGrandeComMuitosItens_Carrega()
    {
        var items = Enumerable.Range(0, 2000).Select(i => new DockEntry { Name = $"App {i}", Path = $@"C:\apps\{i}.exe" });
        WriteSettings(JsonSerializer.Serialize(new { Items = items }));
        SettingsStore.Load();
        Assert.Equal(2000, SettingsStore.Current.Items.Count);
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SettingsCollection
{
    public const string Name = "SettingsStore";
}
