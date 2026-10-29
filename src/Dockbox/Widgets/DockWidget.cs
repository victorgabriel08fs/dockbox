using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dockbox.Core;
using Path = System.Windows.Shapes.Path;

namespace Dockbox.Widgets;

/// <summary>
/// Um widget é um "ícone vivo" no Dock. Ele desenha seu conteúdo numa área de 64x64
/// (unidades de design) e o Dock escala esse desenho junto com a ampliação.
/// </summary>
public abstract class DockWidget
{
    public const double D = 64; // tamanho de design

    public abstract string Kind { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }

    /// <summary>Conteúdo 64x64. Criado uma vez por instância.</summary>
    public Grid Visual { get; }

    /// <summary>Texto mostrado acima do widget ao passar o mouse.</summary>
    public string Label { get; protected set; } = "";

    /// <summary>False esconde o widget do Dock (ex.: bateria num desktop).</summary>
    public virtual bool IsAvailable => true;

    /// <summary>De quanto em quanto tempo <see cref="UpdateAsync"/> roda.</summary>
    public virtual TimeSpan Interval => TimeSpan.FromSeconds(1);

    public DateTime NextUpdate { get; set; } = DateTime.MinValue;

    protected DockTheme Theme { get; private set; } = DockTheme.From(new Appearance());

    /// <summary>Avise o Dock quando a disponibilidade mudar.</summary>
    public event Action? AvailabilityChanged;

    protected DockWidget()
    {
        Visual = new Grid { Width = D, Height = D, Background = Brushes.Transparent };
    }

    public void SetTheme(DockTheme theme)
    {
        Theme = theme;
        OnThemeChanged();
    }

    /// <summary>Monta o desenho. Chamado uma vez, depois do construtor.</summary>
    public abstract void Build();

    protected virtual void OnThemeChanged() { }

    public virtual Task UpdateAsync() => Task.CompletedTask;

    public virtual void OnClick(FrameworkElement anchor) { }

    public virtual IEnumerable<(string Header, Action Action)> MenuItems() => Array.Empty<(string, Action)>();

    public virtual bool AcceptsFiles => false;
    public virtual void OnFilesDropped(string[] files) { }

    public virtual void Dispose() { }

    protected void RaiseAvailabilityChanged() => AvailabilityChanged?.Invoke();

    // ---------- Ajudantes de desenho ----------

    /// <summary>Quadrado arredondado no estilo dos ícones do macOS.</summary>
    protected static Border Tile(Brush background, Brush? border = null) => new()
    {
        Width = 58,
        Height = 58,
        CornerRadius = new CornerRadius(14),
        Background = background,
        BorderBrush = border ?? new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
        BorderThickness = new Thickness(0.6),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        ClipToBounds = true,
    };

    protected static LinearGradientBrush Vertical(string top, string bottom)
    {
        var b = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(top),
            (Color)ColorConverter.ConvertFromString(bottom), 90);
        b.Freeze();
        return b;
    }

    protected static SolidColorBrush Solid(string hex) =>
        ColorUtil.Brush((Color)ColorConverter.ConvertFromString(hex));

    protected static TextBlock Text(string text, double size, Brush fg, FontWeight? weight = null) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = fg,
        FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        FontWeight = weight ?? FontWeights.Normal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
    };

    protected static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    protected static TextBlock Glyph(string glyph, double size, Brush fg) => new()
    {
        Text = glyph,
        FontSize = size,
        Foreground = fg,
        FontFamily = IconFont,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Arco de anel começando no topo, no sentido horário.</summary>
    public static Geometry Arc(double cx, double cy, double r, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction >= 0.9999) return new EllipseGeometry(new Point(cx, cy), r, r);
        if (fraction <= 0.0001) return Geometry.Empty;
        double angle = fraction * 2 * Math.PI;
        var start = new Point(cx, cy - r);
        var end = new Point(cx + r * Math.Sin(angle), cy - r * Math.Cos(angle));
        var fig = new PathFigure { StartPoint = start, IsClosed = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        return g;
    }

    protected static Path Ring(double r, double thickness, Brush stroke) => new()
    {
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Data = new EllipseGeometry(new Point(32, 32), r, r),
    };
}
