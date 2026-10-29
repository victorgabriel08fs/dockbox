using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dockbox.Core;
using Path = System.Windows.Shapes.Path;

namespace Dockbox.Widgets;

/// <summary>Temporizador (Pomodoro): clique inicia e pausa; o anel mostra o tempo que passou.</summary>
public sealed class TimerWidget : DockWidget
{
    private readonly FocusTimer _timer = new();
    private Path _arc = null!;
    private TextBlock _time = null!, _state = null!;
    private Border _tile = null!;
    private bool _finished;

    public override string Kind => "timer";
    public override string DisplayName => Loc.T("W_Timer_Name");
    public override string Description => Loc.T("W_Timer_Desc");
    public override TimeSpan Interval => TimeSpan.FromSeconds(1);

    private static readonly Brush NormalBg = Vertical("#3A2A2A", "#1E1616");
    private static readonly Brush DoneBg = Vertical("#FF6B5E", "#D9372B");

    public override void Build()
    {
        _tile = Tile(NormalBg, Solid("#30FFFFFF"));
        var g = new Grid();
        g.Children.Add(Ring(22, 4, Solid("#33FFFFFF")));
        _arc = new Path { StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stroke = Solid("#FF6B5E") };
        g.Children.Add(_arc);
        _time = Text("", 11, Brushes.White, FontWeights.SemiBold);
        _state = Glyph("", 7, Solid("#B3FFFFFF"));
        _state.Margin = new Thickness(0, 24, 0, 0);
        g.Children.Add(_time);
        g.Children.Add(_state);
        var holder = new Grid { Width = 64, Height = 64, Margin = new Thickness(-3) };
        holder.Children.Add(g);
        _tile.Child = holder;
        Visual.Children.Add(_tile);
        Show(DateTime.Now);
    }

    public override Task UpdateAsync()
    {
        var now = DateTime.Now;
        if (_timer.Running && _timer.IsDone(now))
        {
            _timer.Pause(now);
            _finished = true;
            try { SystemSounds.Exclamation.Play(); } catch { }
        }
        Show(now);
        return Task.CompletedTask;
    }

    private void Show(DateTime now)
    {
        var remaining = _timer.Remaining(now);
        _arc.Data = Arc(32, 32, 22, _timer.Fraction(now));
        _tile.Background = _finished ? DoneBg : NormalBg;
        _arc.Visibility = _finished ? Visibility.Collapsed : Visibility.Visible;

        // No ícone: minutos restantes (arredondados para cima) e, no último minuto, os segundos.
        _time.Text = _finished ? "0:00"
            : remaining.TotalSeconds > 60 ? $"{(int)Math.Ceiling(remaining.TotalMinutes - 1e-9)}m"
            : FocusTimer.Format(remaining);
        _state.Text = _timer.Running ? "" : ""; // pausa / play
        _state.Visibility = _finished ? Visibility.Collapsed : Visibility.Visible;

        string time = FocusTimer.Format(remaining);
        Label = _finished ? Loc.T("W_Timer_Done")
            : _timer.Running ? Loc.F("W_Timer_Running", time)
            : _timer.IsIdle ? Loc.F("W_Timer_Idle", (int)_timer.Duration.TotalMinutes)
            : Loc.F("W_Timer_Paused", time);
    }

    public override void OnClick(FrameworkElement anchor)
    {
        var now = DateTime.Now;
        if (_finished)
        {
            _finished = false;
            _timer.Reset();
        }
        else if (_timer.Running) _timer.Pause(now);
        else _timer.Start(now);
        Show(now);
    }

    public override IEnumerable<(string Header, Action Action)> MenuItems()
    {
        foreach (int m in FocusTimer.Presets)
        {
            int minutes = m;
            string header = minutes == FocusTimer.DefaultMinutes ? Loc.F("W_Timer_Pomodoro", minutes) : Loc.F("W_Timer_Minutes", minutes);
            if (_timer.Duration.TotalMinutes == minutes) header = "✓ " + header;
            yield return (header, () =>
            {
                _finished = false;
                _timer.SetMinutes(minutes);
                _timer.Start(DateTime.Now);
                Show(DateTime.Now);
            });
        }
        if (!_timer.IsIdle || _finished)
            yield return (Loc.T("W_Timer_Reset"), () =>
            {
                _finished = false;
                _timer.Reset();
                Show(DateTime.Now);
            });
    }
}
