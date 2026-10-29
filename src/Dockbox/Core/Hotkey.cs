namespace Dockbox.Core;

/// <summary>Atalho global para usar o Dock pelo teclado (ex.: "Ctrl+Alt+D").</summary>
public static class Hotkey
{
    public const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8;

    public const string Default = "Ctrl+Alt+D";

    /// <summary>Atalhos oferecidos nas configurações. Vazio = desligado.</summary>
    public static readonly string[] Choices = { "Ctrl+Alt+D", "Ctrl+Alt+Space", "Win+Shift+D", "" };

    /// <summary>Texto para mostrar ao usuário.</summary>
    public static string Display(string? text) =>
        string.IsNullOrEmpty(text) ? "Desligado" : text.Replace("Space", "Espaço");

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
