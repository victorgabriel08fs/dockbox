namespace Dockbox.Core;

/// <summary>Atalho global para usar o Dock pelo teclado (ex.: "Win+Shift+D").</summary>
public static class Hotkey
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    // Ctrl+Alt+letra e Ctrl+Alt+Espaço são atalhos da Lupa do Windows; Win+Alt+D mostra data e hora no Windows 11.
    public const string Default = "Win+Shift+D";

    /// <summary>Atalhos oferecidos nas configurações. Vazio = desligado.</summary>
    public static readonly string[] Choices = { "Win+Shift+D", "Ctrl+Alt+Shift+D", "Ctrl+Shift+F12", "" };

    /// <summary>Mostrar/ocultar o Dock (liga e desliga o "Ocultar automaticamente", como Option+Cmd+D no macOS).</summary>
    public const string DefaultToggle = "Ctrl+Alt+Shift+H";

    /// <summary>Modificadores oferecidos para abrir os itens 1 a 9. Vazio = desligado (padrão).</summary>
    public static readonly string[] ItemModifierChoices = { "", "Win+Alt", "Ctrl+Alt+Shift" };

    /// <summary>Atalho do item <paramref name="n"/> (1 a 9) com os modificadores escolhidos.</summary>
    public static string ForItem(string modifiers, int n) => string.IsNullOrEmpty(modifiers) ? "" : $"{modifiers}+{n}";

    /// <summary>Monta o texto ("Win+Ctrl+Alt+Shift+X") a partir do que foi apertado; null se a tecla não serve.</summary>
    public static string? Format(uint mods, uint vk)
    {
        string? key = vk switch
        {
            0x20 => "Space",
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x70 and <= 0x7B => "F" + (vk - 0x70 + 1),
            _ => null,
        };
        if (key == null || mods == 0) return null;
        var parts = new List<string>();
        if ((mods & ModWin) != 0) parts.Add("Win");
        if ((mods & ModControl) != 0) parts.Add("Ctrl");
        if ((mods & ModAlt) != 0) parts.Add("Alt");
        if ((mods & ModShift) != 0) parts.Add("Shift");
        parts.Add(key);
        return string.Join('+', parts);
    }

    /// <summary>Os dois textos são o mesmo atalho (ordem e maiúsculas não importam).</summary>
    public static bool Same(string? a, string? b) => Parse(a) is { } x && Parse(b) is { } y && x == y;

    /// <summary>Texto para mostrar ao usuário.</summary>
    public static string Display(string? text) =>
        string.IsNullOrEmpty(text) ? Loc.T("Common_Off") : text.Replace("Space", Loc.T("Key_Space"));

    /// <summary>Converte "Ctrl+Alt+D" em modificadores e código de tecla virtual do Windows; null se inválido.</summary>
    public static (uint Mods, uint Vk)? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        uint mods = 0;
        uint? vk = null;
        foreach (var raw in text.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModControl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win": mods |= ModWin; break;
                case "space": vk = 0x20; break;
                default:
                    if (part.Length == 1 && char.IsAsciiLetterOrDigit(part[0])) vk = char.ToUpperInvariant(part[0]);
                    else if (part.Length >= 2 && part[0] is 'F' or 'f' && int.TryParse(part.AsSpan(1), out int f) && f is >= 1 and <= 12)
                        vk = (uint)(0x70 + f - 1);
                    else return null;
                    break;
            }
        }
        // Sem modificador, o atalho roubaria a tecla de todos os programas.
        if (vk == null || mods == 0) return null;
        return (mods, vk.Value);
    }
}
