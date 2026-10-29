using Dockbox.Core;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Dockbox.Widgets;

/// <summary>Ícone de calendário com o dia de hoje; clique abre um mini calendário.</summary>
public sealed class CalendarWidget : DockWidget
{
    private TextBlock _weekday = null!, _day = null!;
    private Popup? _popup;

    public override string Kind => "calendar";
    public override string DisplayName => Loc.T("W_Calendar_Name");
    public override string Description => Loc.T("W_Calendar_Desc");
    public override TimeSpan Interval => TimeSpan.FromSeconds(30);

    public override void Build()
    {
        var tile = Tile(Vertical("#FFFFFF", "#F1F1F4"));
        var stack = new Grid();
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        stack.RowDefinitions.Add(new RowDefinition());

        _weekday = Text("", 10, Solid("#FF3B30"), FontWeights.SemiBold);
        _weekday.VerticalAlignment = VerticalAlignment.Bottom;
        _day = Text("", 30, Solid("#1C1C1E"), FontWeights.Light);
        _day.Margin = new Thickness(0, -4, 0, 0);
        Grid.SetRow(_day, 1);

        stack.Children.Add(_weekday);
        stack.Children.Add(_day);
        tile.Child = stack;
        Visual.Children.Add(tile);
        Tick();
    }

    public override Task UpdateAsync()
    {
        Tick();
        return Task.CompletedTask;
    }

    private void Tick()
    {
        var now = DateTime.Now;
        var c = Loc.Culture;
        _weekday.Text = now.ToString("ddd", c).TrimEnd('.').ToUpper(c);
        _day.Text = now.Day.ToString(c);
        Label = Loc.Capitalize(now.ToString("D", c));
    }

    public override void OnClick(FrameworkElement anchor)
    {
        if (_popup is { IsOpen: true })
        {
            _popup.IsOpen = false;
            return;
        }

        var cal = new System.Windows.Controls.Calendar
        {
            DisplayDate = DateTime.Today,
            SelectedDate = DateTime.Today,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag),
            FirstDayOfWeek = Loc.Culture.DateTimeFormat.FirstDayOfWeek,
        };
        var box = new Border
        {
            Background = Theme.LabelBackground,
            BorderBrush = Theme.LabelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Child = new Viewbox { Width = 260, Child = cal },
        };

        _popup = new Popup
        {
            Child = box,
            PlacementTarget = anchor,
            Placement = PlacementMode.Top,
            VerticalOffset = -12,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
        };
        _popup.IsOpen = true;
    }
}
