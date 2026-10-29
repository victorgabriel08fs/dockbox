using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dockbox.Core;
using Dockbox.Services;

namespace Dockbox.Widgets;

/// <summary>Rede em uso (Wi-Fi ou cabo), nome da rede e velocidade de download e upload.</summary>
public sealed class NetworkWidget : DockWidget
{
    private TextBlock _glyph = null!, _down = null!, _up = null!;
    private readonly Stopwatch _clock = new();
    private long _lastRx = -1, _lastTx = -1;
    private double _rxRate, _txRate;

    public override string Kind => "network";
    public override string DisplayName => Loc.T("W_Network_Name");
    public override string Description => Loc.T("W_Network_Desc");
    public override TimeSpan Interval => TimeSpan.FromSeconds(1);

    public override void Build()
    {
        var tile = Tile(Vertical("#1F3A5F", "#0E1C30"), Solid("#30FFFFFF"));
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
        g.RowDefinitions.Add(new RowDefinition());
        g.RowDefinitions.Add(new RowDefinition());
        _glyph = Glyph("", 18, Brushes.White);
        _glyph.VerticalAlignment = VerticalAlignment.Bottom;
        _glyph.Margin = new Thickness(0, 0, 0, 2);
        _down = Text("", 8.5, Solid("#E6FFFFFF"), FontWeights.SemiBold);
        _up = Text("", 8.5, Solid("#A6FFFFFF"));
        _up.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetRow(_down, 1);
        Grid.SetRow(_up, 2);
        g.Children.Add(_glyph);
        g.Children.Add(_down);
        g.Children.Add(_up);
        tile.Child = g;
        Visual.Children.Add(tile);
        _down.Text = "↓ –";
        _up.Text = "↑ –";
        Label = DisplayName;
    }

    public override Task UpdateAsync()
    {
        var s = NetworkStatus.Current();
        double seconds = _clock.Elapsed.TotalSeconds;
        _clock.Restart();
        if (s.Kind == NetworkKind.None)
        {
            _lastRx = _lastTx = -1;
            _rxRate = _txRate = 0;
            _glyph.Text = "";
            _glyph.Opacity = 0.35;
            _down.Text = Loc.T("W_Network_Offline");
            _up.Text = "";
            Label = Loc.T("W_Network_OfflineLabel");
            return Task.CompletedTask;
        }

        if (_lastRx >= 0)
        {
            // Média com a leitura anterior, para o número não pular tanto.
            _rxRate = _rxRate * 0.4 + WidgetMath.Rate(_lastRx, s.BytesReceived, seconds) * 0.6;
            _txRate = _txRate * 0.4 + WidgetMath.Rate(_lastTx, s.BytesSent, seconds) * 0.6;
        }
        _lastRx = s.BytesReceived;
        _lastTx = s.BytesSent;

        _glyph.Text = s.Kind == NetworkKind.WiFi ? "" : "";
        _glyph.Opacity = 1;
        string down = WidgetMath.FormatSpeed(_rxRate, Loc.Culture), up = WidgetMath.FormatSpeed(_txRate, Loc.Culture);
        _down.Text = "↓ " + down;
        _up.Text = "↑ " + up;
        string kind = s.Kind switch
        {
            NetworkKind.WiFi => Loc.T("W_Network_WiFi"),
            NetworkKind.Ethernet => Loc.T("W_Network_Ethernet"),
            _ => Loc.T("W_Network_Other"),
        };
        Label = Loc.F("W_Network_Label", kind, s.Name, down, up);
        return Task.CompletedTask;
    }

    public override void OnClick(FrameworkElement anchor) => AppLauncher.Open("ms-settings:network-status");

    public override IEnumerable<(string Header, Action Action)> MenuItems()
    {
        yield return (Loc.T("W_Network_Settings"), () => AppLauncher.Open("ms-settings:network-status"));
        yield return (Loc.T("W_Network_WiFiList"), () => AppLauncher.Open("ms-availablenetworks:"));
    }
}
