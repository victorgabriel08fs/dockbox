using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
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

    /// <summary>App = fixado; Running = aberto mas não fixado (temporário, depois dos fixados).</summary>
    private enum TileKind { App, Widget, Separator, Running }

    private sealed class Tile
    {
        public TileKind Kind;
        public DockEntry? Entry;
        public DockWidget? Widget;
        public required FrameworkElement Visual;
        public Ellipse? Dot;
        public Border? Badge;           // contador vermelho no canto
        public string? BadgeText;
        public bool Attention;          // app piscando pedindo atenção
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
    private Brush _focusBrush = Brushes.White;
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

    // ---- Apps abertos não fixados ----
    private sealed record UnpinnedApp(string Key, string Path, string Name);
    private List<UnpinnedApp> _unpinned = new();
    private static readonly string? OwnExe = Environment.ProcessPath?.ToLowerInvariant();
    private static readonly Dictionary<string, string> ExeNames = new(StringComparer.OrdinalIgnoreCase);

    // ---- Desfazer remoção ----
    private readonly DispatcherTimer _undoTimer = new() { Interval = TimeSpan.FromSeconds(6) };

    private Appearance A => SettingsStore.Current.Appearance;
    private double BaseSize => A.IconSize;
    private double MaxScale => A.Magnify && !ReduceMotion ? A.Magnification : 1.0;
    /// <summary>"Mostrar animações" desligado no Windows: sem ampliação, pulo nem deslizes.</summary>
    private static bool ReduceMotion => !SystemParameters.ClientAreaAnimation;
    private double InfluenceRadius => BaseSize * 3.0;
    private bool Horizontal => A.Edge is DockEdge.Bottom or DockEdge.Top;
    private double BackgroundThickness => BaseSize + Pad * 2;

    public DockWindow()
    {
        InitializeComponent();
        DockCanvas.RenderTransform = _slide;
        Panel.SetZIndex(_focusRing, 50);
        DockCanvas.Children.Add(_focusRing);
        AutomationProperties.SetName(this, "Dock");
        PreviewKeyDown += OnKeyboard;
        Deactivated += (_, _) => { if (!_menuOpen) ExitKeyboard(restoreFocus: false); };
        PositionWindow();

        // Parado: confere o mouse 20x por segundo. Com o mouse no Dock: anima a cada frame.
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _idleTimer.Tick += (_, _) =>
        {
            CheckFollowMouse();
            if (ShouldWake()) StartAnimating();
        };

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
        Loc.Changed += OnLanguageChanged;
        RemovalHistory.Recorded += entry => Dispatcher.BeginInvoke(() => ShowUndoToast(entry));
        _undoTimer.Tick += (_, _) => HideUndoToast();
        UndoToast.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            UndoRemoval();
        };
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            // Com a AppBar ativa, a reserva só é refeita se a borda ou a espessura mudaram (ver ReserveArea).
            if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(() => PositionWindow());
            // Animações ou alto contraste ligados/desligados no Windows.
            if (e.PropertyName is nameof(SystemParameters.ClientAreaAnimation) or nameof(SystemParameters.HighContrast))
                Dispatcher.BeginInvoke(ApplySettings);
        };
        IsVisibleChanged += (_, _) => UpdateBlur(); // escondido em tela cheia: o desfoque some junto
        Closed += (_, _) =>
        {
            _blur?.Close();
            _preview?.Close();
            CloseStack();
            foreach (var w in _latestWatchers.Values) w.Dispose();
            ReleaseMinimizeEffect();
            ReleaseAppBar();
            var h = new WindowInteropHelper(this).Handle;
            if (h != IntPtr.Zero) UnregisterHotKey(h, HotkeyId);
            if (h != IntPtr.Zero) UnregisterShellHook(h);
        };
        // Se o processo cair, devolve a área de trabalho inteira.
        AppDomain.CurrentDomain.UnhandledException += (_, _) => ReleaseAppBar();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => ReleaseAppBar();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => OnDisplayChanged();
        // Ao trocar de monitor com outra escala, o Windows redimensiona a janela; refaz a posição exata.
        DpiChanged += (_, _) => OnDisplayChanged();
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
        RegisterShortcut();
        RegisterShellHook(hwnd);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DISPLAYCHANGE) OnDisplayChanged();
        if (msg == ShellHookMessage && ShellHookMessage != 0) OnShellHook(wParam, lParam);
        if (msg == TaskbarCreatedMessage && A.HideTaskbar)
        {
            // O Explorer reiniciou e recriou a barra: esconde de novo quando ela terminar de aparecer.
            var again = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            again.Tick += (_, _) => { again.Stop(); ApplyTaskbar(); };
            again.Start();
        }
        if (msg == WM_HOTKEY && wParam.ToInt64() == HotkeyId)
        {
            Dispatcher.BeginInvoke(ToggleKeyboard);
            handled = true;
            return IntPtr.Zero;
        }
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

    private static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

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
            var hwnd = new WindowInteropHelper(this).Handle;
            _screens = Monitors.All();
            // Ao ligar "Seguir o mouse", o Dock fica onde está até o mouse levá-lo.
            if (A.Monitor == MonitorMode.FollowMouse && _followDevice == null) _followDevice = _screen?.Device;
            var screen = Screens.Pick(_screens, A.Monitor, A.MonitorDevice, _followDevice);
            if (hwnd == IntPtr.Zero || screen == null)
            {
                PositionInDips(SystemParameters.WorkArea);
                return;
            }
            // Mudou de monitor: a reserva antiga precisa ir para o novo.
            if (_screen?.Device != screen.Device) force = true;
            _screen = screen;

            // Tudo em pixels físicos do monitor do Dock, que pode ter outra escala que o principal.
            RECT area = ReserveArea(force, screen) ?? screen.Work;
            int t = (int)Math.Ceiling(WindowThickness * screen.Scale);
            RECT r = AppBar.Strip(area, A.Edge, t);
            SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
            // Mantém Width/Height (em DIPs) iguais ao tamanho real, porque o Layout usa esses valores.
            Width = (r.Right - r.Left) / screen.Scale;
            Height = (r.Bottom - r.Top) / screen.Scale;
            Layout();
        }
        finally
        {
            _positioning = false;
        }
    }

    /// <summary>Antes de a janela existir: posiciona em DIPs no monitor principal.</summary>
    private void PositionInDips(Rect area)
    {
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

    /// <summary>
    /// Reserva (ou libera) a faixa do Dock no monitor. Devolve a faixa em pixels físicos, ou null quando
    /// não há reserva e a janela deve usar a área de trabalho normal do monitor.
    /// </summary>
    private RECT? ReserveArea(bool force, ScreenInfo screen)
    {
        if (_appBar == null) return null;
        // Com auto-ocultar, o Dock não reserva espaço (senão sobraria uma faixa vazia).
        if (!A.ReserveSpace || A.AutoHide)
        {
            _appBar.Remove();
            return null;
        }
        if (!_appBar.Register()) return null;

        int px = (int)Math.Ceiling(ReservedThickness * screen.Scale);
        // Só chama o Windows quando algo mudou, para não entrar em ciclo com a mudança da área de trabalho.
        if (force || _appBar.Edge != A.Edge || _appBar.Thickness != px || !_appBar.Screen.Equals(screen.Bounds))
            _appBar.SetPos(A.Edge, px, screen.Bounds);
        return _appBar.Bounds;
    }

    // ================= Vários monitores =================

    private List<ScreenInfo> _screens = new();
    private ScreenInfo? _screen;
    /// <summary>Monitor para onde o mouse levou o Dock (modo "Seguir o mouse").</summary>
    private string? _followDevice;
    private string? _followCandidate;
    private double _followSince = -1;
    /// <summary>Segundos com o mouse parado na borda de outro monitor até o Dock ir para lá.</summary>
    private const double FollowDelay = 0.5;

    /// <summary>Modo "Seguir o mouse": leva o Dock para o monitor onde o mouse parou na borda.</summary>
    private void CheckFollowMouse()
    {
        if (A.Monitor != MonitorMode.FollowMouse || _screens.Count < 2 || _dragTile != null)
        {
            _followSince = -1;
            return;
        }
        if (!GetCursorPos(out POINT pt)) return;
        var target = Screens.AtEdge(_screens, pt.X, pt.Y, A.Edge);
        if (target == null || target.Device == _screen?.Device)
        {
            _followSince = -1;
            return;
        }

        double now = _clock.Elapsed.TotalSeconds;
        if (_followSince < 0 || _followCandidate != target.Device)
        {
            _followCandidate = target.Device;
            _followSince = now;
            return;
        }
        if (now - _followSince < FollowDelay) return;

        _followSince = -1;
        _followDevice = target.Device;
        PositionWindow(force: true);
        // O mouse já está na borda: com auto-ocultar, o Dock aparece direto.
        if (A.AutoHide) _shown = true;
        StartAnimating();
    }

    /// <summary>Monitor conectado/desconectado ou resolução/escala mudou.</summary>
    private void OnDisplayChanged() => Dispatcher.BeginInvoke(() => PositionWindow(force: true));

    // ================= Configurações → Dock =================

    private string _tilesSignature = "";

    private void ApplySettings()
    {
        if (!A.AutoHide) _shown = true;
        ClosePreview(); // tema, tamanho ou borda podem ter mudado
        ApplyTheme();
        // Mudança só de cor, opacidade ou cantos não precisa recriar os ícones.
        string signature = TilesSignature();
        if (signature != _tilesSignature || _tiles.Count == 0) RebuildTiles();
        PositionWindow();
        RegisterShortcut();
        ApplyTaskbar();
        ApplyMinimizeEffect();
        StartAnimating();
    }

    /// <summary>Esconde ou mostra a barra de tarefas do Windows conforme a opção.</summary>
    private static void ApplyTaskbar()
    {
        try
        {
            if (SettingsStore.Current.Appearance.HideTaskbar) Taskbar.Hide();
            else if (Taskbar.Hidden) Taskbar.Restore();
        }
        catch (Exception ex) { ErrorLog.Write("Barra de tarefas", ex); }
    }

    /// <summary>Idioma mudou: recria os ícones, pede rótulos novos aos widgets e refaz os nomes para leitores de tela.</summary>
    private void OnLanguageChanged()
    {
        RebuildTiles(); // dicas de teclado e rótulos guardados nos ícones
        foreach (var t in _tiles.Where(t => t.Kind == TileKind.Widget)) t.Widget!.NextUpdate = DateTime.MinValue;
        UpdateAutomationNames();
    }

    private BlurBackdrop? _blur;
    private bool BlurActive => _blur is { IsVisible: true };

    /// <summary>Cria, mostra ou esconde o desfoque atrás do fundo conforme a opção e o Windows.</summary>
    private void UpdateBlur()
    {
        bool want = A.Blur && IsVisible && _reveal > 0.02 && BlurBackdrop.SystemAllows();
        bool was = BlurActive;
        try
        {
            if (want)
            {
                _blur ??= new BlurBackdrop();
                if (!_blur.IsVisible) _blur.Show();
                PlaceBlur();
            }
            else _blur?.Hide();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Desfoque", ex);
            _blur = null;
        }
        if (was != BlurActive) DockBackground.Background = BackgroundBrush();
    }

    /// <summary>Com desfoque, a cor do fundo fica mais transparente para o desfoque aparecer.</summary>
    private Brush BackgroundBrush()
    {
        if (!BlurActive) return _theme.Background;
        var b = _theme.Background.CloneCurrentValue();
        b.Opacity *= 0.7;
        b.Freeze();
        return b;
    }

    /// <summary>Leva o desfoque para baixo do fundo do Dock (inclusive durante a ampliação e o deslize).</summary>
    private void PlaceBlur()
    {
        if (!BlurActive || PresentationSource.FromVisual(this) is not { CompositionTarget: { } target }) return;
        double left = Canvas.GetLeft(DockBackground), top = Canvas.GetTop(DockBackground);
        if (double.IsNaN(left) || double.IsNaN(top) || double.IsNaN(DockBackground.Width)) return;
        var m = target.TransformToDevice;
        var p = DockCanvas.PointToScreen(new Point(left, top));
        _blur!.Place(new WindowInteropHelper(this).Handle,
            (int)Math.Round(p.X), (int)Math.Round(p.Y),
            (int)Math.Round(DockBackground.Width * m.M11), (int)Math.Round(DockBackground.Height * m.M22),
            (int)Math.Round(A.CornerRadius * m.M11));
    }

    private void ApplyTheme()
    {
        _theme = DockTheme.Current(A);
        _focusBrush = ColorUtil.Brush(_theme.Accent);
        _focusRing.BorderBrush = _focusBrush;
        App.ApplyUiTheme(_theme);

        UpdateBlur();
        DockBackground.Background = BackgroundBrush();
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

    /// <summary>Tudo o que muda quais ícones existem; se não mudou, o Dock não recria os ícones.</summary>
    private string TilesSignature()
    {
        var s = SettingsStore.Current;
        var sb = new System.Text.StringBuilder();
        foreach (var e in s.Items) sb.Append(e.Name).Append('\u0001').Append(e.Path).Append('\u0001').Append(e.Arguments).Append(e.StackShowsLatest ? '+' : '-').Append(e.Icon).Append('\u0002');
        foreach (var w in s.Widgets) sb.Append(w.Kind).Append(w.Enabled ? '+' : '-');
        sb.Append(A.ShowSeparator).Append(A.ShowRunningUnpinned);
        return sb.ToString();
    }

    private void RebuildTiles()
    {
        _tilesSignature = TilesSignature();
        var kbOld = _kbTile;
        var runningKeys = new HashSet<string>(_tiles.Where(t => t.Running && t.ExeKey != null).Select(t => t.ExeKey!));
        foreach (var t in _tiles)
        {
            DockCanvas.Children.Remove(t.Visual);
            if (t.Dot != null) DockCanvas.Children.Remove(t.Dot);
            if (t.Badge != null) DockCanvas.Children.Remove(t.Badge);
        }
        _tiles.Clear();

        var s = SettingsStore.Current;
        foreach (var entry in s.Items)
            _tiles.Add(CreateAppTile(entry));

        if (A.ShowRunningUnpinned && _unpinned.Count > 0)
        {
            if (_tiles.Count > 0) _tiles.Add(CreateSeparator());
            foreach (var app in _unpinned)
            {
                var t = CreateAppTile(new DockEntry { Name = app.Name, Path = app.Path });
                t.Kind = TileKind.Running;
                t.ExeKey = app.Key;
                t.Running = true;
                _tiles.Add(t);
            }
        }

        var widgets = new List<Tile>();
        foreach (var cfg in s.Widgets.Where(w => w.Enabled))
        {
            var t = CreateWidgetTile(cfg.Kind);
            if (t != null && t.Widget!.IsAvailable) widgets.Add(t);
        }

        if (widgets.Count > 0 && _tiles.Count > 0 && A.ShowSeparator)
            _tiles.Add(CreateSeparator());
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
            if (t.Badge != null) DockCanvas.Children.Add(t.Badge);
        }
        ApplyBadges();
        UpdateAutomationNames();
        if (kbOld != null)
        {
            // Mantém o foco do teclado no mesmo item (ou no primeiro, se ele saiu do Dock).
            var same = _tiles.FirstOrDefault(t => t.Kind != TileKind.Separator &&
                (t.Entry != null && kbOld.Entry != null && t.Entry.Path == kbOld.Entry.Path || t.Widget != null && t.Widget == kbOld.Widget));
            var next = same ?? FocusableTiles().FirstOrDefault();
            if (next != null) FocusTile(next);
            else ExitKeyboard(restoreFocus: true);
        }
        RemapPreview();
        WatchLatestFolders();
        Layout();
    }

    private Tile CreateSeparator() => new()
    {
        Kind = TileKind.Separator,
        Visual = new Rectangle { Fill = _theme.Separator, IsHitTestVisible = false },
    };

    private Tile CreateAppTile(DockEntry entry)
    {
        if (!_iconCache.TryGetValue(entry.Path, out var icon))
        {
            icon = IconLoader.Load(entry.Path);
            _iconCache[entry.Path] = icon;
        }

        if (CustomIcons.Load(entry.Icon) is ImageSource custom) icon = custom; // escolhido pelo usuário vale mais
        else if (entry.StackShowsLatest && Directory.Exists(Environment.ExpandEnvironmentVariables(entry.Path))) icon = FolderIcon(entry, icon);
        var img = new Image { Source = icon, Stretch = Stretch.Uniform, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null };
        AutomationProperties.SetHelpText(img, Loc.T("Dock_TileHelp"));
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

        var tile = new Tile
        {
            Kind = TileKind.App,
            Entry = entry,
            Visual = img,
            ExeKey = ShellLinks.ExecutableKey(entry.Path),
            Dot = new Ellipse { Width = DotSize, Height = DotSize, Fill = _theme.Dot, IsHitTestVisible = false, Visibility = Visibility.Collapsed },
            Badge = CreateBadge(),
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
            var box = new Viewbox { Stretch = Stretch.Uniform, Child = w.Visual, Cursor = Cursors.Hand, Focusable = true, FocusVisualStyle = null };
            cached = (w, box);
            _widgetCache[kind] = cached;
        }

        var tile = new Tile { Kind = TileKind.Widget, Widget = cached.Widget, Visual = cached.Box };
        AutomationProperties.SetHelpText(cached.Box, Loc.T("Dock_TileHelp"));
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
        if (t.Badge != null) t.Badge.Visibility = Visibility.Collapsed;
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
        if (at is not (double m, double c) || ReduceMotion) return;
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
        ClosePreview();
        switch (tile.Kind)
        {
            case TileKind.App:
                if (IsFolder(tile) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ToggleStack(tile); // Ctrl+clique abre direto no Explorador
                    return;
                }
                CloseStack();
                bool newInstance = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                if (!newInstance && tile.ExeKey != null && tile.Running && RunningApps.ToggleWindows(tile.ExeKey))
                    return;
                if (AppLauncher.Launch(tile.Entry!) && A.BounceOnLaunch && !ReduceMotion && !tile.Running)
                    Bounce(tile);
                break;

            case TileKind.Running:
                CloseStack();
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || tile.ExeKey == null || !RunningApps.ToggleWindows(tile.ExeKey))
                    AppLauncher.Launch(tile.Entry!);
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
        if (RemovalHistory.Remove(SettingsStore.Current.Items, entry)) SettingsStore.Commit();
    }

    // ================= Desfazer remoção =================

    private void ShowUndoToast(DockEntry entry)
    {
        string name = entry.Name.Length > 32 ? entry.Name[..29] + "..." : entry.Name;
        UndoText.Text = Loc.F("Undo_Removed", name);
        UndoToast.Background = _theme.LabelBackground;
        UndoToast.BorderBrush = _theme.LabelBorder;
        UndoText.Foreground = _theme.LabelForeground;
        UndoButton.Background = ColorUtil.Brush(_theme.Accent);
        ((TextBlock)UndoButton.Child).Foreground = ColorUtil.Luminance(_theme.Accent) > 0.7 ? Brushes.Black : Brushes.White;
        UndoToast.Visibility = Visibility.Visible;
        _undoTimer.Stop();
        _undoTimer.Start();
        _shown = true; // com auto-ocultar, mostra o Dock junto com o aviso
        Layout();
        StartAnimating();
    }

    private void HideUndoToast()
    {
        _undoTimer.Stop();
        UndoToast.Visibility = Visibility.Collapsed;
        StartAnimating();
    }

    private void UndoRemoval()
    {
        HideUndoToast();
        if (RemovalHistory.Undo(SettingsStore.Current.Items) != null) SettingsStore.Commit();
    }

    /// <summary>Coloca o aviso centralizado acima do Dock.</summary>
    private void PlaceUndoToast()
    {
        if (UndoToast.Visibility != Visibility.Visible) return;
        UndoToast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var sz = UndoToast.DesiredSize;
        double mainLen = Horizontal ? sz.Width : sz.Height;
        double crossLen = Horizontal ? sz.Height : sz.Width;
        double main0 = Math.Clamp(_dockStart + _dockLength / 2 - mainLen / 2, 4, Math.Max(4, MainLength - mainLen - 4));
        double cross = EdgeMargin + BackgroundThickness + 12;
        PlacePos(UndoToast, Math.Round(main0), Math.Round(cross), mainLen, crossLen);
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
        if (fullscreen && IsVisible && !_hovering && !_menuOpen)
        {
            ClosePreview();
            Hide();
        }
        else if (!fullscreen && !IsVisible) Show();

        // De vez em quando reafirma "sempre no topo" (a barra de tarefas às vezes passa por cima).
        if (++_tickCount % 3 == 0 && !_menuOpen && !fullscreen)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
            if (hwnd != IntPtr.Zero && BlurActive) _blur!.StayBelow(hwnd);
            // Efeitos de transparência ou economia de bateria podem ter mudado.
            if (A.Blur) UpdateBlur();
        }

        // A barra de tarefas pode voltar sozinha (monitor novo, tecla Windows): esconde de novo.
        if (A.HideTaskbar && !fullscreen)
        {
            try { if (Taskbar.ShouldHideAgain()) Taskbar.Hide(); }
            catch { }
        }

        // Indicadores de apps abertos e apps abertos não fixados
        if (A.ShowRunning || A.ShowRunningUnpinned || A.ShowBadges || A.BounceOnAttention)
        {
            List<RunningApps.AppWindow> windows;
            try { windows = RunningApps.Windows(); }
            catch { windows = new(); }
            var snap = RunningApps.Snapshot(windows);
            bool changed = false;
            foreach (var t in _tiles.Where(t => t.Kind == TileKind.App))
            {
                bool running = t.ExeKey != null && snap.ContainsKey(t.ExeKey);
                if (running != t.Running) { t.Running = running; changed = true; }
            }
            if (UpdateBadges(windows)) changed = true;
            if (changed && !_animating) Layout();
            UpdateUnpinned(windows);
            if (changed) UpdateAutomationNames();
            RefreshAttention(windows);
        }
        else if (_unpinned.Count > 0)
        {
            _unpinned = new();
            RebuildTiles();
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
        UpdateAutomationNames();
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
        double k = ReduceMotion ? 1 : 1 - Math.Exp(-dt * Smoothing);

        // Centro da ampliação: o mouse, ou o item com foco do teclado.
        double? focusMain = null;
        if (_hovering && mouse is (double hm, double)) focusMain = hm;
        else if (_kbTile != null && _tiles.IndexOf(_kbTile) is int ki && ki >= 0) focusMain = restCenters[ki];
        bool settled = true;

        for (int i = 0; i < _tiles.Count; i++)
        {
            var t = _tiles[i];
            if (t.Kind == TileKind.Separator) continue;

            double target = 1;
            if (focusMain is double main && _dragTile == null && _externalSlot < 0 && MaxScale > 1.001)
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
        UpdatePreview(mouse);

        if (settled && revealSettled && !_hovering) StopAnimating();
    }

    // ================= Apps abertos não fixados =================

    /// <summary>Atualiza a seção de apps abertos que não estão fixados; reconstrói o Dock só se mudou.</summary>
    private void UpdateUnpinned(List<RunningApps.AppWindow> windows)
    {
        if (_pressTile != null || _menuOpen) return; // não mexe nos ícones durante arraste ou menu

        var next = new List<UnpinnedApp>();
        if (A.ShowRunningUnpinned)
        {
            var pinned = new HashSet<string>(_tiles.Where(t => t.Kind == TileKind.App && t.ExeKey != null).Select(t => t.ExeKey!));
            var found = new Dictionary<string, UnpinnedApp>();
            foreach (var w in windows)
            {
                if (w.Key is not string key || w.Exe == OwnExe) continue;
                if (w.Keys.Any(pinned.Contains) || found.ContainsKey(key)) continue;
                string path = w.Aumid != null ? @"shell:AppsFolder\" + w.Aumid : w.Exe!;
                found[key] = new UnpinnedApp(key, path, AppName(w));
            }
            // Mantém a ordem de quem já estava e coloca os novos no fim, como no macOS.
            next.AddRange(_unpinned.Where(u => found.ContainsKey(u.Key)).Select(u => found[u.Key]));
            next.AddRange(found.Values.Where(f => !_unpinned.Any(u => u.Key == f.Key)));
        }

        if (next.Select(u => u.Key).SequenceEqual(_unpinned.Select(u => u.Key))) return;
        _unpinned = next;
        RebuildTiles();
        StartAnimating();
    }

    /// <summary>Nome do app: descrição do executável (ex.: "Google Chrome") ou o título da janela, para apps da Store.</summary>
    private static string AppName(RunningApps.AppWindow w)
    {
        if (w.Aumid != null || w.Exe == null)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(w.Handle, sb, sb.Capacity);
            return sb.Length > 0 ? sb.ToString() : "App";
        }
        if (ExeNames.TryGetValue(w.Exe, out var cached)) return cached;
        string name;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(w.Exe);
            name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription.Trim() : Path.GetFileNameWithoutExtension(w.Exe);
        }
        catch { name = Path.GetFileNameWithoutExtension(w.Exe); }
        if (name.Length > 0) name = char.ToUpper(name[0]) + name[1..];
        ExeNames[w.Exe] = name;
        return name;
    }

    /// <summary>"Manter no Dock": fixa um app aberto, no fim dos fixados.</summary>
    private void PinRunning(Tile tile)
    {
        var entry = tile.Entry!;
        _unpinned.RemoveAll(u => u.Key == tile.ExeKey);
        if (DockItems.FindDuplicate(SettingsStore.Current.Items, entry.Path) == null)
            SettingsStore.Current.Items.Add(new DockEntry { Name = entry.Name, Path = entry.Path });
        SettingsStore.Commit();
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
        else if (_hovering || _menuOpen || _kbTile != null || PreviewOpen || StackOpen || UndoToast.Visibility == Visibility.Visible) _leaveSince = -1;
        else if (_shown)
        {
            if (_leaveSince < 0) _leaveSince = now;
            if (now - _leaveSince >= HideDelay) { _shown = false; _leaveSince = -1; }
            else waiting = true;
        }

        double target = _shown ? 1 : 0;
        _reveal += (target - _reveal) * (ReduceMotion ? 1 : 1 - Math.Exp(-dt * 14));
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
        if (A.Blur && (_reveal > 0.02) != BlurActive) UpdateBlur();
        else PlaceBlur();
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
        PlaceBlur();

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
            if (t.Badge != null) PlaceBadge(t, size - press);
            if (t == _kbTile)
                PlaceRect(_focusRing, x - 3, baseCross + lift - 3, size + 6, size + 6);
            x += size + Gap;
        }
        _focusRing.Visibility = _kbTile != null && _tiles.Contains(_kbTile) ? Visibility.Visible : Visibility.Collapsed;
        PlaceUndoToast();
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
        if (hot == null && A.ShowLabels && _kbTile != null && !_hovering && !_menuOpen) hot = _kbTile;
        if (_dragTile != null) hot = _dragTile;
        else if (PreviewOpen || StackOpen) hot = null; // o painel de miniaturas já mostra os títulos
        string text = _dragTile != null && _dragRemove ? Loc.T("Drag_Remove") : hot?.Label ?? "";

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

    // ================= Teclado e leitor de tela =================

    private const int HotkeyId = 0xD0C;
    private string? _shortcutRegistered;
    /// <summary>Por que o atalho não pôde ser registrado (já usado por outro programa), ou null.</summary>
    public static string? ShortcutError => _shortcutFailed is string s ? Loc.F("Shortcut_InUse", Hotkey.Display(s)) : null;
    private static string? _shortcutFailed;
    public static event Action? ShortcutStatusChanged;

    private Tile? _kbTile;          // item com o foco do teclado; null = fora do modo teclado
    private IntPtr _kbPrevious;     // janela que tinha o foco antes do atalho
    private readonly Border _focusRing = new()
    {
        BorderThickness = new Thickness(2.5),
        CornerRadius = new CornerRadius(12),
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    private void RegisterShortcut()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        string want = A.KeyboardShortcut ?? "";
        if (want == _shortcutRegistered) return;

        UnregisterHotKey(hwnd, HotkeyId);
        _shortcutRegistered = want;
        _shortcutFailed = null;
        if (Hotkey.Parse(want) is (uint mods, uint vk) && !RegisterHotKey(hwnd, HotkeyId, mods | MOD_NOREPEAT, vk))
            _shortcutFailed = want;
        ShortcutStatusChanged?.Invoke();
    }

    private List<Tile> FocusableTiles() => _tiles.Where(t => t.Kind != TileKind.Separator).ToList();

    /// <summary>Atalho global: entra no modo teclado, ou sai se já estiver nele.</summary>
    private void ToggleKeyboard()
    {
        if (_kbTile != null)
        {
            ExitKeyboard(restoreFocus: true);
            return;
        }
        var tiles = FocusableTiles();
        if (tiles.Count == 0 || !IsVisible) return;

        var hwnd = new WindowInteropHelper(this).Handle;
        _kbPrevious = GetForegroundWindow();
        _shown = true; // com auto-ocultar, o Dock aparece
        // O atalho global dá a este processo o direito de pegar o foco.
        SetForegroundWindow(hwnd);
        FocusTile(tiles[0]);
    }

    private void FocusTile(Tile t)
    {
        _kbTile = t;
        t.Visual.Focus();
        Keyboard.Focus(t.Visual);
        StartAnimating();
    }

    private void ExitKeyboard(bool restoreFocus)
    {
        if (_kbTile == null) return;
        _kbTile = null;
        _focusRing.Visibility = Visibility.Collapsed;
        Keyboard.ClearFocus();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (restoreFocus && _kbPrevious != IntPtr.Zero && GetForegroundWindow() == hwnd) SetForegroundWindow(_kbPrevious);
        _kbPrevious = IntPtr.Zero;
        StartAnimating();
    }

    /// <summary>Setas/Tab escolhem, Enter/Espaço abrem, Shift+F10 ou tecla de menu abrem o menu, Esc sai.</summary>
    private void OnKeyboard(object sender, KeyEventArgs e)
    {
        if (_kbTile == null || _menuOpen) return;
        var tiles = FocusableTiles();
        if (tiles.Count == 0) { ExitKeyboard(restoreFocus: true); return; }
        int i = Math.Max(0, tiles.IndexOf(_kbTile));
        int n = tiles.Count;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        switch (key)
        {
            case Key.Left or Key.Up:
                FocusTile(tiles[(i - 1 + n) % n]);
                break;
            case Key.Right or Key.Down:
                FocusTile(tiles[(i + 1) % n]);
                break;
            case Key.Tab:
                FocusTile(tiles[shift ? (i - 1 + n) % n : (i + 1) % n]);
                break;
            case Key.Home:
                FocusTile(tiles[0]);
                break;
            case Key.End:
                FocusTile(tiles[n - 1]);
                break;
            case Key.Enter or Key.Space:
                var t = tiles[i];
                ExitKeyboard(restoreFocus: false);
                OnTileClicked(t); // Ctrl+Enter abre nova janela, como Ctrl+clique
                break;
            case Key.Apps:
            case Key.F10 when shift:
                ShowMenu(BuildMenu(tiles[i]), tiles[i]);
                break;
            case Key.Escape:
                ExitKeyboard(restoreFocus: true);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Nomes para o Narrador/NVDA: nome do app e se está aberto; widget e o que ele mostra.</summary>
    private void UpdateAutomationNames()
    {
        foreach (var t in _tiles)
        {
            string name = t.Kind switch
            {
                TileKind.App => t.Running ? Loc.F("A11y_Open", t.Entry!.Name) : t.Entry!.Name,
                TileKind.Running => Loc.F("A11y_OpenUnpinned", t.Entry!.Name),
                TileKind.Widget => string.IsNullOrEmpty(t.Widget!.Label) ? t.Widget.DisplayName : $"{t.Widget.DisplayName}: {t.Widget.Label}",
                _ => "",
            };
            if (name.Length > 0 && t.BadgeText != null) name = Loc.F("A11y_WithBadge", name, t.BadgeText);
            if (name.Length > 0 && t.Attention) name = Loc.F("A11y_WithAttention", name);
            if (name.Length > 0 && AutomationProperties.GetName(t.Visual) != name)
                AutomationProperties.SetName(t.Visual, name);
        }
    }

    // ================= Menus =================

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var mouse = GetMouseDock();
        var tile = mouse is (double main, double) ? TileAt(main) : null;
        ShowMenu(BuildMenu(tile));
    }

    /// <summary>Menu do item (app, widget) ou, sem item, as opções gerais do Dock.</summary>
    private ContextMenu BuildMenu(Tile? tile)
    {
        var menu = new ContextMenu();
        if (tile?.Kind is TileKind.App or TileKind.Running)
        {
            // Cada app tem o próprio menu; as opções gerais ficam num submenu.
            AddAppItems(menu, tile);
            menu.Items.Add(new Separator());
            var options = new MenuItem { Header = Loc.T("Menu_DockOptions") };
            AddDockItems(options);
            menu.Items.Add(options);
        }
        else
        {
            if (tile?.Kind == TileKind.Widget)
            {
                var w = tile.Widget!;
                menu.Items.Add(Header(w.DisplayName));
                foreach (var (h, a) in w.MenuItems()) menu.Items.Add(Item(h, a));
                menu.Items.Add(Item(Loc.T("Menu_RemoveWidget"), () => DisableWidget(w.Kind)));
                menu.Items.Add(new Separator());
            }
            AddDockItems(menu);
        }
        return menu;
    }

    /// <summary>Menu de um app: janelas abertas, nova janela, Explorador, fechar e remover.</summary>
    private void AddAppItems(ItemsControl menu, Tile tile)
    {
        var entry = tile.Entry!;
        menu.Items.Add(Header(entry.Name));
        if (IsFolder(tile))
        {
            menu.Items.Add(Item(Loc.T("Stack_OpenInExplorer"), () => AppLauncher.Launch(entry)));
            AddStackItems(menu, entry);
            menu.Items.Add(new Separator());
        }

        var windows = tile.Running && tile.ExeKey != null ? RunningApps.WindowsOf(tile.ExeKey) : new();
        if (windows.Count > 0)
        {
            foreach (var (hwnd, title, minimized) in windows.Take(12))
            {
                string text = title.Length > 60 ? title[..57] + "..." : title;
                var mi = Item(minimized ? Loc.F("Menu_Minimized", text) : text, () => RunningApps.Activate(hwnd));
                mi.Padding = new Thickness(18, mi.Padding.Top, mi.Padding.Right, mi.Padding.Bottom);
                menu.Items.Add(mi);
            }
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(Loc.T("Menu_NewWindow"), () => AppLauncher.Launch(entry)));
        }
        else
        {
            menu.Items.Add(Item(Loc.T("Menu_Open"), () => OnTileClicked(tile)));
        }

        menu.Items.Add(Item(Directory.Exists(Environment.ExpandEnvironmentVariables(entry.Path)) ? Loc.T("Menu_ShowFolderInExplorer") : Loc.T("Menu_ShowInExplorer"),
            () => AppLauncher.ShowInExplorer(entry.Path)));
        menu.Items.Add(new Separator());
        if (tile.Kind == TileKind.Running) menu.Items.Add(Item(Loc.T("Menu_KeepInDock"), () => PinRunning(tile)));
        else
        {
            menu.Items.Add(Item(Loc.T("Menu_ChangeIcon"), () => { if (CustomIcons.Choose(null, entry)) SettingsStore.Commit(); }));
            if (entry.Icon != null) menu.Items.Add(Item(Loc.T("Menu_RestoreIcon"), () => { CustomIcons.Restore(entry); SettingsStore.Commit(); }));
            menu.Items.Add(Item(Loc.T("Menu_RemoveFromDock"), () => RemoveEntry(entry)));
        }

        if (windows.Count > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item(windows.Count == 1 ? Loc.T("Menu_Close") : Loc.F("Menu_CloseAll", windows.Count), () =>
            {
                foreach (var (hwnd, _, _) in windows) RunningApps.Close(hwnd);
            }));
        }
    }

    /// <summary>Opções gerais do Dock (área vazia, separador, widgets e submenu dos apps).</summary>
    private void AddDockItems(ItemsControl menu)
    {
        menu.Items.Add(Item(Loc.T("Menu_ManageDock"), () => SettingsWindow.ShowSingle()));
        menu.Items.Add(Item(Loc.T(A.Magnify ? "Menu_MagnifyOff" : "Menu_MagnifyOn"), () =>
        {
            A.Magnify = !A.Magnify;
            SettingsStore.Commit();
        }));
        menu.Items.Add(Item(Loc.T(A.AutoHide ? "Menu_AutoHideOff" : "Menu_AutoHideOn"), () =>
        {
            A.AutoHide = !A.AutoHide;
            SettingsStore.Commit();
        }));
        menu.Items.Add(Item(Loc.T(A.HideTaskbar ? "Menu_TaskbarShow" : "Menu_TaskbarHide"), () =>
        {
            A.HideTaskbar = !A.HideTaskbar;
            SettingsStore.Commit();
        }));
        if (A.HideTaskbar) menu.Items.Add(Item(Loc.T("Menu_StartMenu"), Taskbar.OpenStartMenu));
        menu.Items.Add(new Separator());
        menu.Items.Add(Header(Loc.T("Menu_Position")));
        foreach (var (edge, name) in new[] { (DockEdge.Bottom, Loc.T("Edge_Bottom")), (DockEdge.Left, Loc.T("Edge_Left")), (DockEdge.Right, Loc.T("Edge_Right")), (DockEdge.Top, Loc.T("Edge_Top")) })
        {
            var mi = Item(name, () => { A.Edge = edge; SettingsStore.Commit(); });
            mi.IsCheckable = false;
            mi.IsChecked = A.Edge == edge;
            menu.Items.Add(mi);
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(Loc.T("Menu_Quit"), Close));
    }

    private void ShowMenu(ContextMenu menu, Tile? at = null)
    {
        IntPtr previous = IntPtr.Zero;
        var hwnd = new WindowInteropHelper(this).Handle;
        if (at != null)
        {
            // Aberto pelo teclado: ao lado do item, do lado de dentro da tela.
            menu.PlacementTarget = at.Visual;
            menu.Placement = A.Edge switch
            {
                DockEdge.Top => System.Windows.Controls.Primitives.PlacementMode.Bottom,
                DockEdge.Left => System.Windows.Controls.Primitives.PlacementMode.Right,
                DockEdge.Right => System.Windows.Controls.Primitives.PlacementMode.Left,
                _ => System.Windows.Controls.Primitives.PlacementMode.Top,
            };
        }
        else
        {
            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        }
        menu.Opened += (_, _) =>
        {
            _menuOpen = true;
            ClosePreview();
            NameLabel.Visibility = Visibility.Collapsed;
            // O Dock não pega foco; só durante o menu, para setas, Enter e Esc funcionarem.
            previous = GetForegroundWindow();
            SetForegroundWindow(hwnd);
            menu.Focus();
        };
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            StartAnimating();
            // Devolve o foco para quem tinha, a não ser que a ação do menu tenha ativado outra janela.
            if (_kbTile != null && GetForegroundWindow() == hwnd)
            {
                // Continua no modo teclado: o foco volta para o item.
                FocusTile(_kbTile);
                return;
            }
            if (previous != IntPtr.Zero && GetForegroundWindow() == hwnd) SetForegroundWindow(previous);
        };
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
        else if (CanDropInto(t, files)) e.Effects = FolderDropEffect(t!, files);
        else if (t?.Kind is TileKind.App or TileKind.Running && !files.All(IsPinnable)) e.Effects = DragDropEffects.Copy;
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
        if (CanDropInto(t, files))
        {
            DropIntoFolder(t!, files);
            return;
        }
        if (t?.Kind is TileKind.App or TileKind.Running && !files.All(IsPinnable))
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
            if (DockItems.FindDuplicate(items, f) is DockEntry existing)
            {
                // Já está no Dock: destaca o item em vez de duplicar.
                if (_tiles.FirstOrDefault(x => x.Entry == existing) is Tile same) Bounce(same);
                continue;
            }
            string name = Directory.Exists(f) ? new DirectoryInfo(f).Name : Path.GetFileNameWithoutExtension(f);
            var entry = new DockEntry { Name = name, Path = f };
            var risk = AppLauncher.Assess(f, null);
            if (risk == LaunchRisk.Blocked) continue;
            if (risk == LaunchRisk.Confirm)
            {
                // Fixar um script por arraste também avisa; quem confirmou aqui não é perguntado de novo ao abrir.
                if (!AppLauncher.ConfirmRisky(name, f, null, pinning: true)) continue;
                entry.Trusted = true;
            }
            items.Insert(index++, entry);
        }
        SettingsStore.Commit();
    }
}
