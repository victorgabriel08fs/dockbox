using Dockbox.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dockbox.Services;
using Path = System.Windows.Shapes.Path;

namespace Dockbox.Widgets;

/// <summary>Anéis de uso de CPU (externo) e memória (interno).</summary>
public sealed class SystemWidget : DockWidget
{
    private Path _cpuArc = null!, _ramArc = null!;
    private TextBlock _cpuText = null!;
    private double _cpu;

    public override string Kind => "system";
    public override string DisplayName => Loc.T("W_System_Name");
    public override string Description => Loc.T("W_System_Desc");

    public override void Build()
    {
        var tile = Tile(Vertical("#2C2C30", "#161618"), Solid("#30FFFFFF"));
        var g = new Grid();

        g.Children.Add(Ring(22, 4.5, Solid("#33FFFFFF")));
        g.Children.Add(Ring(15, 4.5, Solid("#33FFFFFF")));
        _cpuArc = new Path { StrokeThickness = 4.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        _ramArc = new Path { StrokeThickness = 4.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stroke = Solid("#30D158") };
        g.Children.Add(_cpuArc);
        g.Children.Add(_ramArc);

        _cpuText = Text("", 9.5, Brushes.White, FontWeights.SemiBold);
        g.Children.Add(_cpuText);

        // O Grid do tile tem 58x58; centraliza as geometrias desenhadas em 64x64
        var holder = new Grid { Width = 64, Height = 64, Margin = new Thickness(-3) };
        holder.Children.Add(g);
        tile.Child = holder;
        Visual.Children.Add(tile);
        OnThemeChanged();
    }

    protected override void OnThemeChanged()
    {
        if (_cpuArc != null) _cpuArc.Stroke = new SolidColorBrush(Theme.Accent);
    }

    public override Task UpdateAsync()
    {
        double cpu = SystemStats.CpuUsage();
        _cpu = _cpu * 0.5 + cpu * 0.5; // suaviza
        var (ram, used, total) = SystemStats.Memory();

        _cpuArc.Data = Arc(32, 32, 22, _cpu);
        _ramArc.Data = Arc(32, 32, 15, ram);
        _cpuText.Text = $"{_cpu * 100:0}%";

        Label = Loc.F("W_System_Label", _cpu * 100, used, total);
        return Task.CompletedTask;
    }

    public override void OnClick(FrameworkElement anchor) => AppLauncher.Open("taskmgr.exe");
}
