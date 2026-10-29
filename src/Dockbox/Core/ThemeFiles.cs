using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dockbox.Core;

/// <summary>Arquivo de tema ou backup inválido. A mensagem já é a que o usuário vê.</summary>
public sealed class ThemeFileException : Exception
{
    public ThemeFileException(string message) : base(message) { }
}

/// <summary>Só a aparência: cores, opacidade, desfoque, cantos, tamanho e ampliação.</summary>
public sealed class ThemeData
{
    public string Name { get; set; } = "";
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;
    public string CustomColor { get; set; } = "#2A2D3A";
    public string AccentColor { get; set; } = "#0A84FF";
    public double Opacity { get; set; } = 0.72;
    public bool Blur { get; set; } = true;
    public double CornerRadius { get; set; } = 18;
    public double IconSize { get; set; } = 54;
    public bool Magnify { get; set; } = true;
    public double Magnification { get; set; } = 1.8;

    public static ThemeData From(Appearance a, string name) => new()
    {
        Name = name, Theme = a.Theme, CustomColor = a.CustomColor, AccentColor = a.AccentColor, Opacity = a.Opacity,
        Blur = a.Blur, CornerRadius = a.CornerRadius, IconSize = a.IconSize, Magnify = a.Magnify, Magnification = a.Magnification,
    };

    /// <summary>
    /// Aplica na aparência; o resto (posição, monitor, comportamento) fica como está. Sem
    /// <paramref name="includeSize"/>, tamanho e ampliação também ficam (é o que os temas prontos fazem).
    /// </summary>
    public void ApplyTo(Appearance a, bool includeSize = true)
    {
        a.Theme = Theme;
        a.CustomColor = CustomColor;
        a.AccentColor = AccentColor;
        a.Opacity = Opacity;
        a.Blur = Blur;
        a.CornerRadius = CornerRadius;
        if (!includeSize) return;
        a.IconSize = IconSize;
        a.Magnify = Magnify;
        a.Magnification = Magnification;
    }
}

/// <summary>
/// Exportar e importar temas (.dockbox-theme) e backups completos (.dockbox-backup), em JSON.
/// Tudo é validado antes de aplicar: um arquivo inválido não muda nada.
/// </summary>
public static class ThemeFiles
{
    public const string ThemeExtension = ".dockbox-theme";
    public const string BackupExtension = ".dockbox-backup";
    private const string ThemeFormat = "dockbox-theme";
    private const string BackupFormat = "dockbox-backup";
    private const int Version = 1;
    /// <summary>Arquivos maiores que isso não são tema nem backup (evita travar lendo algo enorme).</summary>
    public const long MaxFileSize = 20 * 1024 * 1024;

    // ================= Temas prontos =================

    /// <summary>Temas que vêm com o Dockbox. Os nomes são traduzidos na hora de mostrar.</summary>
    public static IReadOnlyList<(string Key, ThemeData Theme)> BuiltIn { get; } = new List<(string, ThemeData)>
    {
        ("Theme_SonomaLight", new ThemeData { Theme = ThemeMode.Light, AccentColor = "#0A84FF", Opacity = 0.72, Blur = true, CornerRadius = 18 }),
        ("Theme_SonomaDark", new ThemeData { Theme = ThemeMode.Dark, AccentColor = "#0A84FF", Opacity = 0.72, Blur = true, CornerRadius = 18 }),
        ("Theme_Graphite", new ThemeData { Theme = ThemeMode.Custom, CustomColor = "#3A3A3C", AccentColor = "#8E8E93", Opacity = 0.85, Blur = true, CornerRadius = 14 }),
        ("Theme_Transparent", new ThemeData { Theme = ThemeMode.Auto, AccentColor = "#FFFFFF", Opacity = 0.12, Blur = false, CornerRadius = 20 }),
    };

    // ================= Tema =================

    public static string ExportTheme(Appearance a, string name)
    {
        var data = ThemeData.From(a, name);
        var root = new JsonObject
        {
            ["format"] = ThemeFormat,
            ["version"] = Version,
            ["theme"] = JsonSerializer.SerializeToNode(data, SettingsStore.JsonOptions),
        };
        return root.ToJsonString(SettingsStore.JsonOptions);
    }

    /// <summary>Lê um tema. Valores fora do intervalo são ajustados; formato errado lança <see cref="ThemeFileException"/>.</summary>
    public static ThemeData ParseTheme(string json)
    {
        var root = ParseRoot(json);
        if (Format(root) == BackupFormat) throw new ThemeFileException(Loc.T("ThemeFile_IsBackup"));
        CheckFormat(root, ThemeFormat);
        ThemeData? data;
        try { data = root["theme"]?.Deserialize<ThemeData>(SettingsStore.JsonOptions); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new ThemeFileException(Loc.T("ThemeFile_BadValues"));
        }
        if (data == null) throw new ThemeFileException(Loc.T("ThemeFile_BadValues"));

        // Mesmos limites das configurações.
        var check = new DockSettings();
        data.ApplyTo(check.Appearance);
        SettingsStore.Normalize(check);
        var clean = ThemeData.From(check.Appearance, (data.Name ?? "").Trim());
        if (clean.Name.Length > 80) clean.Name = clean.Name[..80];
        return clean;
    }

    // ================= Backup completo =================

    /// <summary>Tudo: itens, widgets, aparência e opções, com os ícones personalizados embutidos.</summary>
    public static string ExportBackup(DockSettings s, string iconsFolder)
    {
        var icons = new JsonObject();
        foreach (var name in s.Items.Select(i => i.Icon).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string path = Path.Combine(iconsFolder, Path.GetFileName(name!));
            try { if (File.Exists(path)) icons[Path.GetFileName(name!)] = Convert.ToBase64String(File.ReadAllBytes(path)); }
            catch { }
        }
        var root = new JsonObject
        {
            ["format"] = BackupFormat,
            ["version"] = Version,
            ["settings"] = JsonSerializer.SerializeToNode(s, SettingsStore.JsonOptions),
            ["icons"] = icons,
        };
        return root.ToJsonString(SettingsStore.JsonOptions);
    }

    /// <summary>Backup lido e validado, pronto para aplicar.</summary>
    public sealed record Backup(DockSettings Settings, IReadOnlyDictionary<string, byte[]> Icons);

    /// <summary>
    /// Lê um backup. Itens perdem a confirmação de segurança já dada ("Trusted"): scripts e protocolos voltam
    /// a perguntar antes de abrir, como num item novo.
    /// </summary>
    public static Backup ParseBackup(string json)
    {
        var root = ParseRoot(json);
        if (Format(root) == ThemeFormat) throw new ThemeFileException(Loc.T("ThemeFile_IsTheme"));
        CheckFormat(root, BackupFormat);
        DockSettings? s;
        try { s = root["settings"]?.Deserialize<DockSettings>(SettingsStore.JsonOptions); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            throw new ThemeFileException(Loc.T("ThemeFile_BadValues"));
        }
        if (s == null) throw new ThemeFileException(Loc.T("ThemeFile_BadValues"));
        SettingsStore.Normalize(s);
        foreach (var e in s.Items) e.Trusted = false;

        var icons = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (root["icons"] is JsonObject obj)
        {
            foreach (var (name, value) in obj)
            {
                string safe = Path.GetFileName(name);
                if (safe.Length == 0 || !safe.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                try { if (value?.GetValue<string>() is string b64) icons[safe] = Convert.FromBase64String(b64); }
                catch { /* ícone corrompido: o item fica com o ícone do app */ }
            }
        }
        // Ícone citado mas não incluído: volta ao do app.
        foreach (var e in s.Items)
            if (e.Icon != null && !icons.ContainsKey(Path.GetFileName(e.Icon))) e.Icon = null;
        return new Backup(s, icons);
    }

    /// <summary>Troca tudo em <paramref name="target"/> pelo que veio no backup.</summary>
    public static void ApplyBackup(DockSettings target, Backup b)
    {
        var s = b.Settings;
        target.Items = s.Items;
        target.Widgets = s.Widgets;
        target.Appearance = s.Appearance;
        target.Language = s.Language;
        target.WeatherCity = s.WeatherCity;
        target.Clock24h = s.Clock24h;
        target.WeatherAutoLocate = s.WeatherAutoLocate;
    }

    /// <summary>Grava os ícones do backup na pasta de ícones (sem apagar os que já existem).</summary>
    public static void WriteIcons(Backup b, string iconsFolder)
    {
        if (b.Icons.Count == 0) return;
        Directory.CreateDirectory(iconsFolder);
        foreach (var (name, bytes) in b.Icons) File.WriteAllBytes(Path.Combine(iconsFolder, name), bytes);
    }

    // ================= Comum =================

    /// <summary>Lê o arquivo com limite de tamanho; erros de leitura viram mensagem clara.</summary>
    public static string ReadFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxFileSize) throw new ThemeFileException(Loc.T("ThemeFile_TooBig"));
            return File.ReadAllText(path);
        }
        catch (ThemeFileException) { throw; }
        catch (Exception ex) { throw new ThemeFileException(Loc.F("ThemeFile_CantRead", ex.Message)); }
    }

    private static JsonObject ParseRoot(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is JsonObject o) return o;
        }
        catch (JsonException) { }
        throw new ThemeFileException(Loc.T("ThemeFile_NotJson"));
    }

    private static string? Format(JsonObject root)
    {
        try { return root["format"]?.GetValue<string>(); }
        catch { return null; }
    }

    private static void CheckFormat(JsonObject root, string expected)
    {
        if (Format(root) != expected) throw new ThemeFileException(Loc.T("ThemeFile_WrongFormat"));
        int version;
        try { version = root["version"]?.GetValue<int>() ?? 0; }
        catch { version = 0; }
        if (version < 1) throw new ThemeFileException(Loc.T("ThemeFile_WrongFormat"));
        if (version > Version) throw new ThemeFileException(Loc.T("ThemeFile_Newer"));
    }
}
