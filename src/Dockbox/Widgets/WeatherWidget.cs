using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dockbox.Core;
using Dockbox.Services;
using Path = System.Windows.Shapes.Path;

namespace Dockbox.Widgets;

/// <summary>Clima atual (Open-Meteo). A cidade é configurável; vazio = localização pelo IP.</summary>
public sealed class WeatherWidget : DockWidget
{
    private Border _tile = null!;
    private Canvas _icon = null!;
    private TextBlock _temp = null!;
    private WeatherInfo? _info;
    private string? _lastCity;

    public override string Kind => "weather";
    public override string DisplayName => "Clima";
    public override string Description => "Temperatura e condição atual da sua cidade.";
    public override TimeSpan Interval => _info == null ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(15);

    public override void Build()
    {
        _tile = Tile(Vertical("#4FA3F7", "#2B6FD8"));
        var g = new Grid();
        _icon = new Canvas { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(-3, -3, 0, 0) };
        _temp = Text("--°", 17, Brushes.White, FontWeights.SemiBold);
        _temp.VerticalAlignment = VerticalAlignment.Bottom;
        _temp.HorizontalAlignment = HorizontalAlignment.Center;
        _temp.Margin = new Thickness(0, 0, 0, 3);
        g.Children.Add(_icon);
        g.Children.Add(_temp);
        _tile.Child = g;
        Visual.Children.Add(_tile);
        Draw(WeatherKind.PartlyCloudy, true);
        Label = "Clima: carregando…";
    }

    public override async Task UpdateAsync()
    {
        string city = SettingsStore.Current.WeatherCity;
        if (city != _lastCity) _info = null;
        _lastCity = city;

        if (string.IsNullOrWhiteSpace(city) && !SettingsStore.Current.WeatherAutoLocate)
        {
            _info = null;
            _temp.Text = "--°";
            Label = "Clima: defina sua cidade em Gerenciar Dock › Widgets";
            return;
        }

        var info = await WeatherService.GetAsync(city);
        if (info == null)
        {
            if (_info == null) Label = "Clima indisponível (sem internet ou cidade não encontrada)";
            return;
        }
        _info = info;
        _temp.Text = $"{Math.Round(info.Temperature):0}°";
        _tile.Background = info.IsDay ? Vertical("#4FA3F7", "#2B6FD8") : Vertical("#25365C", "#101A33");
        Draw(info.Kind, info.IsDay);
        string where = string.IsNullOrEmpty(info.City) ? "" : info.City + " · ";
        Label = $"{where}{info.Temperature:0.#}° · {info.Description}";
    }

    /// <summary>Força recarregar (ex.: cidade mudou).</summary>
    public void Refresh() => NextUpdate = DateTime.MinValue;

    public override void OnClick(FrameworkElement anchor)
    {
        string q = _info?.City is { Length: > 0 } c ? c : SettingsStore.Current.WeatherCity;
        AppLauncher.Open("https://www.google.com/search?q=" + Uri.EscapeDataString("previsão do tempo " + q));
    }

    // ---------- Ícones desenhados ----------

    private void Draw(WeatherKind kind, bool day)
    {
        _icon.Children.Clear();
        var white = Brushes.White;
        var cloudFill = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255));
        var grayCloud = Solid("#D7DEE8");

        switch (kind)
        {
            case WeatherKind.Clear:
                if (day) Sun(32, 25, 10); else Moon(32, 25, 10);
                break;
            case WeatherKind.PartlyCloudy:
                if (day) Sun(25, 20, 8); else Moon(25, 20, 8);
                Cloud(34, 30, 1.0, cloudFill);
                break;
            case WeatherKind.Cloudy:
            case WeatherKind.Fog:
                Cloud(27, 22, 0.8, grayCloud);
                Cloud(35, 29, 1.0, cloudFill);
                if (kind == WeatherKind.Fog)
                    for (int i = 0; i < 2; i++)
                        _icon.Children.Add(new Line { X1 = 18, X2 = 46, Y1 = 40 + i * 4, Y2 = 40 + i * 4, Stroke = white, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                break;
            case WeatherKind.Drizzle:
            case WeatherKind.Rain:
            case WeatherKind.Thunder:
                Cloud(32, 24, 1.0, cloudFill);
                if (kind == WeatherKind.Thunder)
                {
                    _icon.Children.Add(new Path { Data = Geometry.Parse("M33,32 L27,41 L31,41 L28,48 L36,38 L32,38 L35,32 Z"), Fill = Solid("#FFD60A") });
                }
                else
                {
                    int drops = kind == WeatherKind.Rain ? 3 : 2;
                    for (int i = 0; i < drops; i++)
                    {
                        double x = 25 + i * (drops == 3 ? 7 : 10);
                        _icon.Children.Add(new Line { X1 = x, X2 = x - 2, Y1 = 34, Y2 = 40, Stroke = Solid("#BFE3FF"), StrokeThickness = 2.2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                    }
                }
                break;
            case WeatherKind.Snow:
                Cloud(32, 24, 1.0, cloudFill);
                for (int i = 0; i < 3; i++)
                    _icon.Children.Add(new Ellipse { Width = 3.5, Height = 3.5, Fill = white, Margin = new Thickness(23 + i * 7, 35 + (i % 2) * 3, 0, 0) });
                break;
        }
    }

    private void Sun(double cx, double cy, double r)
    {
        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4;
            _icon.Children.Add(new Line
            {
                X1 = cx + Math.Cos(a) * (r + 3), Y1 = cy + Math.Sin(a) * (r + 3),
                X2 = cx + Math.Cos(a) * (r + 6.5), Y2 = cy + Math.Sin(a) * (r + 6.5),
                Stroke = Solid("#FFD60A"), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            });
        }
        _icon.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Fill = Vertical("#FFE45C", "#FFB800"), Margin = new Thickness(cx - r, cy - r, 0, 0) });
    }

    private void Moon(double cx, double cy, double r)
    {
        var moon = new CombinedGeometry(GeometryCombineMode.Exclude,
            new EllipseGeometry(new Point(cx, cy), r, r),
            new EllipseGeometry(new Point(cx + r * 0.55, cy - r * 0.35), r * 0.85, r * 0.85));
        _icon.Children.Add(new Path { Data = moon, Fill = Solid("#F5F0D0") });
    }

    private void Cloud(double cx, double cy, double scale, Brush fill)
    {
        var g = new GeometryGroup { FillRule = FillRule.Nonzero };
        g.Children.Add(new EllipseGeometry(new Point(cx - 7 * scale, cy + 2 * scale), 6 * scale, 6 * scale));
        g.Children.Add(new EllipseGeometry(new Point(cx, cy - 2 * scale), 8 * scale, 8 * scale));
        g.Children.Add(new EllipseGeometry(new Point(cx + 8 * scale, cy + 2.5 * scale), 5.5 * scale, 5.5 * scale));
        g.Children.Add(new RectangleGeometry(new Rect(cx - 7 * scale, cy + 1 * scale, 15 * scale, 7 * scale), 3, 3));
        _icon.Children.Add(new Path { Data = g, Fill = fill });
    }
}
