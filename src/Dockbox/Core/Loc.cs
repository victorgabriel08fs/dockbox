using System.Globalization;
using System.Resources;

namespace Dockbox.Core;

/// <summary>
/// Textos da interface em português, inglês e espanhol (Strings.resx = inglês, Strings.pt.resx, Strings.es.resx;
/// gerados por tools/i18n/build_resx.py).
/// O idioma vem das configurações; vazio = idioma do Windows (pt, en ou es; outros caem no inglês).
/// </summary>
public static class Loc
{
    private static readonly ResourceManager Rm = new("Dockbox.Strings", typeof(Loc).Assembly);

    /// <summary>Idiomas com tradução, na ordem mostrada nas configurações.</summary>
    public static readonly (string Code, string NativeName)[] Languages =
    {
        ("pt-BR", "Português"),
        ("en", "English"),
        ("es", "Español"),
    };

    /// <summary>Cultura dos textos e também de datas e números.</summary>
    public static CultureInfo Culture { get; private set; } = Resolve("", CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);

    /// <summary>Disparado depois de trocar o idioma.</summary>
    public static event Action? Changed;

    /// <summary>
    /// Escolhe a cultura: <paramref name="setting"/> ("pt-BR", "en", "es" ou vazio = automático).
    /// No automático, usa o idioma do Windows; se for pt/en/es, mantém o país do Windows para datas
    /// (ex.: es-MX), senão inglês.
    /// </summary>
    public static CultureInfo Resolve(string? setting, CultureInfo systemUi, CultureInfo systemFormat)
    {
        string? lang = Normalize(setting);
        if (lang == null)
        {
            lang = Normalize(systemUi.TwoLetterISOLanguageName) ?? "en";
        }
        string two = lang[..2];
        // Datas e números no formato do país do Windows quando o idioma é o mesmo.
        if (systemFormat.TwoLetterISOLanguageName == two && !systemFormat.IsNeutralCulture) return systemFormat;
        if (systemUi.TwoLetterISOLanguageName == two && !systemUi.IsNeutralCulture) return systemUi;
        return CultureInfo.GetCultureInfo(two switch { "pt" => "pt-BR", "es" => "es-ES", _ => "en-US" });
    }

    /// <summary>"pt", "pt-PT", "PT-br" → "pt-BR"; "es-MX" → "es"; desconhecido ou vazio → null.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string two = code.Trim().Split('-', '_')[0].ToLowerInvariant();
        return two switch { "pt" => "pt-BR", "en" => "en", "es" => "es", _ => null };
    }

    /// <summary>Última opção aplicada ("" = automático); null antes do primeiro <see cref="Apply"/>.</summary>
    public static string? Setting { get; private set; }

    /// <summary>Troca o idioma do app (texto, datas e números) e avisa quem mostra textos.</summary>
    public static void Apply(string? setting)
    {
        Setting = Normalize(setting) ?? "";
        Culture = Resolve(setting, SystemUi, SystemFormat);
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
        Changed?.Invoke();
    }

    // Cultura do Windows guardada na primeira chamada, antes de o app trocar a da thread.
    private static readonly CultureInfo SystemUi = CultureInfo.CurrentUICulture;
    private static readonly CultureInfo SystemFormat = CultureInfo.CurrentCulture;

    /// <summary>Idioma que o automático escolheria agora (nome nativo, ex.: "Português").</summary>
    public static string AutoName =>
        Languages.First(l => l.Code == (Normalize(Resolve("", SystemUi, SystemFormat).Name) ?? "en")).NativeName;

    /// <summary>Primeira letra maiúscula, na cultura atual ("quarta-feira" → "Quarta-feira").</summary>
    public static string Capitalize(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpper(text[0], Culture) + text[1..];

    /// <summary>Texto da chave no idioma atual (a própria chave se faltar).</summary>
    public static string T(string key) => T(key, Culture);

    public static string T(string key, CultureInfo culture) => Rm.GetString(key, culture) ?? key;

    /// <summary>Texto com valores ({0}, {1}...), formatados na cultura atual.</summary>
    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);
}
