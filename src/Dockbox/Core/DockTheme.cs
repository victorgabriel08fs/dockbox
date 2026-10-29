using System.Windows.Media;
using Microsoft.Win32;

namespace Dockbox.Core;

/// <summary>Cores derivadas das configurações de aparência e do tema do Windows.</summary>
public sealed class DockTheme
{
    public bool IsLight { get; private init; }
    public Brush Background { get; private init; } = Brushes.Transparent;
    public Brush Border { get; private init; } = Brushes.Transparent;
    public Brush InnerHighlight { get; private init; } = Brushes.Transparent;
    public Brush Separator { get; private init; } = Brushes.Transparent;
    public Brush LabelBackground { get; private init; } = Brushes.Black;
    public Brush LabelForeground { get; private init; } = Brushes.White;
    public Brush LabelBorder { get; private init; } = Brushes.Transparent;
    public Brush Dot { get; private init; } = Brushes.White;
    public Color Accent { get; private init; }
    public Color ShadowColor { get; private init; }
    public double ShadowOpacity { get; private init; }

    public static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    public static DockTheme From(Appearance a)
    {
        Color baseColor = a.Theme switch
        {
            ThemeMode.Light => Color.FromRgb(0xF2, 0xF2, 0xF6),
            ThemeMode.Dark => Color.FromRgb(0x24, 0x24, 0x2A),
            ThemeMode.Custom => ColorUtil.Parse(a.CustomColor, Color.FromRgb(0x2A, 0x2D, 0x3A)),
            _ => SystemUsesLightTheme() ? Color.FromRgb(0xF2, 0xF2, 0xF6) : Color.FromRgb(0x24, 0x24, 0x2A),
        };
        bool light = ColorUtil.Luminance(baseColor) > 0.55;
        double op = a.Opacity;

        // Gradiente vertical sutil: um pouco mais claro em cima, como vidro.
        Color top = ColorUtil.WithAlpha(ColorUtil.Mix(baseColor, Colors.White, light ? 0.35 : 0.08), op);
        Color bottom = ColorUtil.WithAlpha(ColorUtil.Mix(baseColor, light ? Colors.White : Colors.Black, light ? 0.05 : 0.18), op);
        var bg = new LinearGradientBrush(top, bottom, 90);
        bg.Freeze();

        // Contorno visível mesmo com opacidade baixa
        double borderAlpha = Math.Max(0.18, op * 0.5);
        Color border = light ? Color.FromArgb((byte)(borderAlpha * 110), 0, 0, 0)
                             : Color.FromArgb((byte)(borderAlpha * 140), 255, 255, 255);

        var highlight = new LinearGradientBrush(
            Color.FromArgb((byte)(light ? 200 : 70), 255, 255, 255),
            Color.FromArgb(0, 255, 255, 255), 90);
        highlight.Freeze();

        Color accent = ColorUtil.Parse(a.AccentColor, Color.FromRgb(0x0A, 0x84, 0xFF));

        return new DockTheme
        {
            IsLight = light,
            Background = bg,
            Border = ColorUtil.Brush(border),
            InnerHighlight = highlight,
            Separator = ColorUtil.Brush(light ? Color.FromArgb(70, 0, 0, 0) : Color.FromArgb(80, 255, 255, 255)),
            LabelBackground = ColorUtil.Brush(light ? Color.FromArgb(235, 246, 246, 248) : Color.FromArgb(235, 40, 40, 44)),
            LabelForeground = ColorUtil.Brush(light ? Color.FromRgb(20, 20, 24) : Colors.White),
            LabelBorder = ColorUtil.Brush(light ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(50, 255, 255, 255)),
            Dot = ColorUtil.Brush(light ? Color.FromArgb(200, 30, 30, 34) : Color.FromArgb(230, 255, 255, 255)),
            Accent = accent,
            ShadowColor = Colors.Black,
            ShadowOpacity = light ? 0.22 : 0.45,
        };
    }
}
