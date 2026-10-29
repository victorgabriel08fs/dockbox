using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Dockbox.Core;
using Dockbox.Services;
using Dockbox.Widgets;
using static Dockbox.Interop.NativeMethods;
using Path = System.IO.Path;

namespace Dockbox.Views;

public partial class DockWindow : Window
{
    // ---- Medidas fixas (DIPs) ----
    private const double Gap = 6;              // espaço entre ícones
    private const double Pad = 8;              // margem interna do fundo
    private const double EdgeMargin = 6;       // distância do fundo até a borda da tela
    private const double DotSize = 4.5;
    private const double SeparatorLength = Gap * 2 + 1;
    private const double Smoothing = 16;       // maior = segue o mouse mais rápido

    private static readonly Brush HitBrush = ColorUtil.Brush(Color.FromArgb(1, 0, 0, 0));

    private enum TileKind { App, Widget, Separator }

    private sealed class Tile
    {
        public TileKind Kind;
        public DockEntry? Entry;
        public DockWidget? Widget;
        public required FrameworkElement Visual;
        public Ellipse? Dot;
        public string? ExeKey;
        public bool Running;
        public bool Pressed;
        public double Scale = 1;
        public double Main, Size;       // posição/tamanho atuais no eixo do Dock
        public double BounceStart = -1; // segundos do relógio interno; -1 = sem pulo
        public string Label => Entry?.Name ?? Widget?.Label ?? "";
    }

    private readonly List<Tile> _tiles = new();
    private readonly Dictionary<string, (DockWidget Widget, Viewbox Box)> _widgetCache = new();
    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _slowTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private bool _animating;
    private bool _hovering;
    private bool _menuOpen;
    private double _dockStart, _dockLength; // no eixo principal
    private DockTheme _theme = DockTheme.From(new Appearance());

    private Appearance A => SettingsStore.Current.Appearance;
    private double BaseSize => A.IconSize;
    private double MaxScale => A.Magnify ? A.Magnification : 1.0;
    private double InfluenceRadius => BaseSize * 3.0;
    private bool Horizontal => A.Edge is DockEdge.Bottom or DockEdge.Top;
    private double BackgroundThickness => BaseSize + Pad * 2;

    public DockWindow()
    {
        InitializeComponent();
        PositionWindow();

        // Parado: confere o mouse 20x por segundo. Com o mouse no Dock: anima a cada frame.
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _idleTimer.Tick += (_, _) => { if (IsMouseInDock()) StartAnimating(); };

        // Widgets e indicadores de apps abertos
        _slowTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _slowTimer.Tick += async (_, _) => await SlowTick();

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) =>
        {
            ApplySettings();
            _idleTimer.Start();
            _slowTimer.Start();
            _ = SlowTick();
        };
        SizeChanged += (_, _) => Layout();

        DragOver += OnDragOver;
        Drop += OnDrop;
        MouseRightButtonUp += OnRightClick;

        SettingsStore.Changed += ApplySettings;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(PositionWindow);
        };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, _) =>
        {
            if (A.Theme == ThemeMode.Auto) Dispatcher.BeginInvoke(ApplyTheme);
        };
    }

    // ================= Janela: sem foco, fora do Alt+Tab =================

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        return IntPtr.Zero;
    }

    /// <summary>Espessura da janela (perpendicular à borda): ícone ampliado + pulo + rótulo.</summary>
    private double WindowThickness =>
        EdgeMargin + Pad * 2 + BaseSize * Math.Max(MaxScale, 1.0) + BaseSize * 0.5 + (Horizontal ? 40 : 240);

    private void PositionWindow()
    {
        Rect wa = SystemParameters.WorkArea; // monitor principal, em DIPs
        double t = WindowThickness;
        switch (A.Edge)
        {
            case DockEdge.Bottom: Left = wa.Left; Top = wa.Bottom - t; Width = wa.Width; Height = t; break;
            case DockEdge.Top: Left = wa.Left; Top = wa.Top; Width = wa.Width; Height = t; break;
            case DockEdge.Left: Left = wa.Left; Top = wa.Top; Width = t; Height = wa.Height; break;
            case DockEdge.Right: Left = wa.Right - t; Top = wa.Top; Width = t; Height = wa.Height; break;
        }
        Layout();
    }

    // ================= Configurações → Dock =================

    private void ApplySettings()
    {
        ApplyTheme();
        RebuildTiles();
        PositionWindow();
        StartAnimating();
    }

    private void ApplyTheme()
    {
        _theme = DockTheme.From(A);
        App.ApplyUiTheme(_theme);

        DockBackground.Background = _theme.Background;
        DockBackground.BorderBrush = _theme.Border;
        DockBackground.CornerRadius = new CornerRadius(A.CornerRadius);
        DockRim.BorderBrush = _theme.InnerHighlight;
        DockRim.CornerRadius = new CornerRadius(Math.Max(0, A.CornerRadius - 1));
        DockShadow.Opacity = _theme.ShadowOpacity * Math.Max(0.35, A.Opacity);
        DockShadow.Direction = A.Edge switch
        {
            DockEdge.Left => 0,
            DockEdge.Right => 180,
            _ => 270,
        };

        NameLabel.Background = _theme.LabelBackground;
        NameLabel.BorderBrush = _theme.LabelBorder;
        NameLabelText.Foreground = _theme.LabelForeground;

        foreach (var t in _tiles)
        {
            if (t.Dot != null) t.Dot.Fill = _theme.Dot;
            if (t.Kind == TileKind.Separator && t.Visual is Rectangle r) r.Fill = _theme.Separator;
        }
        foreach (var (w, _) in _widgetCache.Values) w.SetTheme(_theme);
    }

    private void RebuildTiles()
    {
        var runningKeys = new HashSet<string>(_tiles.Where(t => t.Running && t.ExeKey != null).Select(t => t.ExeKey!));
        foreach (var t in _tiles)
        {
            DockCanvas.Children.Remove(t.Visual);
            if (t.Dot != null) DockCanvas.Children.Remove(t.Dot);
        }
        _tiles.Clear();

        var s = SettingsStore.Current;
        foreach (var entry in s.Items)
            _tiles.Add(CreateAppTile(entry));

        var widgets = new List<Tile>();
        foreach (var cfg in s.Widgets.Where(w => w.Enabled))
        {
            var t = CreateWidgetTile(cfg.Kind);
            if (t != null && t.Widget!.IsAvailable) widgets.Add(t);
        }

        if (widgets.Count > 0 && _tiles.Count > 0 && A.ShowSeparator)
        {
            _tiles.Add(new Tile
            {
                Kind = TileKind.Separator,
                Visual = new Rectangle { Fill = _theme.Separator, IsHitTestVisible = false },
            });
        }
        _tiles.AddRange(widgets);

        // Widgets desligados: libera recursos
        var enabled = new HashSet<string>(s.Widgets.Where(w => w.Enabled).Select(w => w.Kind));
        foreach (var kind in _widgetCache.Keys.Where(k => !enabled.Contains(k)).ToList())
        {
            _widgetCache[kind].Widget.Dispose();
            _widgetCache.Remove(kind);
        }

        foreach (var t in _tiles)
        {
            if (t.ExeKey != null && runningKeys.Contains(t.ExeKey)) t.Running = true;
            DockCanvas.Children.Add(t.Visual);
            if (t.Dot != null) DockCanvas.Children.Add(t.Dot);
        }
        Layout();
    }

    private Tile CreateAppTile(DockEntry entry)
    {
        if (!_iconCache.TryGetValue(entry.Path, out var icon))
        {
            icon = IconLoader.Load(entry.Path);
            _iconCache[entry.Path] = icon;
        }

        var img = new Image { Source = icon, Stretch = Stretch.Uniform, Cursor = Cursors.Hand };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var tile = new Tile
        {
            Kind = TileKind.App,
            Entry = entry,
            Visual = img,
            ExeKey = ShellLinks.ExecutableKey(entry.Path),
            Dot = new Ellipse { Width = DotSize, Height = DotSize, Fill = _theme.Dot, IsHitTestVisible = false, Visibility = Visibility.Collapsed },
        };
        HookClicks(tile);
        return tile;
    }

    private Tile? CreateWidgetTile(string kind)
    {
        if (!_widgetCache.TryGetValue(kind, out var cached))
        {
            var w = WidgetFactory.Create(kind);
            if (w == null) return null;
            w.SetTheme(_theme);
            w.AvailabilityChanged += () => Dispatcher.BeginInvoke(RebuildTiles);
            var box = new Viewbox { Stretch = Stretch.Uniform, Child = w.Visual, Cursor = Cursors.Hand };
            cached = (w, box);
            _widgetCache[kind] = cached;
        }

        var tile = new Tile { Kind = TileKind.Widget, Widget = cached.Widget, Visual = cached.Box };
        HookClicks(tile);
        return tile;
    }

    private void HookClicks(Tile tile)
    {
        var v = tile.Visual;
        // Remove handlers antigos (o Viewbox de widget é reaproveitado entre reconstruções)
        if (v.Tag is (MouseButtonEventHandler down, MouseButtonEventHandler up, MouseEventHandler leave))
        {
            v.MouseLeftButtonDown -= down;
            v.MouseLeftButtonUp -= up;
            v.MouseLeave -= leave;
        }

        MouseButtonEventHandler d = (_, e) => { tile.Pressed = true; StartAnimating(); e.Handled = true; };
        MouseButtonEventHandler u = (_, e) =>
        {
            bool was = tile.Pressed;
            tile.Pressed = false;
            StartAnimating();
            e.Handled = true;
            if (was) OnTileClicked(tile);
        };
        MouseEventHandler l = (_, _) => { tile.Pressed = false; StartAnimating(); };

        v.MouseLeftButtonDown += d;
        v.MouseLeftButtonUp += u;
        v.MouseLeave += l;
        v.Tag = (d, u, l);
    }

    // ================= Ações =================

    private void OnTileClicked(Tile tile)
    {
        switch (tile.Kind)
        {
            case TileKind.App:
                bool newInstance = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                if (!newInstance && tile.ExeKey != null && tile.Running && RunningApps.ToggleWindows(tile.ExeKey))
                    return;
                if (AppLauncher.Launch(tile.Entry!) && A.BounceOnLaunch && !tile.Running)
                    Bounce(tile);
                break;

            case TileKind.Widget:
                tile.Widget!.OnClick(tile.Visual);
                break;
        }
    }

    private void Bounce(Tile tile)
    {
        tile.BounceStart = _clock.Elapsed.TotalSeconds;
        StartAnimating();
    }

    private void RemoveEntry(DockEntry entry)
    {
        SettingsStore.Current.Items.Remove(entry);
        SettingsStore.Commit();
    }

    private void DisableWidget(string kind)
    {
        var cfg = SettingsStore.Current.Widgets.FirstOrDefault(w => w.Kind == kind);
        if (cfg != null) cfg.Enabled = false;
        SettingsStore.Commit();
    }

    private int _tickCount;

    private async Task SlowTick()
    {
        // Some quando um vídeo, jogo ou apresentação está em tela cheia.
        bool fullscreen = SHQueryUserNotificationState(out int qs) == 0 && qs is 2 or 3 or 4;
        if (fullscreen && IsVisible && !_hovering && !_menuOpen) Hide();
        else if (!fullscreen && !IsVisible) Show();

        // De vez em quando reafirma "sempre no topo" (a barra de tarefas às vezes passa por cima).
        if (++_tickCount % 3 == 0 && !_menuOpen && !fullscreen)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        // Indicadores de apps abertos
        if (A.ShowRunning)
        {
            Dictionary<string, List<IntPtr>> snap;
            try { snap = RunningApps.Snapshot(); }
            catch { snap = new(); }
            bool changed = false;
            foreach (var t in _tiles.Where(t => t.Kind == TileKind.App))
            {
                bool running = t.ExeKey != null && snap.ContainsKey(t.ExeKey);
                if (running != t.Running) { t.Running = running; changed = true; }
            }
            if (changed && !_animating) Layout();
        }

        // Widgets
        var now = DateTime.Now;
        foreach (var t in _tiles.Where(t => t.Kind == TileKind.Widget).ToList())
        {
            var w = t.Widget!;
            if (now < w.NextUpdate) continue;
            w.NextUpdate = now + w.Interval;
            try { await w.UpdateAsync(); }
            catch { /* um widget com erro não derruba o Dock */ }
        }
        if (_hovering) UpdateLabel(GetMouseDock());
    }

    // ================= Animação =================

    private void StartAnimating()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = _clock.Elapsed.TotalSeconds;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopAnimating()
    {
        if (!_animating) return;
        _animating = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - _lastFrame, 0, 0.1);
        _lastFrame = now;

        var mouse = GetMouseDock();
        bool wasHovering = _hovering;
        _hovering = !_menuOpen && mouse is (double mm, double mc) && IsInside(mm, mc, _hovering);
        if (_menuOpen) _hovering = wasHovering;

        // Distâncias medidas nas posições "em repouso" para o efeito não oscilar.
        var restCenters = RestCenters();
        double k = 1 - Math.Exp(-dt * Smoothing);
        bool settled = true;

        for (int i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            if (t.Kind == TileKind.Separator) continue;

            double target = 1;
            if (_hovering && mouse is (double main, double) && MaxScale > 1.001)
            {
                double d = Math.Abs(main - restCenters[i]) / InfluenceRadius;
                if (d < 1) target = 1 + (MaxScale - 1) * (Math.Cos(Math.PI * d) + 1) / 2;
            }

            t.Scale += (target - t.Scale) * k;
            if (Math.Abs(target - t.Scale) > 0.002) settled = false;
            else t.Scale = target;

            if (t.BounceStart >= 0)
            {
                if (now - t.BounceStart > BounceDuration) t.BounceStart = -1;
                else settled = false;
            }
        }

        Layout();
        UpdateLabel(mouse);

        if (settled && !_hovering) StopAnimating();
    }

    private const double BounceDuration = 1.4;

    private double BounceOffset(Tile t)
    {
        if (t.BounceStart < 0) return 0;
        double time = _clock.Elapsed.TotalSeconds - t.BounceStart;
        if (time > BounceDuration) return 0;
        // dois pulos, o segundo menor
        double phase = time / BounceDuration * 2;
        double height = phase < 1 ? 1 : 0.45;
        return Math.Abs(Math.Sin(phase * Math.PI)) * height * BaseSize * 0.42;
    }

    private double MainLength => Horizontal ? W : H;
    private double W => double.IsNaN(Width) ? ActualWidth : Width;
    private double H => double.IsNaN(Height) ? ActualHeight : Height;

    private double TileRestLength(Tile t) => t.Kind == TileKind.Separator ? SeparatorLength : BaseSize;

    private double[] RestCenters()
    {
        var centers = new double[_tiles.Count];
        double total = _tiles.Sum(TileRestLength) + Math.Max(0, _tiles.Count - 1) * Gap + Pad * 2;
        double x = (MainLength - total) / 2 + Pad;
        for (int i = 0; i < _tiles.Count; i++)
        {
            double len = TileRestLength(_tiles[i]);
            centers[i] = x + len / 2;
            x += len + Gap;
        }
        return centers;
    }

    /// <summary>Posiciona fundo, ícones e indicadores conforme a escala atual.</summary>
    private void Layout()
    {
        if (W <= 0 || H <= 0) return;
        double b = BaseSize;

        double content = 0;
        foreach (var t in _tiles) content += t.Kind == TileKind.Separator ? SeparatorLength : b * t.Scale;
        content += Math.Max(0, _tiles.Count - 1) * Gap;
        if (_tiles.Count == 0) content = b;

        _dockLength = content + Pad * 2;
        _dockStart = Math.Round((MainLength - _dockLength) / 2);

        PlaceRect(DockBackground, _dockStart, EdgeMargin, _dockLength, BackgroundThickness);

        double stayDepth = EdgeMargin + Pad * 2 + b * MaxScale + 10;
        PlaceRect(HitArea, _dockStart, 0, _dockLength, stayDepth);
        HitArea.Fill = _hovering ? HitBrush : Brushes.Transparent;

        double x = _dockStart + Pad;
        double baseCross = EdgeMargin + Pad;
        foreach (var t in _tiles)
        {
            if (t.Kind == TileKind.Separator)
            {
                double len = b * 0.72;
                PlaceRect(t.Visual, x + Gap, baseCross + (b - len) / 2, 1, len);
                t.Main = x; t.Size = SeparatorLength;
                x += SeparatorLength + Gap;
                continue;
            }

            double size = b * t.Scale;
            double press = t.Pressed ? size * 0.08 : 0;
            double lift = BounceOffset(t);
            t.Main = x;
            t.Size = size;
            PlaceRect(t.Visual, x + press / 2, baseCross + lift + press / 2, size - press, size - press);
            t.Visual.Opacity = t.Pressed ? 0.75 : 1;

            if (t.Dot != null)
            {
                bool show = A.ShowRunning && t.Running;
                t.Dot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) PlaceRect(t.Dot, x + size / 2 - DotSize / 2, EdgeMargin + 1.5, DotSize, DotSize);
            }
            x += size + Gap;
        }
    }

    /// <summary>
    /// Converte um retângulo nas coordenadas do Dock (eixo principal + distância da borda da tela)
    /// para coordenadas do Canvas, conforme a borda escolhida.
    /// </summary>
    private void PlaceRect(FrameworkElement e, double main, double cross, double mainLen, double crossLen)
    {
        PlacePos(e, main, cross, mainLen, crossLen);
        if (Horizontal) { e.Width = Math.Max(0, mainLen); e.Height = Math.Max(0, crossLen); }
        else { e.Width = Math.Max(0, crossLen); e.Height = Math.Max(0, mainLen); }
    }

    private void PlacePos(UIElement e, double main, double cross, double mainLen, double crossLen)
    {
        switch (A.Edge)
        {
            case DockEdge.Bottom: Canvas.SetLeft(e, main); Canvas.SetTop(e, H - cross - crossLen); break;
            case DockEdge.Top: Canvas.SetLeft(e, main); Canvas.SetTop(e, cross); break;
            case DockEdge.Left: Canvas.SetLeft(e, cross); Canvas.SetTop(e, main); break;
            case DockEdge.Right: Canvas.SetLeft(e, W - cross - crossLen); Canvas.SetTop(e, main); break;
        }
    }

    private Tile? TileAt(double main) =>
        _tiles.FirstOrDefault(t => t.Kind != TileKind.Separator && main >= t.Main - Gap / 2 && main < t.Main + t.Size + Gap / 2);

    private void UpdateLabel((double main, double cross)? mouse)
    {
        Tile? hot = null;
        if (A.ShowLabels && _hovering && !_menuOpen && mouse is (double main, double)) hot = TileAt(main);

        if (hot == null || string.IsNullOrEmpty(hot.Label))
        {
            NameLabel.Visibility = Visibility.Collapsed;
            return;
        }

        NameLabelText.Text = hot.Label;
        NameLabel.Visibility = Visibility.Visible;
        NameLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = NameLabel.DesiredSize;
        double mainLen = Horizontal ? sz.Width : sz.Height;
        double crossLen = Horizontal ? sz.Height : sz.Width;

        double cross = EdgeMargin + Pad + hot.Size + BounceOffset(hot) + 10;
        double main0 = hot.Main + hot.Size / 2 - mainLen / 2;
        main0 = Math.Clamp(main0, 4, Math.Max(4, MainLength - mainLen - 4));
        PlacePos(NameLabel, Math.Round(main0), Math.Round(cross), mainLen, crossLen);
    }

    // ================= Mouse =================

    /// <summary>Cursor nas coordenadas do Dock: eixo principal e distância até a borda da tela.</summary>
    private (double main, double cross)? GetMouseDock()
    {
        if (PresentationSource.FromVisual(this) == null) return null;
        if (!GetCursorPos(out POINT pt)) return null;
        Point p;
        try { p = PointFromScreen(new Point(pt.X, pt.Y)); }
        catch (InvalidOperationException) { return null; }

        return A.Edge switch
        {
            DockEdge.Bottom => (p.X, H - p.Y),
            DockEdge.Top => (p.X, p.Y),
            DockEdge.Left => (p.Y, p.X),
            _ => (p.Y, W - p.X),
        };
    }

    private bool IsMouseInDock() => GetMouseDock() is (double m, double c) && IsInside(m, c, false);

    /// <summary>
    /// Para entrar, o mouse precisa estar sobre o fundo do Dock; uma vez dentro, pode ir
    /// até a ponta dos ícones ampliados sem sair.
    /// </summary>
    private bool IsInside(double main, double cross, bool alreadyInside)
    {
        double depth = alreadyInside
            ? EdgeMargin + Pad * 2 + BaseSize * MaxScale + 10
            : EdgeMargin + BackgroundThickness;
        return main >= _dockStart && main <= _dockStart + _dockLength && cross >= -2 && cross <= depth;
    }

    // ================= Menus =================

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var mouse = GetMouseDock();
        var tile = mouse is (double main, double) ? TileAt(main) : null;

        var menu = new ContextMenu();
        if (tile?.Kind == TileKind.App)
        {
            var entry = tile.Entry!;
            menu.Items.Add(Header(entry.Name));
            menu.Items.Add(Item(tile.Running ? "Mostrar" : "Abrir", () => OnTileClicked(tile)));
            if (tile.Running) menu.Items.Add(Item("Nova janela", () => AppLauncher.Launch(entry)));
            menu.Items.Add(Item("Mostrar no Explorador", () => AppLauncher.ShowInExplorer(entry.Path)));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("Remover do Dock", () => RemoveEntry(entry)));
        }
        else if (tile?.Kind == TileKind.Widget)
        {
            var w = tile.Widget!;
            menu.Items.Add(Header(w.DisplayName));
            foreach (var (h, a) in w.MenuItems()) menu.Items.Add(Item(h, a));
            menu.Items.Add(Item("Remover widget", () => DisableWidget(w.Kind)));
        }

        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        menu.Items.Add(Item("Gerenciar Dock...", () => SettingsWindow.ShowSingle()));
        menu.Items.Add(Item(A.Magnify ? "Desligar ampliação" : "Ligar ampliação", () =>
        {
            A.Magnify = !A.Magnify;
            SettingsStore.Commit();
        }));
        menu.Items.Add(new Separator());
        menu.Items.Add(Header("Posição na tela"));
        foreach (var (edge, name) in new[] { (DockEdge.Bottom, "Embaixo"), (DockEdge.Left, "À esquerda"), (DockEdge.Right, "À direita"), (DockEdge.Top, "Em cima") })
        {
            var mi = Item(name, () => { A.Edge = edge; SettingsStore.Commit(); });
            mi.IsCheckable = false;
            mi.IsChecked = A.Edge == edge;
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Sair do Dockbox", Close));

        menu.PlacementTarget = this;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.Opened += (_, _) => { _menuOpen = true; NameLabel.Visibility = Visibility.Collapsed; };
        menu.Closed += (_, _) => { _menuOpen = false; StartAnimating(); };
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, Action action)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => action();
        return mi;
    }

    private static MenuItem Header(string text) => new()
    {
        Header = text,
        IsEnabled = false,
        FontWeight = FontWeights.SemiBold,
    };

    // ================= Arrastar e soltar =================

    private Tile? DropTarget(out double main)
    {
        main = 0;
        if (GetMouseDock() is not (double m, double)) return null;
        main = m;
        return TileAt(m);
    }

    private static bool IsPinnable(string f) =>
        Directory.Exists(f) || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
        f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files)
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        StartAnimating();
        var t = DropTarget(out _);
        if (t?.Widget is { AcceptsFiles: true }) e.Effects = DragDropEffects.Move;
        else if (t?.Kind == TileKind.App && !files.All(IsPinnable)) e.Effects = DragDropEffects.Copy;
        else e.Effects = DragDropEffects.Link;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        e.Handled = true;
        var t = DropTarget(out double main);

        if (t?.Widget is { AcceptsFiles: true } w)
        {
            w.OnFilesDropped(files);
            return;
        }
        if (t?.Kind == TileKind.App && !files.All(IsPinnable))
        {
            // Soltar arquivos num app abre os arquivos com ele (como no macOS)
            if (AppLauncher.Launch(t.Entry!, files) && A.BounceOnLaunch) Bounce(t);
            return;
        }

        // Fixar no Dock, na posição em que foi solto
        var items = SettingsStore.Current.Items;
        int index = _tiles.Count(x => x.Kind == TileKind.App && x.Main + x.Size / 2 < main);
        index = Math.Clamp(index, 0, items.Count);
        foreach (var f in files)
        {
            string name = Directory.Exists(f) ? new DirectoryInfo(f).Name : Path.GetFileNameWithoutExtension(f);
            items.Insert(index++, new DockEntry { Name = name, Path = f });
        }
        SettingsStore.Commit();
    }
}
