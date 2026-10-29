using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dockbox.Core;

namespace Dockbox.Widgets;

/// <summary>Relógio analógico no estilo do macOS.</summary>
public sealed class ClockWidget : DockWidget
{
    private readonly RotateTransform _hour = new(0, 32, 32);
    private readonly RotateTransform _minute = new(0, 32, 32);
    private readonly RotateTransform _second = new(0, 32, 32);

    public override string Kind => "clock";
    public override string DisplayName => "Relógio";
    public override string Description => "Relógio analógico; mostra data e hora ao passar o mouse.";

    public override void Build()
    {
        var face = new Ellipse
        {
            Width = 58, Height = 58,
            Fill = new RadialGradientBrush(Colors.White, Color.FromRgb(0xE9, 0xE9, 0xEE)),
            Stroke = Solid("#1C1C1E"),
            StrokeThickness = 3,
        };
        Visual.Children.Add(face);

        var canvas = new Canvas { Width = D, Height = D };
        for (int i = 0; i < 12; i++)
        {
            bool major = i % 3 == 0;
            var tick = new Line
            {
                X1 = 32, Y1 = major ? 7.5 : 8,
                X2 = 32, Y2 = major ? 13 : 11,
                Stroke = Solid(major ? "#1C1C1E" : "#8E8E93"),
                StrokeThickness = major ? 2.2 : 1.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new RotateTransform(i * 30, 32, 32),
            };
            canvas.Children.Add(tick);
        }

        canvas.Children.Add(Hand(17, 3.2, "#1C1C1E", _hour));
        canvas.Children.Add(Hand(9, 2.2, "#1C1C1E", _minute));
        canvas.Children.Add(Hand(9.5, 1.1, "#FF9500", _second, tail: 6));
        canvas.Children.Add(new Ellipse { Width = 4, Height = 4, Fill = Solid("#FF9500"), Margin = new Thickness(30, 30, 0, 0) });
        Visual.Children.Add(canvas);
        Tick();
    }

    private static Line Hand(double top, double thickness, string color, RotateTransform rt, double tail = 3) => new()
    {
        X1 = 32, Y1 = 32 + tail,
        X2 = 32, Y2 = top,
        Stroke = Solid(color),
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        RenderTransform = rt,
    };

    public override Task UpdateAsync()
    {
        Tick();
        return Task.CompletedTask;
    }

    private void Tick()
    {
        var now = DateTime.Now;
        _second.Angle = now.Second * 6;
        _minute.Angle = now.Minute * 6 + now.Second * 0.1;
        _hour.Angle = (now.Hour % 12) * 30 + now.Minute * 0.5;

        var pt = CultureInfo.GetCultureInfo("pt-BR");
        string time = SettingsStore.Current.Clock24h ? now.ToString("HH:mm", pt) : now.ToString("h:mm tt", CultureInfo.InvariantCulture);
        Label = $"{time} · {pt.TextInfo.ToTitleCase(now.ToString("dddd, d 'de' MMMM", pt))}";
    }
}
