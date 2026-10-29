using System.Windows.Media;

namespace Dockbox.Core;

public static class ColorUtil
{
    public static bool IsValid(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { ColorConverter.ConvertFromString(hex); return true; }
        catch { return false; }
    }

    public static Color Parse(string? hex, Color fallback)
    {
        if (!IsValid(hex)) return fallback;
        return (Color)ColorConverter.ConvertFromString(hex!);
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color WithAlpha(Color c, double alpha) =>
        Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Luminância relativa aproximada (0 = preto, 1 = branco).</summary>
    public static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
