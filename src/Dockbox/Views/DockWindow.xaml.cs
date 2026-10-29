using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        public double GapBefore;        // espaço aberto antes do ícone (arrastar), animado
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
    private AppBar? _appBar;
    private bool _positioning;

    // ---- Auto-ocultar ----
    private const double EdgeTrigger = 3;      // faixa (DIPs) junto à borda que revela o Dock
    private const double HideDelay = 0.35;     // segundos depois que o mouse sai
    private bool _shown = true;                // estado desejado: visível ou escondido
    private double _reveal = 1;                // animado: 1 = visível, 0 = fora da tela
    private double _edgeSince = -1;            // desde quando o mouse está na borda
    private double _leaveSince = -1;           // desde quando o mouse saiu do Dock
    private readonly TranslateTransform _slide = new();

    // ---- Arrastar ícones dentro do Dock ----
    private const double DragThreshold = 6;    // DIPs antes de virar arraste (clique continua clique)
    private Tile? _pressTile;                  // ícone com o botão apertado
    private (double main, double cross) _pressPoint;
    private Tile? _dragTile;                   // ícone sendo arrastado
    private int _dragSlot;                     // posição entre os apps onde ele vai entrar
    private bool _dragRemove;                  // solto aqui, sai do Dock
    private int _externalSlot = -1;            // arquivo vindo de fora: onde vai entrar (-1 = nenhum)
    private double _gapEnd;                    // espaço aberto depois do último app

    private Appearance A => SettingsStore.Current.Appearance;
    private double BaseSize => A.IconSize;
    private double MaxScale => A.Magnify ? A.Magnification : 1.0;
    private double InfluenceRadius => BaseSize * 3.0;
    private bool Horizontal => A.Edge is DockEdge.Bottom or DockEdge.Top;
    private double BackgroundThickness => BaseSize + Pad * 2;

    public DockWindow()
    {
        InitializeComponent();
        DockCanvas.RenderTransform = _slide;
        PositionWindow();

        // Parado: confere o mouse 20x por segundo. Com o mouse no Dock: anima a cada frame.
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _idleTimer.Tick += (_, _) => { if (ShouldWake()) StartAnimating(); };

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
        DragLeave += (_, _) => { _externalSlot = -1; StartAnimating(); };
        Drop += OnDrop;
        MouseMove += OnMouseMoveDrag;
        MouseLeftButtonUp += OnMouseUpDrag;
        LostMouseCapture += (_, _) => CancelPress();
        MouseRightButtonUp += OnRightClick;

        SettingsStore.Changed += ApplySettings;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            // Com a AppBar ativa, a reserva só é refeita se a borda ou a espessura mudaram (ver ReserveArea).
            if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(() => PositionWindow());
        };
        Closed += (_, _) => ReleaseAppBar();
        // Se o processo cair, devolve a área de trabalho inteira.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseAppBar();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseAppBar();
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
        _appBar = new AppBar(hwnd);
        PositionWindow();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(MA_NOACTIVATE);
        }
        if (_appBar != null && msg == _appBar.CallbackMessage)
        {
            // Outra AppBar (ex.: barra de tarefas) mudou de lugar ou tamanho: recalcula a reserva.
            if (wParam.ToInt64() == ABN_POSCHANGED) Dispatcher.BeginInvoke(() => PositionWindow(force: true));
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void ReleaseAppBar()
    {
        try { _appBar?.Remove(); } catch { }
    }

    /// <summary>Espessura da janela (perpendicular à borda): ícone ampliado + pulo + rótulo.</summary>
    private double WindowThickness =>
        EdgeMargin + Pad * 2 + BaseSize * Math.Max(MaxScale, 1.0) + BaseSize * 0.5 + (Horizontal ? 40 : 240);

    /// <summary>Faixa reservada na tela: o fundo do Dock com uma margem dos dois lados.</summary>
    private double ReservedThickness => EdgeMargin * 2 + BackgroundThickness;

    private void PositionWindow() => PositionWindow(force: false);

    private void PositionWindow(bool force)
    {
        if (_positioning) return;
        _positioning = true;
        try
        {
            // Área onde a janela encosta, em DIPs. Com a AppBar, é a faixa reservada; sem ela, a área de trabalho.
            Rect area = ReserveArea(force) ?? SystemParameters.WorkArea;
            double t = WindowThickness;
            switch (A.Edge)
            {
                case DockEdge.Bottom: Left = area.Left; Top = area.Bottom - t; Width = area.Width; Height = t; break;
                case DockEdge.Top: Left = area.Left; Top = area.Top; Width = area.Width; Height = t; break;
                case DockEdge.Left: Left = area.Left; Top = area.Top; Width = t; Height = area.Height; break;
                case DockEdge.Right: Left = area.Right - t; Top = area.Top; Width = t; Height = area.Height; break;
            }
            Layout();
        }
        finally
        {
            _positioning = false;
        }
    }

    /// <summary>
    /// Reserva (ou libera) a faixa do Dock na tela. Devolve a faixa em DIPs, ou null quando
    /// não há reserva e a janela deve usar a área de trabalho normal.
    /// </summary>
    private Rect? ReserveArea(bool force)
    {
        if (_appBar == null) return null;
        // Com auto-ocultar, o Dock não reserva espaço (senão sobraria uma faixa vazia).
        if (!A.ReserveSpace || A.AutoHide)
        {
            _appBar.Remove();
            return null;
        }

        var toDevice = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
        if (toDevice is not Matrix m || m.M11 <= 0 || m.M22 <= 0) return null;
        if (!_appBar.Register()) return null;

        int px = (int)Math.Ceiling(ReservedThickness * (Horizontal ? m.M22 : m.M11));
        // Só chama o Windows quando algo mudou, para não entrar em ciclo com a mudança da área de trabalho.
        if (force || _appBar.Edge != A.Edge || _appBar.Thickness != px)
            _appBar.SetPos(A.Edge, px);

        var rc = _appBar.Bounds;
        return new Rect(rc.Left / m.M11, rc.Top / m.M22, (rc.Right - rc.Left) / m.M11, (rc.Bottom - rc.Top) / m.M22);
    }

    // ================= Configurações → Dock =================

    private void ApplySettings()
    {
        if (!A.AutoHide) _shown = true;
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
        // Remove o handler antigo (o Viewbox de widget é reaproveitado entre reconstruções)
        if (v.Tag is MouseButtonEventHandler old) v.MouseLeftButtonDown -= old;

        // O clique é decidido ao soltar (OnMouseUpDrag): se o mouse andou, vira arraste.
        MouseButtonEventHandler down = (_, e) =>
        {
            e.Handled = true;
            if (GetMouseDock() is not (double m, double c)) return;
            tile.Pressed = true;
            _pressTile = tile;
            _pressPoint = (m, c);
            CaptureMouse();
            StartAnimating();
        };
        v.MouseLeftButtonDown += down;
        v.Tag = down;
    }

    // ================= Arrastar ícones no Dock =================

    private double SlotSize => BaseSize + Gap;
    private double _dragCross;

    private int AppCount => _tiles.Count(t => t.Kind == TileKind.App);

    /// <summary>Espaço que o ícone de índice <paramref name="index"/> deve abrir antes de si (índice = Count: depois do último).</summary>
    private double TargetGap(int index)
    {
        int slot;
        Tile? skip;
        if (_dragTile != null && !_dragRemove) { slot = _dragSlot; skip = _dragTile; }
        else if (_externalSlot >= 0) { slot = _externalSlot; skip = null; }
        else return 0;
        return GapIndex(slot, skip) == index ? SlotSize : 0;
    }

    /// <summary>Índice em _tiles antes do qual fica o espaço da posição <paramref name="slot"/> entre os apps.</summary>
    private int GapIndex(int slot, Tile? skip)
    {
        int seen = 0;
        for (int i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            if (t == skip) continue;
            if (t.Kind != TileKind.App || seen == slot) return i; // os apps vêm primeiro
            seen++;
        }
        return _tiles.Count;
    }

    /// <summary>Em qual das <paramref name="slots"/> posições entre os apps fica o ponto <paramref name="main"/>.</summary>
    private int SlotAt(double main, int slots, bool extraSlot)
    {
        double total = _tiles.Sum(TileRestLength) + Math.Max(0, _tiles.Count - 1) * Gap + Pad * 2 + (extraSlot ? SlotSize : 0);
        double start = (MainLength - total) / 2 + Pad;
        int k = (int)Math.Floor((main - start + Gap / 2) / SlotSize);
        return Math.Clamp(k, 0, Math.Max(0, slots - 1));
    }

    private void OnMouseMoveDrag(object sender, MouseEventArgs e)
    {
        if (_pressTile == null || GetMouseDock() is not (double m, double c)) return;
        if (_dragTile == null)
        {
            if (_pressTile.Kind != TileKind.App) return; // widgets ficam fixos
            if (Math.Abs(m - _pressPoint.main) < DragThreshold && Math.Abs(c - _pressPoint.cross) < DragThreshold) return;
            BeginDrag(_pressTile);
        }
        _dragRemove = c > EdgeMargin + BackgroundThickness + BaseSize * 0.9;
        _dragSlot = SlotAt(m, AppCount, extraSlot: false);
        StartAnimating();
    }

    private void BeginDrag(Tile t)
    {
        _dragTile = t;
        t.Pressed = false;
        Panel.SetZIndex(t.Visual, 50);
        _dragSlot = _tiles.Where(x => x.Kind == TileKind.App).ToList().IndexOf(t);
        // O espaço já nasce aberto onde o ícone estava, para nada pular.
        int gi = GapIndex(_dragSlot, t);
        if (gi < _tiles.Count) _tiles[gi].GapBefore = SlotSize;
        else _gapEnd = SlotSize;
    }

    /// <summary>Desenha o ícone arrastado seguindo o mouse.</summary>
    private void PlaceDragged(Tile t, double baseCross)
    {
        if (GetMouseDock() is not (double m, double c)) return;
        double size = BaseSize;
        double crossMax = Math.Max(baseCross, (Horizontal ? H : W) - size - 34); // deixa lugar para o rótulo
        t.Main = m - size / 2;
        t.Size = size;
        _dragCross = Math.Clamp(c - size / 2, baseCross, crossMax);
        PlaceRect(t.Visual, t.Main, _dragCross, size, size);
        t.Visual.Opacity = _dragRemove ? 0.5 : 0.9;
        if (t.Dot != null) t.Dot.Visibility = Visibility.Collapsed;
    }

    private void OnMouseUpDrag(object sender, MouseButtonEventArgs e)
    {
        var press = _pressTile;
        if (press == null) return;
        e.Handled = true;
        var drag = _dragTile;
        bool remove = _dragRemove;
        int slot = _dragSlot;
        var mouse = GetMouseDock();
        EndPress();

        if (drag == null)
        {
            // Não arrastou: é um clique, se soltou em cima do mesmo item.
            if (mouse is (double m, double c) && TileAt(m) == press && IsInside(m, c, true)) OnTileClicked(press);
            return;
        }

        var entry = drag.Entry!;
        if (remove)
        {
            Puff(mouse);
            RemoveEntry(entry);
            return;
        }
        var items = SettingsStore.Current.Items;
        int from = items.IndexOf(entry);
        if (from < 0 || from == slot) return;
        items.RemoveAt(from);
        items.Insert(Math.Clamp(slot, 0, items.Count), entry);
        SettingsStore.Commit();
    }

    private void EndPress()
    {
        if (_dragTile != null) Panel.SetZIndex(_dragTile.Visual, 0);
        if (_pressTile != null) _pressTile.Pressed = false;
        _pressTile = null;
        _dragTile = null;
        _dragRemove = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        StartAnimating();
    }

    private void CancelPress()
    {
        if (_pressTile != null) EndPress();
    }

    /// <summary>Nuvem rápida que some, no lugar onde um item foi removido.</summary>
    private void Puff((double main, double cross)? at)
    {
        if (at is not (double m, double c)) return;
        double size = BaseSize;
        var scale = new ScaleTransform(0.4, 0.4);
        var puff = new Ellipse
        {
            Fill = new RadialGradientBrush(Color.FromArgb(220, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = scale,
        };
        double crossMax = Math.Max(0, (Horizontal ? H : W) - size);
        PlaceRect(puff, m - size / 2, Math.Clamp(c - size / 2, 0, crossMax), size, size);
        Panel.SetZIndex(puff, 60);
        DockCanvas.Children.Add(puff);

        var duration = TimeSpan.FromMilliseconds(280);
        var grow = new DoubleAnimation(0.4, 1.5, duration) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        var fade = new DoubleAnimation(0.9, 0, duration);
        fade.Completed += (_, _) => DockCanvas.Children.Remove(puff);
        puff.BeginAnimation(OpacityProperty, fade);
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
        _hovering = !_menuOpen && _shown && mouse is (double mm, double mc) && IsInside(mm, mc, _hovering);
        if (_menuOpen) _hovering = wasHovering;
        if (_dragTile != null || _pressTile != null) _hovering = true;
        bool revealSettled = UpdateReveal(now, dt);

        // Distâncias medidas nas posições "em repouso" para o efeito não oscilar.
        var restCenters = RestCenters();
        double k = 1 - Math.Exp(-dt * Smoothing);
        bool settled = true;

        for (int i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            if (t.Kind == TileKind.Separator) continue;

            double target = 1;
            if (_hovering && _dragTile == null && _externalSlot < 0 && mouse is (double main, double) && MaxScale > 1.001)
            {
                double d = Math.Abs(main - restCenters[i]) / InfluenceRadius;
                if (d < 1) target = 1 + (MaxScale - 1) * (Math.Cos(Math.PI * d) + 1) / 2;
            }

            t.Scale += (target - t.Scale) * k;
            if (Math.Abs(target - t.Scale) > 0.002) settled = false;
            else t.Scale = target;

            double gap = TargetGap(i);
            t.GapBefore += (gap - t.GapBefore) * k;
            if (Math.Abs(gap - t.GapBefore) > 0.3) settled = false;
            else t.GapBefore = gap;

            if (t.BounceStart >= 0)
            {
                if (now - t.BounceStart > BounceDuration) t.BounceStart = -1;
                else settled = false;
            }
        }

        double endGap = TargetGap(_tiles.Count);
        _gapEnd += (endGap - _gapEnd) * k;
        if (Math.Abs(endGap - _gapEnd) > 0.3) settled = false;
        else _gapEnd = endGap;

        Layout();
        UpdateLabel(mouse);

        if (settled && revealSettled && !_hovering) StopAnimating();
    }

    // ================= Auto-ocultar =================

    /// <summary>Chamado pelo timer parado: acorda a animação quando o mouse chega ao Dock (ou à borda, se oculto).</summary>
    private bool ShouldWake()
    {
        if (!A.AutoHide || _shown) return IsMouseInDock();

        double now = _clock.Elapsed.TotalSeconds;
        if (IsMouseAtEdge())
        {
            // Espera um pouco para não aparecer quando o mouse só passa pela borda.
            if (_edgeSince < 0) _edgeSince = now;
            if (now - _edgeSince >= A.AutoHideDelay)
            {
                _edgeSince = -1;
                _shown = true;
                return true;
            }
        }
        else _edgeSince = -1;
        return false;
    }

    private bool IsMouseAtEdge() =>
        GetMouseDock() is (double m, double c)
        && m >= _dockStart && m <= _dockStart + _dockLength
        && c >= -2 && c <= EdgeTrigger;

    /// <summary>Decide se o Dock deve sumir e anima o deslize. Devolve true quando não há nada a animar.</summary>
    private bool UpdateReveal(double now, double dt)
    {
        bool waiting = false;
        if (!A.AutoHide) _shown = true;
        else if (_hovering || _menuOpen) _leaveSince = -1;
        else if (_shown)
        {
            if (_leaveSince < 0) _leaveSince = now;
            if (now - _leaveSince >= HideDelay) { _shown = false; _leaveSince = -1; }
            else waiting = true;
        }

        double target = _shown ? 1 : 0;
        _reveal += (target - _reveal) * (1 - Math.Exp(-dt * 14));
        bool settled = Math.Abs(target - _reveal) < 0.003;
        if (settled) _reveal = target;
        ApplySlide();
        return settled && !waiting;
    }

    /// <summary>Empurra todo o conteúdo do Dock para fora da tela conforme <see cref="_reveal"/>.</summary>
    private void ApplySlide()
    {
        // Fundo + margem + sombra precisam sair inteiros.
        double distance = (1 - _reveal) * (EdgeMargin + BackgroundThickness + 30);
        _slide.X = A.Edge switch { DockEdge.Left => -distance, DockEdge.Right => distance, _ => 0 };
        _slide.Y = A.Edge switch { DockEdge.Top => -distance, DockEdge.Bottom => distance, _ => 0 };
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
        int inFlow = 0;
        foreach (var t in _tiles)
        {
            content += t.GapBefore;
            if (t == _dragTile) continue; // o ícone arrastado segue o mouse, fora da fila
            content += t.Kind == TileKind.Separator ? SeparatorLength : b * t.Scale;
            inFlow++;
        }
        content += _gapEnd + Math.Max(0, inFlow - 1) * Gap;
        if (inFlow == 0 && content <= 0) content = b;

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
            x += t.GapBefore;
            if (t == _dragTile)
            {
                PlaceDragged(t, baseCross);
                continue;
            }
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
        _tiles.FirstOrDefault(t => t != _dragTile && t.Kind != TileKind.Separator && main >= t.Main - Gap / 2 && main < t.Main + t.Size + Gap / 2);

    private void UpdateLabel((double main, double cross)? mouse)
    {
        Tile? hot = null;
        if (A.ShowLabels && _hovering && !_menuOpen && _pressTile == null && _externalSlot < 0 && mouse is (double main, double)) hot = TileAt(main);
        if (_dragTile != null) hot = _dragTile;
        string text = _dragTile != null && _dragRemove ? "Remover" : hot?.Label ?? "";

        if (hot == null || string.IsNullOrEmpty(text))
        {
            NameLabel.Visibility = Visibility.Collapsed;
            return;
        }

        NameLabelText.Text = text;
        NameLabel.Visibility = Visibility.Visible;
        NameLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = NameLabel.DesiredSize;
        double mainLen = Horizontal ? sz.Width : sz.Height;
        double crossLen = Horizontal ? sz.Height : sz.Width;

        double cross = hot == _dragTile ? _dragCross + hot.Size + 10 : EdgeMargin + Pad + hot.Size + BounceOffset(hot) + 10;
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
        menu.Items.Add(Item(A.AutoHide ? "Desligar ocultação automática" : "Ligar ocultação automática", () =>
        {
            A.AutoHide = !A.AutoHide;
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
        var t = DropTarget(out double main);
        if (t?.Widget is { AcceptsFiles: true }) e.Effects = DragDropEffects.Move;
        else if (t?.Kind == TileKind.App && !files.All(IsPinnable)) e.Effects = DragDropEffects.Copy;
        else e.Effects = DragDropEffects.Link;

        // Ao fixar, abre espaço mostrando onde o item vai entrar.
        _externalSlot = e.Effects == DragDropEffects.Link ? SlotAt(main, AppCount + 1, extraSlot: true) : -1;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        int slot = _externalSlot;
        _externalSlot = -1;
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
        int index = slot >= 0 ? slot : _tiles.Count(x => x.Kind == TileKind.App && x.Main + x.Size / 2 < main);
        index = Math.Clamp(index, 0, items.Count);
        foreach (var f in files)
        {
            string name = Directory.Exists(f) ? new DirectoryInfo(f).Name : Path.GetFileNameWithoutExtension(f);
            items.Insert(index++, new DockEntry { Name = name, Path = f });
        }
        SettingsStore.Commit();
    }
}
