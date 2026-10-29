using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Views;

/// <summary>
/// Painel com as miniaturas ao vivo das janelas de um app, mostrado acima do ícone no Dock.
/// As miniaturas são desenhadas pelo próprio Windows (DwmRegisterThumbnail) por cima de caixas vazias
/// do painel. Clicar numa miniatura traz a janela; o X fecha a janela.
/// A janela não é transparente (miniaturas do DWM não aparecem em janelas em camadas); os cantos
/// arredondados vêm de uma região, como no desfoque do Dock.
/// </summary>
internal sealed class WindowPreview : Window
{
    /// <summary>Uma janela do app: identificador, título e se está minimizada.</summary>
    public sealed record Item(IntPtr Handle, string Title, bool Minimized);

    private const double CardPad = 8;
    private const double CardGap = 4;
    private const double TitleHeight = 24;
    private const double FramePad = 6;

    private sealed class Card
    {
        public required IntPtr Handle;
        public required Border Root;
        public required Border Box;
        public IntPtr Thumb;
    }

    private readonly StackPanel _panel = new();
    private readonly Border _frame;
    private readonly List<Card> _cards = new();
    private IntPtr _hwnd;
    private DockTheme _theme = DockTheme.From(new Appearance());
    private Placement? _last;

    /// <summary>Onde o painel fica: ponto de apoio (pixels físicos), borda do Dock e monitor.</summary>
    private sealed record Placement(int X, int Y, DockEdge Edge, RECT Screen, double Scale, double Radius);

    /// <summary>A miniatura foi clicada: trazer essa janela.</summary>
    public event Action<IntPtr>? WindowChosen;
    /// <summary>O X foi clicado: fechar essa janela.</summary>
    public event Action<IntPtr>? CloseRequested;

    public int CardCount => _cards.Count;
    /// <summary>True entre ShowFor e HidePanel (não depende do IsVisible do WPF, que pode demorar a mudar).</summary>
    public bool IsOpen { get; private set; }
    public IntPtr Handle => _hwnd;
    public IReadOnlyList<IntPtr> Handles => _cards.Select(c => c.Handle).ToList();

    public WindowPreview()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Width = Height = 1;
        Left = Top = -32000;
        _frame = new Border { Padding = new Thickness(FramePad), BorderThickness = new Thickness(1), Child = _panel };
        Content = _frame;
        AutomationProperties.SetName(this, Loc.T("Preview_Name"));

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            int round = DWMWCP_DONOTROUND; // os cantos vêm da região
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        };
        // Ao cair num monitor com outra escala, o WPF redesenha em outro tamanho: refaz posição e miniaturas.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (IsOpen && _last != null) Place(_last); });
        Closed += (_, _) => ClearCards();
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Clicar no painel não tira o foco da janela atual (como no Dock).
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Mostra as janelas <paramref name="items"/> com o painel encostado no ponto (<paramref name="x"/>,
    /// <paramref name="y"/>) em pixels físicos: acima dele com o Dock embaixo, abaixo com o Dock em cima,
    /// e ao lado com o Dock nas laterais.
    /// </summary>
    public void ShowFor(IReadOnlyList<Item> items, ImageSource? icon, DockTheme theme, double cornerRadius,
        int x, int y, DockEdge edge, RECT screen, double scale)
    {
        _theme = theme;
        bool horizontal = edge is DockEdge.Bottom or DockEdge.Top;
        _panel.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;

        // Quantas cabem na tela, no eixo do Dock.
        double available = (horizontal ? screen.Right - screen.Left : screen.Bottom - screen.Top) / Math.Max(0.5, scale) - 24 - FramePad * 2;
        double fixedLen = horizontal ? CardPad * 2 + CardGap : CardPad * 2 + TitleHeight + CardGap;
        double cardLen = fixedLen + (horizontal ? PreviewLayout.BoxWidth : PreviewLayout.BoxHeight);
        var (factor, count) = PreviewLayout.Arrange(items.Count, cardLen, fixedLen, available);
        double boxW = Math.Round(PreviewLayout.BoxWidth * factor), boxH = Math.Round(PreviewLayout.BoxHeight * factor);

        ClearCards();
        foreach (var item in items.Take(count))
            _cards.Add(CreateCard(item, icon, boxW, boxH));
        foreach (var c in _cards) _panel.Children.Add(c.Root);

        ApplyTheme();
        if (!IsOpen) Show();
        IsOpen = true;
        var p = new Placement(x, y, edge, screen, scale, Math.Clamp(cornerRadius, 4, 16));
        Place(p);
    }

    /// <summary>Esconde o painel e solta as miniaturas.</summary>
    public void HidePanel()
    {
        ClearCards();
        _last = null;
        if (IsOpen) Hide();
        IsOpen = false;
    }

    /// <summary>True com o mouse em cima do painel.</summary>
    public bool ContainsCursor()
    {
        if (!IsOpen || _hwnd == IntPtr.Zero || !GetCursorPos(out POINT pt) || !GetWindowRect(_hwnd, out RECT r)) return false;
        return pt.X >= r.Left && pt.X < r.Right && pt.Y >= r.Top && pt.Y < r.Bottom;
    }

    private void ApplyTheme()
    {
        // Sem transparência: cor do rótulo do Dock, sem o alfa.
        Background = Opaque(_theme.LabelBackground);
        _frame.Background = Background;
        _frame.BorderBrush = _theme.LabelBorder;
    }

    private static Brush Opaque(Brush b) =>
        b is SolidColorBrush s ? ColorUtil.Brush(Color.FromRgb(s.Color.R, s.Color.G, s.Color.B)) : b;

    private Card CreateCard(Item item, ImageSource? icon, double boxW, double boxH)
    {
        var fg = _theme.LabelForeground;
        var hover = ColorUtil.Brush(_theme.IsLight ? Color.FromArgb(28, 0, 0, 0) : Color.FromArgb(36, 255, 255, 255));

        var close = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Cursor = Cursors.Hand,
            Background = Brushes.Transparent, Margin = new Thickness(6, 0, 0, 0), Opacity = 0.75,
            Child = new TextBlock { Text = "✕", FontSize = 11, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            ToolTip = Loc.T("Preview_Close"),
        };
        AutomationProperties.SetName(close, Loc.F("Preview_CloseA11y", item.Title));
        close.MouseEnter += (_, _) => close.Background = hover;
        close.MouseLeave += (_, _) => close.Background = Brushes.Transparent;
        close.MouseLeftButtonDown += (_, e) => e.Handled = true;
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseRequested?.Invoke(item.Handle);
        };
        DockPanel.SetDock(close, Dock.Right);

        var title = new TextBlock
        {
            Text = item.Minimized ? Loc.F("Menu_Minimized", item.Title) : item.Title,
            Foreground = fg, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        var appIcon = new Image { Source = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(appIcon, Dock.Left);
        var header = new DockPanel { Height = TitleHeight, LastChildFill = true, Width = boxW };
        header.Children.Add(close);
        header.Children.Add(appIcon);
        header.Children.Add(title);

        // Caixa vazia onde o Windows desenha a miniatura. Minimizada: o Windows não tem imagem atual,
        // então mostra o ícone do app.
        var box = new Border
        {
            Width = boxW, Height = boxH, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 4, 0, 0),
            Background = ColorUtil.Brush(_theme.IsLight ? Color.FromArgb(18, 0, 0, 0) : Color.FromArgb(22, 255, 255, 255)),
        };
        if (item.Minimized || icon == null)
            box.Child = new Image { Source = icon, Width = 48, Height = 48, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(box);
        var root = new Border
        {
            Child = stack, Padding = new Thickness(CardPad), CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand,
            Background = Brushes.Transparent, Margin = new Thickness(CardGap / 2), Focusable = false,
        };
        AutomationProperties.SetName(root, item.Title);
        AutomationProperties.SetHelpText(root, Loc.T("Preview_CardHelp"));
        root.MouseEnter += (_, _) => root.Background = hover;
        root.MouseLeave += (_, _) => root.Background = Brushes.Transparent;
        root.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            WindowChosen?.Invoke(item.Handle);
        };

        return new Card { Handle = item.Handle, Root = root, Box = box };
    }

    private void Place(Placement p)
    {
        _last = p;
        if (_hwnd == IntPtr.Zero) return;
        _frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _frame.DesiredSize;
        int w = (int)Math.Ceiling(size.Width * p.Scale), h = (int)Math.Ceiling(size.Height * p.Scale);
        var s = p.Screen;

        int left, top;
        switch (p.Edge)
        {
            case DockEdge.Top: left = p.X - w / 2; top = p.Y; break;
            case DockEdge.Left: left = p.X; top = p.Y - h / 2; break;
            case DockEdge.Right: left = p.X - w; top = p.Y - h / 2; break;
            default: left = p.X - w / 2; top = p.Y - h; break;
        }
        int margin = (int)Math.Round(8 * p.Scale);
        left = Math.Clamp(left, s.Left + margin, Math.Max(s.Left + margin, s.Right - margin - w));
        top = Math.Clamp(top, s.Top + margin, Math.Max(s.Top + margin, s.Bottom - margin - h));

        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, w, h, SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        Width = w / p.Scale;
        Height = h / p.Scale;
        int d = (int)Math.Round(p.Radius * 2 * p.Scale);
        SetWindowRgn(_hwnd, CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d), true);

        UpdateLayout();
        UpdateThumbnails();
    }

    /// <summary>Liga cada caixa à janela correspondente e põe a miniatura no lugar exato da caixa.</summary>
    private void UpdateThumbnails()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        foreach (var c in _cards)
        {
            if (c.Box.Child != null) continue; // minimizada: mostra o ícone
            try
            {
                if (c.Thumb == IntPtr.Zero && DwmRegisterThumbnail(_hwnd, c.Handle, out var thumb) == 0) c.Thumb = thumb;
                if (c.Thumb == IntPtr.Zero) continue;

                var origin = c.Box.TransformToAncestor(this).Transform(new Point(0, 0));
                double bx = origin.X * scale, by = origin.Y * scale, bw = c.Box.Width * scale, bh = c.Box.Height * scale;
                double sw = 0, sh = 0;
                if (DwmQueryThumbnailSourceSize(c.Thumb, out SIZE src) == 0) { sw = src.cx; sh = src.cy; }
                var (fx, fy, fw, fh) = PreviewLayout.Fit(sw, sh, bw, bh);

                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
                    rcDestination = new RECT
                    {
                        Left = (int)Math.Round(bx + fx), Top = (int)Math.Round(by + fy),
                        Right = (int)Math.Round(bx + fx + fw), Bottom = (int)Math.Round(by + fy + fh),
                    },
                    opacity = 255,
                    fVisible = true,
                    fSourceClientAreaOnly = false,
                };
                DwmUpdateThumbnailProperties(c.Thumb, ref props);
            }
            catch { /* sem miniatura: a caixa fica vazia, o título e o clique continuam */ }
        }
    }

    private void ClearCards()
    {
        foreach (var c in _cards)
        {
            if (c.Thumb != IntPtr.Zero)
            {
                try { DwmUnregisterThumbnail(c.Thumb); } catch { }
                c.Thumb = IntPtr.Zero;
            }
        }
        _cards.Clear();
        _panel.Children.Clear();
    }
}
