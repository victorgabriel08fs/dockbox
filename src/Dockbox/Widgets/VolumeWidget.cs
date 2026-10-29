using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dockbox.Core;
using Dockbox.Services;

namespace Dockbox.Widgets;

/// <summary>Volume do alto-falante padrão: rolar ajusta, clique muta, rótulo mostra o dispositivo.</summary>
public sealed class VolumeWidget : DockWidget
{
    private TextBlock _glyph = null!, _percent = null!;
    private Border _bar = null!, _barTrack = null!;
    private AudioVolume.State? _state;
    private int _wheel; // touchpads mandam pedaços de dente: soma até dar um

    public override string Kind => "volume";
    public override string DisplayName => Loc.T("W_Volume_Name");
    public override string Description => Loc.T("W_Volume_Desc");
    public override TimeSpan Interval => TimeSpan.FromSeconds(1);

    public override void Build()
    {
        var tile = Tile(Vertical("#3A3A3F", "#1E1E22"), Solid("#30FFFFFF"));
        var g = new Grid();
        _glyph = Glyph("", 22, Brushes.White);
        _glyph.Margin = new Thickness(0, 0, 0, 14);
        _percent = Text("", 10, Solid("#D0FFFFFF"), FontWeights.SemiBold);
        _percent.Margin = new Thickness(0, 22, 0, 0);
        _barTrack = new Border
        {
            Height = 4, Width = 38, CornerRadius = new CornerRadius(2), Background = Solid("#33FFFFFF"),
            VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 7),
        };
        _bar = new Border { Height = 4, Width = 0, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        _barTrack.Child = _bar;
        g.Children.Add(_glyph);
        g.Children.Add(_percent);
        g.Children.Add(_barTrack);
        tile.Child = g;
        Visual.Children.Add(tile);
        OnThemeChanged();
        Show();
    }

    protected override void OnThemeChanged()
    {
        if (_bar != null) _bar.Background = new SolidColorBrush(Theme.Accent);
    }

    public override Task UpdateAsync()
    {
        _state = AudioVolume.Get();
        Show();
        return Task.CompletedTask;
    }

    private void Show()
    {
        if (_state is not { } s)
        {
            _glyph.Text = "";
            _glyph.Opacity = 0.45;
            _percent.Text = "";
            _bar.Width = 0;
            Label = Loc.T("W_Volume_NoDevice");
            return;
        }
        int pct = (int)Math.Round(s.Level * 100);
        _glyph.Text = WidgetMath.VolumeGlyph(s.Level, s.Muted);
        _glyph.Opacity = s.Muted ? 0.6 : 1;
        _percent.Text = s.Muted ? Loc.T("W_Volume_MutedShort") : $"{pct}%";
        _bar.Width = 38 * s.Level;
        _bar.Opacity = s.Muted ? 0.35 : 1;
        string state = s.Muted ? Loc.T("W_Volume_Muted") : $"{pct}%";
        Label = s.DeviceName.Length > 0 ? Loc.F("W_Volume_Label", state, s.DeviceName) : Loc.F("W_Volume_LabelNoName", state);
    }

    public override bool OnMouseWheel(int delta)
    {
        var s = _state ?? AudioVolume.Get();
        if (s == null) return false;
        _wheel += delta;
        int notches = _wheel / 120;
        if (notches == 0) return true;
        _wheel -= notches * 120;
        double next = WidgetMath.StepVolume(s.Level, notches * 120);
        if (AudioVolume.SetLevel(next)) _state = AudioVolume.Get();
        Show();
        return true;
    }

    public override void OnClick(FrameworkElement anchor)
    {
        var s = _state ?? AudioVolume.Get();
        if (s == null) return;
        if (AudioVolume.SetMute(!s.Muted)) _state = AudioVolume.Get();
        Show();
    }

    public override IEnumerable<(string Header, Action Action)> MenuItems()
    {
        yield return (Loc.T("W_Volume_Up"), () => OnMouseWheel(120 * 5));
        yield return (Loc.T("W_Volume_Down"), () => OnMouseWheel(-120 * 5));
        yield return (Loc.T("W_Volume_Settings"), () => AppLauncher.Open("ms-settings:sound"));
    }
}
