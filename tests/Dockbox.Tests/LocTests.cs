using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Dockbox.Core;

namespace Dockbox.Tests;

/// <summary>Idiomas: escolha da cultura, textos completos nas três línguas e chaves usadas no código.</summary>
[Collection(SettingsCollection.Name)] // Loc é global
public sealed class LocTests : IDisposable
{
    private static readonly ResourceManager Rm = new("Dockbox.Strings", typeof(Loc).Assembly);

    public void Dispose() => Loc.Apply("pt-BR");

    private static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    private static Dictionary<string, string> Texts(string culture)
    {
        var set = Rm.GetResourceSet(culture == "" ? CultureInfo.InvariantCulture : C(culture), true, false)
                  ?? throw new Xunit.Sdk.XunitException($"Sem textos para '{culture}'");
        return set.Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();

    [Theory]
    [InlineData("pt")]
    [InlineData("es")]
    public void TodasAsChavesExistemNasTresLinguas_ComOsMesmosValores(string culture)
    {
        var en = Texts("");
        var other = Texts(culture);
        Assert.Empty(en.Keys.Except(other.Keys));
        Assert.Empty(other.Keys.Except(en.Keys));
        foreach (var (key, text) in en)
        {
            Assert.False(string.IsNullOrWhiteSpace(other[key]), $"{culture}: {key} vazio");
            Assert.True(Placeholders(text).SequenceEqual(Placeholders(other[key])), $"{culture}: {key} com {{n}} diferentes");
        }
    }

    [Fact]
    public void ChavesUsadasNoCodigoENoXaml_Existem()
    {
        var keys = Texts("").Keys.ToHashSet();
        string src = Path.Combine(RepoRoot(), "src", "Dockbox");
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), "\"([A-Z][A-Za-z0-9]*_[A-Za-z0-9_]+)\""))
                if (!m.Groups[1].Value.StartsWith("Shell_")) used.Add(m.Groups[1].Value); // Shell_TrayWnd: classe de janela
        foreach (var file in Directory.EnumerateFiles(src, "*.xaml", SearchOption.AllDirectories))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{l:T (\w+)\}"))
                used.Add(m.Groups[1].Value);
        Assert.NotEmpty(used);
        Assert.Empty(used.Where(k => !keys.Contains(k)));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dockbox.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Dockbox.sln não encontrado");
    }

    [Theory]
    [InlineData("", "pt-BR", "pt-BR", "pt-BR")]   // automático: Windows em português
    [InlineData("", "pt-PT", "pt-PT", "pt-PT")]   // mantém o país do Windows para datas
    [InlineData("", "es-MX", "es-MX", "es-MX")]
    [InlineData("", "en-GB", "en-GB", "en-GB")]
    [InlineData("", "fr-FR", "fr-FR", "en-US")]   // sem tradução: inglês
    [InlineData("", "en-US", "pt-BR", "en-US")]   // texto em inglês, formato em português: datas em inglês
    [InlineData("es", "en-US", "en-US", "es-ES")] // escolhido nas configurações
    [InlineData("pt-BR", "en-US", "en-US", "pt-BR")]
    [InlineData("en", "pt-BR", "en-GB", "en-GB")]
    public void Resolve(string setting, string ui, string format, string expected) =>
        Assert.Equal(expected, Loc.Resolve(setting, C(ui), C(format)).Name);

    [Theory]
    [InlineData("pt", "pt-BR")]
    [InlineData("PT-br", "pt-BR")]
    [InlineData("pt_PT", "pt-BR")]
    [InlineData("es-MX", "es")]
    [InlineData(" en ", "en")]
    [InlineData("fr", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize(string? code, string? expected) => Assert.Equal(expected, Loc.Normalize(code));

    [Fact]
    public void Apply_TrocaTextosEAvisa()
    {
        int changed = 0;
        void OnChanged() => changed++;
        Loc.Changed += OnChanged;
        try
        {
            Loc.Apply("es");
            Assert.Equal("es", Loc.Setting);
            Assert.Equal("Salir de Dockbox", Loc.T("Menu_Quit"));
            Assert.Equal("Desactivado", Hotkey.Display(""));
            Loc.Apply("en");
            Assert.Equal("Quit Dockbox", Loc.T("Menu_Quit"));
            Assert.Equal("Recycle Bin · 3 items", Loc.F("W_Trash_Many", 3));
            Loc.Apply("pt-BR");
            Assert.Equal("Sair do Dockbox", Loc.T("Menu_Quit"));
            Assert.Equal(3, changed);
        }
        finally { Loc.Changed -= OnChanged; }
    }

    [Fact]
    public void PortuguesDePortugal_UsaOsTextosEmPortugues() =>
        Assert.Equal("Sair do Dockbox", Loc.T("Menu_Quit", C("pt-PT")));

    [Fact]
    public void ChaveDesconhecida_DevolveAPropriaChave() => Assert.Equal("Nao_Existe", Loc.T("Nao_Existe"));

    [Fact]
    public void Capitalize()
    {
        Loc.Apply("pt-BR");
        Assert.Equal("Quarta-feira, 29 de outubro", Loc.Capitalize("quarta-feira, 29 de outubro"));
        Assert.Equal("", Loc.Capitalize(""));
    }

    [Theory]
    [InlineData("PT", "pt-BR")]
    [InlineData("es-AR", "es")]
    [InlineData("fr", "")]
    [InlineData(null, "")]
    public void Configuracao_NormalizaIdioma(string? value, string expected)
    {
        var s = new DockSettings { Language = value! };
        SettingsStore.Normalize(s);
        Assert.Equal(expected, s.Language);
    }
}
