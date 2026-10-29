using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dockbox.Core;
using Dockbox.Services;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Views;

/// <summary>
/// Pilha de uma pasta fixada: o conteúdo em leque, grade ou lista, aberto ao clicar na pasta no Dock.
/// Clicar num item abre; numa subpasta (grade e lista) entra nela. Arrastar um item leva o arquivo para
/// outro app. Esc, clicar fora ou abrir um item fecha. O conteúdo se atualiza sozinho quando a pasta muda.
/// </summary>
internal sealed class StackWindow : Window
{
    private const double GridCell = 88, GridIcon = 48, ListWidth = 320, ListIcon = 20, FanIcon = 48, FanRow = 58, FanLabelMax = 230;
    private const double DragThreshold = 6;

    private readonly DockEntry _entry;
    private readonly string _root;
    private readonly DockTheme _theme;
    private readonly double _radius;
    private readonly DockEdge _edge;
    private readonly RECT _screen;
    private readonly double _scale;
    private readonly int _anchorX, _anchorY;
    private readonly Stack<string> _history = new();
    private string _folder;
    private IntPtr _hwnd;
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _reloadTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private int _generation;          // muda a cada recarga: ícones atrasados de uma lista antiga são ignorados
    private bool _dragging;
    private Point _pressPoint;
    private StackItem? _pressItem;
    private ScrollViewer? _scroll;

    /// <summary>True depois de fechar (fechar de novo não faz nada).</summary>
    public bool IsClosed { get; private set; }

    /// <summary>Fecha, se ainda não fechou (clicar fora e abrir um item podem fechar quase juntos).</summary>
    public void CloseOnce()
    {
        if (!IsClosed) Close();
    }

    /// <summary>Caminho da pasta mostrada na raiz (a pasta fixada).</summary>
    public string Folder => _root;
    /// <summary>Jeito como a pilha está aparecendo agora (depois de resolver o automático).</summary>
    public StackView ShownView { get; private set; }
    /// <summary>Itens mostrados agora, na ordem.</summary>
    public IReadOnlyList<StackItem> Items { get; private set; } = Array.Empty<StackItem>();

    public StackWindow(DockEntry entry, DockTheme theme, double cornerRadius, int anchorX, int anchorY, DockEdge edge, RECT screen, double scale)
    {
        _entry = entry;
        _root = _folder = Environment.ExpandEnvironmentVariables(entry.Path);
        _theme = theme;
        _radius = Math.Clamp(cornerRadius, 6, 18);
        _anchorX = anchorX;
        _anchorY = anchorY;
        _edge = edge;
        _screen = screen;
        _scale = Math.Max(0.5, scale);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = Height = 1;
        Left = Top = -32000;
        AutomationProperties.SetName(this, entry.Name);

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW); // fora do Alt+Tab
        };
        Deactivated += (_, _) => { if (!_dragging && IsVisible) CloseOnce(); };
        PreviewKeyDown += OnKey;
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            Build(keepScroll: true);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Place);
        Closed += (_, _) =>
        {
            IsClosed = true;
            _reloadTimer.Stop();
            _watcher?.Dispose();
            _watcher = null;
        };
    }

    /// <summary>Mostra a pilha e põe o foco nela (para Esc e as setas funcionarem).</summary>
    public void Open()
    {
        Build(keepScroll: false);
        Show();
        Place();
        Activate();
        FocusFirst();
    }

    // ================= Conteúdo =================

    private void Navigate(string folder)
    {
        _history.Push(_folder);
        _folder = folder;
        Build(keepScroll: false);
        Place();
        FocusFirst();
    }

    private bool Back()
    {
        if (_history.Count == 0) return false;
        _folder = _history.Pop();
        Build(keepScroll: false);
        Place();
        FocusFirst();
        return true;
    }

    private void Build(bool keepScroll)
    {
        double offset = keepScroll && _scroll != null ? _scroll.VerticalOffset : 0;
        _generation++;
        Watch(_folder);

        var all = StackContent.Sort(StackContent.List(_folder), _entry.StackSort);
        bool inSub = _history.Count > 0;
        ShownView = StackContent.Resolve(_entry.StackView, all.Count, _edge, inSub);
        int limit = ShownView == StackView.Fan ? StackContent.FanMax : StackContent.MaxItems;
        Items = all.Take(limit).ToList();
        int hidden = all.Count - Items.Count;

        Content = ShownView == StackView.Fan ? BuildFan() : BuildPanel(hidden);
        if (_scroll != null && offset > 0) _scroll.ScrollToVerticalOffset(offset);
    }

    /// <summary>Grade ou lista dentro de um painel com título, rolagem e "Abrir no Explorador".</summary>
    private FrameworkElement BuildPanel(int hidden)
    {
        var fg = _theme.LabelForeground;
        var dock = new DockPanel { LastChildFill = true };

        // Título (e voltar, dentro de subpastas)
        var header = new DockPanel { Margin = new Thickness(4, 0, 4, 8) };
        if (_history.Count > 0)
        {
            var back = LinkButton("‹", Loc.T("Stack_Back"), () => Back());
            back.FontSize = 18;
            back.Margin = new Thickness(0, -3, 8, 0);
            DockPanel.SetDock(back, Dock.Left);
            header.Children.Add(back);
        }
        header.Children.Add(new TextBlock
        {
            Text = _history.Count > 0 ? Path.GetFileName(_folder.TrimEnd('\\')) : _entry.Name,
            Foreground = fg, FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
        });
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);

        // Rodapé
        var footer = new DockPanel { Margin = new Thickness(4, 8, 4, 0) };
        var open = LinkButton(Loc.T("Stack_OpenInExplorer"), null, OpenFolderInExplorer);
        DockPanel.SetDock(open, Dock.Right);
        footer.Children.Add(open);
        if (hidden > 0)
            footer.Children.Add(new TextBlock { Text = Loc.F("Stack_More", hidden), Foreground = fg, Opacity = 0.7, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);

        // Itens
        FrameworkElement body;
        if (Items.Count == 0)
        {
            body = new TextBlock { Text = Loc.T("Stack_Empty"), Foreground = fg, Opacity = 0.7, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center };
        }
        else
        {
            Panel panel;
            if (ShownView == StackView.List)
            {
                panel = new StackPanel { Width = ListWidth };
                foreach (var item in Items) panel.Children.Add(ListRow(item));
            }
            else
            {
                int cols = Math.Clamp((int)Math.Ceiling(Math.Sqrt(Items.Count * 1.6)), 3, 7);
                panel = new WrapPanel { Width = cols * GridCell };
                foreach (var item in Items) panel.Children.Add(GridCellFor(item));
            }
            KeyboardNavigation.SetDirectionalNavigation(panel, KeyboardNavigationMode.Cycle);
            double maxH = (_screen.Bottom - _screen.Top) / _scale * 0.6;
            _scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = maxH, Focusable = false };
            body = _scroll;
        }
        dock.Children.Add(body);

        return new Border
        {
            Child = dock, Padding = new Thickness(10), CornerRadius = new CornerRadius(_radius), BorderThickness = new Thickness(1),
            Background = _theme.LabelBackground, BorderBrush = _theme.LabelBorder,
        };
    }

    /// <summary>Leque: os itens sobem a partir do ícone numa curva, com o nome à esquerda (só com o Dock embaixo).</summary>
    private FrameworkElement BuildFan()
    {
        _scroll = null;
        var canvas = new Canvas();
        int rows = Items.Count + 1; // + "Abrir no Explorador" no topo
        double iconColumn = FanLabelMax + 10 + FanIcon / 2; // centro dos ícones na linha de baixo
        double width = iconColumn + FanIcon / 2 + 90;
        double height = rows * FanRow + 6;
        canvas.Width = width;
        canvas.Height = height;

        for (int i = 0; i < rows; i++)
        {
            bool explorer = i == Items.Count;
            var row = explorer ? FanRowFor(null) : FanRowFor(Items[i]);
            row.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double rowW = row.DesiredSize.Width;
            double k = rows <= 1 ? 0 : (double)i / (rows - 1);
            double dx = 70 * k * k;                 // curva para a direita conforme sobe
            double angle = 9 * k;                   // e inclina um pouco
            double cx = iconColumn + dx;
            double top = height - (i + 1) * FanRow;
            Canvas.SetLeft(row, cx - (rowW - FanIcon / 2));
            Canvas.SetTop(row, top);
            row.RenderTransform = new RotateTransform(angle, rowW - FanIcon / 2, FanRow / 2);
            canvas.Children.Add(row);
        }
        _fanIconColumn = iconColumn;
        return canvas;
    }

    private double _fanIconColumn;

    private FrameworkElement FanRowFor(StackItem? item)
    {
        string name = item?.Name ?? Loc.T("Stack_OpenInExplorer");
        var label = new Border
        {
            Background = _theme.LabelBackground, BorderBrush = _theme.LabelBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = FanLabelMax,
            Child = new TextBlock { Text = name, Foreground = _theme.LabelForeground, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        var img = new Image { Width = FanIcon, Height = FanIcon, VerticalAlignment = VerticalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        if (item != null) LoadIconLater(img, item.Path, FanIcon);
        else LoadIconLater(img, _folder, FanIcon);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Height = FanRow, Background = Brushes.Transparent };
        row.Children.Add(label);
        row.Children.Add(img);
        if (item != null) HookItem(row, item);
        else HookAction(row, name, OpenFolderInExplorer);
        return row;
    }

    private FrameworkElement GridCellFor(StackItem item)
    {
        var img = new Image { Width = GridIcon, Height = GridIcon, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        LoadIconLater(img, item.Path, GridIcon);
        var text = new TextBlock
        {
            Text = item.Name, Foreground = _theme.LabelForeground, FontSize = 11.5, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 32, Margin = new Thickness(0, 4, 0, 0),
        };
        var stack = new StackPanel();
        stack.Children.Add(img);
        stack.Children.Add(text);
        var cell = new Border { Width = GridCell - 4, Height = 96, Margin = new Thickness(2), Padding = new Thickness(4, 6, 4, 4), CornerRadius = new CornerRadius(8), Child = stack };
        HookItem(cell, item);
        return cell;
    }

    private FrameworkElement ListRow(StackItem item)
    {
        var img = new Image { Width = ListIcon, Height = ListIcon, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        LoadIconLater(img, item.Path, ListIcon);
        var dock = new DockPanel();
        DockPanel.SetDock(img, Dock.Left);
        dock.Children.Add(img);
        if (item.IsFolder)
        {
            var arrow = new TextBlock { Text = "›", Foreground = _theme.LabelForeground, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(arrow, Dock.Right);
            dock.Children.Add(arrow);
        }
        dock.Children.Add(new TextBlock { Text = item.Name, Foreground = _theme.LabelForeground, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var row = new Border { Height = 30, Padding = new Thickness(6, 0, 6, 0), CornerRadius = new CornerRadius(6), Child = dock };
        HookItem(row, item);
        return row;
    }

    /// <summary>Ícones carregados aos poucos, com prioridade baixa, para a pilha abrir na hora mesmo com muitos itens.</summary>
    private void LoadIconLater(Image img, string path, double dips)
    {
        int gen = _generation;
        int px = IconLoader.PixelsFor(dips, _scale);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (gen != _generation) return; // a pilha mudou de pasta ou recarregou
            try { img.Source = IconLoader.Load(path, Math.Max(px, 32)); } catch { }
        });
    }

    // ================= Interação =================

    private Brush HoverBrush => ColorUtil.Brush(_theme.IsLight ? Color.FromArgb(30, 0, 0, 0) : Color.FromArgb(40, 255, 255, 255));

    private void HookItem(FrameworkElement el, StackItem item)
    {
        el.Focusable = true;
        el.Cursor = Cursors.Hand;
        AutomationProperties.SetName(el, item.Name);
        AutomationProperties.SetHelpText(el, Loc.T(item.IsFolder && ShownView != StackView.Fan ? "Stack_FolderHelp" : "Stack_ItemHelp"));
        if (el is Border b) b.Background = Brushes.Transparent;
        el.MouseEnter += (_, _) => SetHover(el, true);
        el.MouseLeave += (_, _) => SetHover(el, el.IsKeyboardFocused);
        el.GotKeyboardFocus += (_, _) => SetHover(el, true);
        el.LostKeyboardFocus += (_, _) => SetHover(el, el.IsMouseOver);
        el.MouseLeftButtonDown += (_, e) =>
        {
            _pressItem = item;
            _pressPoint = e.GetPosition(this);
            e.Handled = true;
        };
        el.MouseMove += (_, e) =>
        {
            if (_pressItem != item || e.LeftButton != MouseButtonState.Pressed || _dragging) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - _pressPoint.X) < DragThreshold && Math.Abs(p.Y - _pressPoint.Y) < DragThreshold) return;
            StartDrag(el, item);
        };
        el.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            bool click = _pressItem == item && !_dragging;
            _pressItem = null;
            if (click) OpenItem(item);
        };
        el.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space)
            {
                e.Handled = true;
                OpenItem(item);
            }
        };
    }

    private void HookAction(FrameworkElement el, string name, Action action)
    {
        el.Focusable = true;
        el.Cursor = Cursors.Hand;
        AutomationProperties.SetName(el, name);
        el.MouseEnter += (_, _) => SetHover(el, true);
        el.MouseLeave += (_, _) => SetHover(el, el.IsKeyboardFocused);
        el.GotKeyboardFocus += (_, _) => SetHover(el, true);
        el.LostKeyboardFocus += (_, _) => SetHover(el, el.IsMouseOver);
        el.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
        el.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; action(); } };
    }

    private void SetHover(FrameworkElement el, bool on)
    {
        if (el is Border b) b.Background = on ? HoverBrush : Brushes.Transparent;
        else if (el is Panel p) p.Opacity = on ? 0.82 : 1;
    }

    private TextBlock LinkButton(string text, string? a11y, Action action)
    {
        var t = new TextBlock { Text = text, Foreground = ColorUtil.Brush(_theme.Accent), FontSize = 12.5, Cursor = Cursors.Hand, Focusable = true, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(t, a11y ?? text);
        t.MouseEnter += (_, _) => t.TextDecorations = TextDecorations.Underline;
        t.MouseLeave += (_, _) => t.TextDecorations = null;
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; action(); };
        t.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { e.Handled = true; action(); } };
        return t;
    }

    /// <summary>Clique ou Enter num item: subpasta abre dentro da pilha (grade e lista); o resto abre normalmente.</summary>
    private void OpenItem(StackItem item)
    {
        if (item.IsFolder && ShownView != StackView.Fan)
        {
            Navigate(item.Path);
            return;
        }
        CloseOnce();
        // Mesmas regras de segurança dos itens fixados (scripts e protocolos perguntam antes).
        AppLauncher.Launch(new DockEntry { Name = item.Name, Path = item.Path });
    }

    private void OpenFolderInExplorer()
    {
        CloseOnce();
        AppLauncher.Launch(new DockEntry { Name = _entry.Name, Path = _folder });
    }

    private void StartDrag(FrameworkElement el, StackItem item)
    {
        _dragging = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            DragDrop.DoDragDrop(el, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        catch { }
        finally
        {
            _dragging = false;
            _pressItem = null;
        }
        // Soltou em outro app: a pilha já cumpriu o papel.
        if (!IsActive) CloseOnce();
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (!Back()) CloseOnce();
        }
        else if (e.Key == Key.Back)
        {
            e.Handled = true;
            Back();
        }
    }

    private void FocusFirst()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            var first = FindFocusable(Content as DependencyObject);
            first?.Focus();
        });
    }

    private static UIElement? FindFocusable(DependencyObject? root)
    {
        if (root == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement { Focusable: true, IsVisible: true } u && AutomationProperties.GetHelpText(u).Length > 0) return u;
            if (FindFocusable(child) is UIElement found) return found;
        }
        return null;
    }

    // ================= Atualização ao vivo =================

    private void Watch(string folder)
    {
        if (_watcher?.Path.Equals(folder, StringComparison.OrdinalIgnoreCase) == true) return;
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            var w = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
            };
            FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(ScheduleReload);
            w.Created += changed;
            w.Deleted += changed;
            w.Changed += changed;
            w.Renamed += (_, _) => Dispatcher.BeginInvoke(ScheduleReload);
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
        catch { /* pasta de rede ou sem permissão: sem atualização ao vivo */ }
    }

    private void ScheduleReload()
    {
        if (!IsVisible) return;
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    // ================= Posição =================

    /// <summary>Encosta a pilha no ícone, do lado de dentro da tela, como o painel de miniaturas.</summary>
    private void Place()
    {
        if (_hwnd == IntPtr.Zero || Content is not FrameworkElement root) return;
        root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = root.DesiredSize;
        int w = (int)Math.Ceiling(size.Width * _scale), h = (int)Math.Ceiling(size.Height * _scale);

        int left, top;
        if (ShownView == StackView.Fan)
        {
            // A coluna dos ícones começa em cima do ícone da pasta.
            left = _anchorX - (int)Math.Round(_fanIconColumn * _scale);
            top = _anchorY - h;
        }
        else
        {
            switch (_edge)
            {
                case DockEdge.Top: left = _anchorX - w / 2; top = _anchorY; break;
                case DockEdge.Left: left = _anchorX; top = _anchorY - h / 2; break;
                case DockEdge.Right: left = _anchorX - w; top = _anchorY - h / 2; break;
                default: left = _anchorX - w / 2; top = _anchorY - h; break;
            }
        }
        int margin = (int)Math.Round(8 * _scale);
        var s = _screen;
        left = Math.Clamp(left, s.Left + margin, Math.Max(s.Left + margin, s.Right - margin - w));
        top = Math.Clamp(top, s.Top + margin, Math.Max(s.Top + margin, s.Bottom - margin - h));
        SetWindowPos(_hwnd, HWND_TOPMOST, left, top, w, h, SWP_NOOWNERZORDER);
        Width = w / _scale;
        Height = h / _scale;
    }
}
