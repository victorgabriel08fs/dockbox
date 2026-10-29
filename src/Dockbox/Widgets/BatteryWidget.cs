using Dockbox.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dockbox.Services;

namespace Dockbox.Widgets;

/// <summary>Nível de bateria. Fica escondido em computadores sem bateria.</summary>
public sealed class BatteryWidget : DockWidget
{
    private Rectangle _fill = null!;
    private TextBlock _percent = null!, _bolt = null!;
    private bool _available = SystemStats.Battery() != null;

    public override string Kind => "battery";
    public override string DisplayName => Loc.T("W_Battery_Name");
    public override string Description => Loc.T("W_Battery_Desc");
    public override bool IsAvailable => _available;
    public override TimeSpan Interval => TimeSpan.FromSeconds(20);

    public override void Build()
    {
        var tile = Tile(Vertical("#2C2C30", "#161618"), Solid("#30FFFFFF"));
        var canvas = new Canvas { Width = 58, Height = 58 };

        var body = new Rectangle { Width = 36, Height = 18, RadiusX = 4.5, RadiusY = 4.5, Stroke = Solid("#B0FFFFFF"), StrokeThickness = 1.6 };
        Canvas.SetLeft(body, 9); Canvas.SetTop(body, 12);
        var cap = new Rectangle { Width = 3, Height = 7, RadiusX = 1.5, RadiusY = 1.5, Fill = Solid("#B0FFFFFF") };
        Canvas.SetLeft(cap, 46); Canvas.SetTop(cap, 17.5);
        _fill = new Rectangle { Height = 13, RadiusX = 2.5, RadiusY = 2.5, Fill = Solid("#30D158") };
        Canvas.SetLeft(_fill, 11.5); Canvas.SetTop(_fill, 14.5);

        _bolt = Glyph("", 12, Brushes.White);
        _bolt.Width = 36; _bolt.TextAlignment = TextAlignment.Center;
        Canvas.SetLeft(_bolt, 9); Canvas.SetTop(_bolt, 14);

        _percent = Text("", 14, Brushes.White, FontWeights.SemiBold);
        _percent.Width = 58;
        Canvas.SetTop(_percent, 34);

        canvas.Children.Add(body);
        canvas.Children.Add(cap);
        canvas.Children.Add(_fill);
        canvas.Children.Add(_bolt);
        canvas.Children.Add(_percent);
        tile.Child = canvas;
        Visual.Children.Add(tile);
    }

    public override Task UpdateAsync()
    {
        var b = SystemStats.Battery();
        bool available = b != null;
        if (available != _available)
        {
            _available = available;
            RaiseAvailabilityChanged();
        }
        if (b is not var (percent, charging)) return Task.CompletedTask;

        _fill.Width = Math.Max(2, 31 * percent / 100.0);
        _fill.Fill = Solid(percent <= 15 && !charging ? "#FF453A" : percent <= 30 && !charging ? "#FFD60A" : "#30D158");
        _bolt.Visibility = charging ? Visibility.Visible : Visibility.Collapsed;
        _percent.Text = $"{percent}%";
        Label = Loc.F(charging ? "W_Battery_Charging" : "W_Battery_Level", percent);
        return Task.CompletedTask;
    }

    public override void OnClick(FrameworkElement anchor) => AppLauncher.Open("ms-settings:batterysaver");
}
