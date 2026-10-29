using System.Windows.Media;
using Dockbox.Core;

namespace Dockbox.Tests;

public sealed class AccessibilityTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", Hotkey.ModControl | Hotkey.ModAlt, (uint)'D')]
    [InlineData("ctrl + alt + space", Hotkey.ModControl | Hotkey.ModAlt, 0x20u)]
    [InlineData("Win+Shift+D", Hotkey.ModWin | Hotkey.ModShift, (uint)'D')]
    [InlineData("Alt+F9", Hotkey.ModAlt, 0x78u)]
    [InlineData("Ctrl+1", Hotkey.ModControl, (uint)'1')]
    public void Hotkey_Parse(string text, uint mods, uint vk) =>
        Assert.Equal((mods, vk), Hotkey.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("D")]            // sem modificador
    [InlineData("Ctrl+Alt")]     // sem tecla
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+F13")]
    public void Hotkey_ParseInvalido(string? text) => Assert.Null(Hotkey.Parse(text));

    [Fact]
    public void Hotkey_TodasAsOpcoesSaoValidasOuDesligado() =>
        Assert.All(Hotkey.Choices, c => Assert.True(c == "" || Hotkey.Parse(c) != null, c));

    [Fact]
    public void Hotkey_Display()
    {
        Assert.Equal("Desligado", Hotkey.Display(""));
        Assert.Equal("Ctrl+Alt+Espaço", Hotkey.Display("Ctrl+Alt+Space"));
    }

    [Fact]
    public void AtalhoInvalidoNoArquivoVoltaAoPadrao()
    {
        var s = new DockSettings { Appearance = new Appearance { KeyboardShortcut = "X" } };
        SettingsStore.Normalize(s);
        Assert.Equal(Hotkey.Default, s.Appearance.KeyboardShortcut);

        s.Appearance.KeyboardShortcut = "";
        SettingsStore.Normalize(s);
        Assert.Equal("", s.Appearance.KeyboardShortcut); // desligado continua desligado
    }

    private static double RelLum(Color c)
    {
        static double Ch(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Ch(c.R) + 0.7152 * Ch(c.G) + 0.0722 * Ch(c.B);
    }

    private static double Contrast(Color a, Color b)
    {
        double l1 = RelLum(a), l2 = RelLum(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    [Theory]
    [InlineData(ThemeMode.Light, "#2A2D3A")]
    [InlineData(ThemeMode.Dark, "#2A2D3A")]
    [InlineData(ThemeMode.Custom, "#FFFFFF")]
    [InlineData(ThemeMode.Custom, "#000000")]
    [InlineData(ThemeMode.Custom, "#8C8C8C")]
    [InlineData(ThemeMode.Custom, "#FF3B30")]
    public void RotuloTemContrasteMinimo(ThemeMode mode, string custom)
    {
        var t = DockTheme.From(new Appearance { Theme = mode, CustomColor = custom });
        var bg = ((SolidColorBrush)t.LabelBackground).Color;
        var fg = ((SolidColorBrush)t.LabelForeground).Color;
        // O fundo do rótulo é quase opaco; mesmo sobre preto ou branco o contraste fica acima de 4,5:1 (WCAG AA).
        foreach (var behind in new[] { Colors.Black, Colors.White })
        {
            var mixed = ColorUtil.Mix(behind, Color.FromRgb(bg.R, bg.G, bg.B), bg.A / 255.0);
            Assert.True(Contrast(mixed, fg) >= 4.5, $"{mode} {custom} sobre {behind}: {Contrast(mixed, fg):0.00}");
        }
    }

    [Fact]
    public void AltoContraste_UsaAsCoresDoSistemaSemTransparencia()
    {
        var t = DockTheme.HighContrast(Colors.Black, Colors.Yellow, Colors.Cyan);
        Assert.False(t.IsLight);
        Assert.Equal(Colors.Black, ((SolidColorBrush)t.Background).Color);
        Assert.Equal(Colors.Yellow, ((SolidColorBrush)t.LabelForeground).Color);
        Assert.Equal(Colors.Yellow, ((SolidColorBrush)t.Border).Color);
        Assert.Equal(Colors.Cyan, t.Accent);
        Assert.Equal(0, t.ShadowOpacity);

        Assert.True(DockTheme.HighContrast(Colors.White, Colors.Black, Colors.Blue).IsLight);
    }
}
