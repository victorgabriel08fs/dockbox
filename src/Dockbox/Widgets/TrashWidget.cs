using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dockbox.Services;
using Path = System.Windows.Shapes.Path;

namespace Dockbox.Widgets;

/// <summary>Lixeira: abre no clique, aceita arquivos arrastados e pode ser esvaziada.</summary>
public sealed class TrashWidget : DockWidget
{
    private Canvas _papers = null!;
    private long _count = -1;

    public override string Kind => "trash";
    public override string DisplayName => "Lixeira";
    public override string Description => "Clique para abrir; arraste arquivos para cá para apagá-los.";
    public override TimeSpan Interval => TimeSpan.FromSeconds(4);
    public override bool AcceptsFiles => true;

    public override void Build()
    {
        var c = new Canvas { Width = D, Height = D };

        // Papéis amassados (aparecem quando a lixeira tem itens)
        _papers = new Canvas { Visibility = Visibility.Collapsed };
        _papers.Children.Add(new Path { Data = Geometry.Parse("M22,20 L30,13 L36,19 L29,24 Z"), Fill = Solid("#F2F2F2"), Stroke = Solid("#B0B0B0"), StrokeThickness = 0.6 });
        _papers.Children.Add(new Path { Data = Geometry.Parse("M31,17 L40,12 L44,21 L35,23 Z"), Fill = Solid("#FFFFFF"), Stroke = Solid("#B0B0B0"), StrokeThickness = 0.6 });
        c.Children.Add(_papers);

        // Corpo em "vidro metálico"
        var body = new Path
        {
            Data = Geometry.Parse("M15,19 L49,19 L45.5,57 Q45,60 42,60 L22,60 Q19,60 18.5,57 Z"),
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromArgb(235, 0xDA, 0xDD, 0xE2), 0),
                new(Color.FromArgb(235, 0xF7, 0xF8, 0xFA), 0.45),
                new(Color.FromArgb(235, 0xB8, 0xBC, 0xC4), 1),
            }, 0),
            Stroke = Solid("#8A8F98"),
            StrokeThickness = 0.8,
        };
        c.Children.Add(body);

        // Grade vertical
        for (int i = 0; i < 5; i++)
        {
            double x0 = 21 + i * 5.5;
            double x1 = 23 + i * 4.5;
            c.Children.Add(new Line { X1 = x0, Y1 = 23, X2 = x1, Y2 = 56, Stroke = Solid("#9097A1"), StrokeThickness = 1.1, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }

        // Aro de cima
        c.Children.Add(new Ellipse
        {
            Width = 36, Height = 6,
            Margin = new Thickness(14, 16, 0, 0),
            Fill = Vertical("#E9EBEE", "#AEB3BB"),
            Stroke = Solid("#7E848D"),
            StrokeThickness = 0.8,
        });

        Visual.Children.Add(c);
    }

    public override Task UpdateAsync()
    {
        long count = RecycleBin.ItemCount();
        if (count != _count)
        {
            _count = count;
            _papers.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Label = count switch
            {
                0 => "Lixeira (vazia)",
                1 => "Lixeira · 1 item",
                _ => $"Lixeira · {count} itens",
            };
        }
        return Task.CompletedTask;
    }

    public override void OnClick(FrameworkElement anchor) => RecycleBin.Open();

    public override IEnumerable<(string Header, Action Action)> MenuItems()
    {
        yield return ("Abrir Lixeira", RecycleBin.Open);
        yield return ("Esvaziar Lixeira...", () => { RecycleBin.Empty(); NextUpdate = DateTime.MinValue; });
    }

    public override void OnFilesDropped(string[] files)
    {
        string what = files.Length == 1 ? $"\"{System.IO.Path.GetFileName(files[0].TrimEnd('\\'))}\"" : $"{files.Length} itens";
        var r = MessageBox.Show($"Mover {what} para a Lixeira?", "Dockbox", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;
        RecycleBin.Delete(files);
        NextUpdate = DateTime.MinValue;
    }
}
