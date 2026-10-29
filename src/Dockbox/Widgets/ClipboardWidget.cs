using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Widgets;

/// <summary>
/// Últimos textos copiados. Clique abre a lista; escolher um copia de novo. Fica só na memória e respeita
/// os programas (como gerenciadores de senha) que pedem para não registrar o que copiam.
/// </summary>
public sealed class ClipboardWidget : DockWidget
{
    private readonly ClipboardHistory _history = new();
    private TextBlock _count = null!;
    private Popup? _popup;
    private uint _seq;
    private bool _paused;

    public override string Kind => "clipboard";
    public override string DisplayName => Loc.T("W_Clipboard_Name");
    public override string Description => Loc.T("W_Clipboard_Desc");
    public override TimeSpan Interval => TimeSpan.FromSeconds(1);

    public override void Build()
    {
        var tile = Tile(Vertical("#E9E4DA", "#CFC6B5"), Solid("#40705C3A"));
        var g = new Grid();
        g.Children.Add(Glyph("", 24, Solid("#5B4A2E")));
        _count = Text("", 9, Solid("#5B4A2E"), FontWeights.SemiBold);
        _count.VerticalAlignment = VerticalAlignment.Bottom;
        _count.Margin = new Thickness(0, 0, 0, 4);
        g.Children.Add(_count);
        tile.Child = g;
        Visual.Children.Add(tile);
        try { _seq = GetClipboardSequenceNumber(); } catch { }
        Show();
    }

    private void Show()
    {
        int n = _history.Items.Count;
        _count.Text = n > 0 ? n.ToString(Loc.Culture) : "";
        Label = _paused ? Loc.T("W_Clipboard_PausedLabel")
            : n == 0 ? Loc.T("W_Clipboard_Empty")
            : Loc.F("W_Clipboard_Label", ClipboardHistory.Preview(_history.Items[0], 40));
    }

    public override Task UpdateAsync()
    {
        uint seq;
        try { seq = GetClipboardSequenceNumber(); } catch { return Task.CompletedTask; }
        if (seq == _seq) return Task.CompletedTask;
        try
        {
            if (!_paused && !Excluded() && Clipboard.ContainsText())
                _history.Add(Clipboard.GetText());
            _seq = seq;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Outro programa está com a área de transferência aberta: tenta no próximo segundo.
            return Task.CompletedTask;
        }
        Show();
        return Task.CompletedTask;
    }

    /// <summary>Formatos que gerenciadores de senha usam para pedir que o histórico não guarde o texto.</summary>
    private static bool Excluded()
    {
        if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return true;
        foreach (var format in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (!Clipboard.ContainsData(format)) continue;
            if (Clipboard.GetData(format) is System.IO.MemoryStream ms && ms.Length >= 4)
            {
                var bytes = ms.ToArray();
                if (BitConverter.ToInt32(bytes, 0) == 0) return true;
            }
        }
        return false;
    }

    public override void OnClick(FrameworkElement anchor)
    {
        if (_popup is { IsOpen: true })
        {
            _popup.IsOpen = false;
            return;
        }

        var list = new StackPanel { Width = 280 };
        list.Children.Add(new TextBlock
        {
            Text = Loc.T("W_Clipboard_Name"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Theme.LabelForeground,
            Margin = new Thickness(8, 2, 8, 6),
        });
        if (_history.Items.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = _paused ? Loc.T("W_Clipboard_PausedLabel") : Loc.T("W_Clipboard_EmptyHint"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.LabelForeground,
                Opacity = 0.7,
                Margin = new Thickness(8, 0, 8, 6),
            });
        }
        foreach (var text in _history.Items)
        {
            var item = text;
            var button = new Button
            {
                Content = new TextBlock { Text = ClipboardHistory.Preview(item), TextTrimming = TextTrimming.CharacterEllipsis },
                ToolTip = item.Length > 500 ? item[..500] + "…" : item,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 1, 0, 1),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Theme.LabelForeground,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, Loc.F("W_Clipboard_CopyA11y", ClipboardHistory.Preview(item, 120)));
            button.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(item);
                    _history.Add(item);
                    _seq = GetClipboardSequenceNumber(); // não conta a própria cópia como nova
                }
                catch { }
                Show();
                if (_popup != null) _popup.IsOpen = false;
            };
            list.Children.Add(button);
        }

        var box = new Border
        {
            Background = Theme.LabelBackground,
            BorderBrush = Theme.LabelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(6),
            Child = list,
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
        if (list.Children.OfType<Button>().FirstOrDefault() is Button first) first.Focus();
    }

    public override IEnumerable<(string Header, Action Action)> MenuItems()
    {
        yield return (_paused ? Loc.T("W_Clipboard_Resume") : Loc.T("W_Clipboard_Pause"), () =>
        {
            _paused = !_paused;
            try { _seq = GetClipboardSequenceNumber(); } catch { }
            Show();
        });
        if (_history.Items.Count > 0)
            yield return (Loc.T("W_Clipboard_Clear"), () =>
            {
                _history.Clear();
                Show();
            });
    }

    public override void Dispose()
    {
        if (_popup != null) _popup.IsOpen = false;
        _history.Clear();
    }
}
