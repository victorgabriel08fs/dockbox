using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using static Dockbox.NativeMethods;

namespace Dockbox;

public partial class MainWindow : Window
{
    // ---- Aparência (em DIPs) ----
    private const double BaseSize = 56;        // tamanho do ícone em repouso
    private const double MaxScale = 1.9;       // ampliação máxima do ícone sob o mouse
    private const double Gap = 8;              // espaço entre ícones
    private const double DockPadding = 9;         // margem interna do fundo
    private const double BottomMargin = 6;     // distância do fundo até a borda da área de trabalho
    private const double LabelSpace = 40;      // espaço acima dos ícones para o rótulo
    private const double InfluenceRadius = BaseSize * 3.2; // até onde a ampliação "espalha"
    private const double Smoothing = 18;       // maior = segue o mouse mais rápido

    private static readonly Brush HitBrush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

    private sealed class DockItem
    {
        public required PinnedApp App;
        public required Image Image;
        public double Scale = 1;
        public double Left, Width; // posição atual no Canvas
        public double PressOffset; // pequeno "afundar" ao clicar
    }

    private readonly List<DockItem> _items = new();
    private readonly DispatcherTimer _idleTimer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private double _lastFrame;
    private bool _animating;
    private bool _hovering;
    private double _dockLeft, _dockWidth;

    public MainWindow()
    {
        InitializeComponent();

        // Quando nada se mexe, verifica o mouse 20x por segundo (barato).
        // Ao entrar no Dock, passa a animar a cada frame de tela (CompositionTarget.Rendering).
        _idleTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
        _idleTimer.Tick += (_, _) => { if (IsMouseInDock()) StartAnimating(); };

        SourceInitialized += OnSourceInitialized;
        PositionWindow();
        Loaded += (_, _) => { LoadItems(); _idleTimer.Start(); };
        SizeChanged += (_, _) => Layout();
        Drop += OnDrop;
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };

        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SystemParameters.WorkArea)) Dispatcher.BeginInvoke(PositionWindow);
        };
    }

    // ---------------- Janela: sem foco, fora do Alt+Tab ----------------

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

    private void PositionWindow()
    {
        Rect wa = SystemParameters.WorkArea; // monitor principal, em DIPs
        Width = wa.Width;
        Height = BaseSize * MaxScale + DockPadding * 2 + BottomMargin + LabelSpace;
        Left = wa.Left;
        Top = wa.Bottom - Height;
        Layout();
    }

    // ---------------- Ícones ----------------

    private void LoadItems()
    {
        foreach (var it in _items) DockCanvas.Children.Remove(it.Image);
        _items.Clear();

        foreach (var app in PinnedAppsStore.Load())
        {
            var img = new Image
            {
                Source = IconLoader.Load(app.Path),
                Stretch = Stretch.Uniform,
                Cursor = Cursors.Hand,
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);

            var item = new DockItem { App = app, Image = img };
            img.MouseLeftButtonDown += (_, e) => { item.PressOffset = 1; StartAnimating(); e.Handled = true; };
            img.MouseLeftButtonUp += (_, e) =>
            {
                bool wasPressed = item.PressOffset > 0;
                item.PressOffset = 0;
                StartAnimating();
                if (wasPressed) Launch(item.App);
                e.Handled = true;
            };
            img.MouseLeave += (_, _) => { item.PressOffset = 0; StartAnimating(); };

            _items.Add(item);
            DockCanvas.Children.Add(img);
        }

        foreach (var it in _items) it.Scale = 1;
        Layout();
    }

    private static void Launch(PinnedApp app)
    {
        try
        {
            string path = Environment.ExpandEnvironmentVariables(app.Path);
            AllowSetForegroundWindow(ASFW_ANY);
            var psi = new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = app.Arguments ?? "",
                WorkingDirectory = File.Exists(path) ? Path.GetDirectoryName(path) ?? "" : "",
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir \"{app.Name}\":\n{ex.Message}", "Dockbox",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------------- Animação de ampliação ----------------

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

        Point? mouse = GetMouse();
        _hovering = mouse is Point p && IsInside(p, _hovering);

        // Posições "em repouso" (sem ampliação) servem de referência para a distância,
        // assim o efeito não oscila enquanto os ícones crescem.
        double restWidth = RestWidth();
        double restLeft = (W - restWidth) / 2 + DockPadding;

        double k = 1 - Math.Exp(-dt * Smoothing);
        bool settled = true;

        for (int i = 0; i < _items.Count; i++)
        {
            double target = 1;
            if (_hovering && mouse is Point m)
            {
                double center = restLeft + i * (BaseSize + Gap) + BaseSize / 2;
                double d = Math.Abs(m.X - center) / InfluenceRadius;
                if (d < 1) target = 1 + (MaxScale - 1) * (Math.Cos(Math.PI * d) + 1) / 2;
            }

            var it = _items[i];
            it.Scale += (target - it.Scale) * k;
            if (Math.Abs(target - it.Scale) > 0.002) settled = false;
            else it.Scale = target;
        }

        Layout();
        UpdateLabel(mouse);

        if (settled && !_hovering) StopAnimating();
    }

    // Usa o tamanho que nós definimos (Width/Height) para não depender do próximo passe de layout.
    private double W => double.IsNaN(Width) ? ActualWidth : Width;
    private double H => double.IsNaN(Height) ? ActualHeight : Height;

    private double RestWidth() =>
        _items.Count == 0 ? BaseSize + DockPadding * 2
                          : _items.Count * BaseSize + (_items.Count - 1) * Gap + DockPadding * 2;

    /// <summary>Posiciona fundo e ícones conforme a escala atual de cada um.</summary>
    private void Layout()
    {
        double w = W, h = H;
        if (w <= 0 || h <= 0) return;

        double contentWidth = 0;
        foreach (var it in _items) contentWidth += BaseSize * it.Scale;
        contentWidth += Math.Max(0, _items.Count - 1) * Gap;
        if (_items.Count == 0) contentWidth = BaseSize;

        _dockWidth = contentWidth + DockPadding * 2;
        _dockLeft = Math.Round((w - _dockWidth) / 2);

        double bgHeight = BaseSize + DockPadding * 2;
        double bgTop = h - BottomMargin - bgHeight;
        Canvas.SetLeft(DockBackground, _dockLeft);
        Canvas.SetTop(DockBackground, bgTop);
        DockBackground.Width = _dockWidth;
        DockBackground.Height = bgHeight;

        // Área de hover: cobre toda a altura sobre o Dock enquanto o mouse está nele.
        Canvas.SetLeft(HitArea, _dockLeft);
        Canvas.SetTop(HitArea, 0);
        HitArea.Width = _dockWidth;
        HitArea.Height = h;
        HitArea.Fill = _hovering ? HitBrush : Brushes.Transparent;

        double x = _dockLeft + DockPadding;
        double iconBottom = h - BottomMargin - DockPadding;
        foreach (var it in _items)
        {
            double size = BaseSize * it.Scale;
            double press = it.PressOffset * 4;
            it.Left = x;
            it.Width = size;
            it.Image.Width = size - press;
            it.Image.Height = size - press;
            Canvas.SetLeft(it.Image, x + press / 2);
            Canvas.SetTop(it.Image, iconBottom - size + press);
            it.Image.Opacity = it.PressOffset > 0 ? 0.7 : 1;
            x += size + Gap;
        }
    }

    private void UpdateLabel(Point? mouse)
    {
        DockItem? hot = null;
        if (_hovering && mouse is Point m)
            hot = _items.FirstOrDefault(it => m.X >= it.Left - Gap / 2 && m.X < it.Left + it.Width + Gap / 2);

        if (hot == null)
        {
            NameLabel.Visibility = Visibility.Collapsed;
            return;
        }

        NameLabelText.Text = hot.App.Name;
        NameLabel.Visibility = Visibility.Visible;
        NameLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = NameLabel.DesiredSize;

        double iconTop = Canvas.GetTop(hot.Image);
        double left = hot.Left + hot.Width / 2 - size.Width / 2;
        left = Math.Clamp(left, 0, Math.Max(0, W - size.Width));
        Canvas.SetLeft(NameLabel, Math.Round(left));
        Canvas.SetTop(NameLabel, Math.Round(Math.Max(0, iconTop - size.Height - 8)));
    }

    // ---------------- Mouse ----------------

    /// <summary>Posição do cursor em DIPs relativa à janela (funciona mesmo fora dela).</summary>
    private Point? GetMouse()
    {
        if (PresentationSource.FromVisual(this) == null) return null;
        if (!GetCursorPos(out POINT pt)) return null;
        try { return PointFromScreen(new Point(pt.X, pt.Y)); }
        catch (InvalidOperationException) { return null; }
    }

    private bool IsMouseInDock() => GetMouse() is Point p && IsInside(p, false);

    /// <summary>
    /// Para entrar, o mouse precisa estar sobre o fundo do Dock; uma vez dentro,
    /// pode subir até o topo da janela (onde ficam os ícones ampliados) sem sair.
    /// </summary>
    private bool IsInside(Point p, bool alreadyInside)
    {
        double h = H;
        double top = alreadyInside ? 0 : h - BottomMargin - BaseSize - DockPadding * 2;
        return p.X >= _dockLeft && p.X <= _dockLeft + _dockWidth && p.Y >= top && p.Y <= h;
    }

    // ---------------- Menu e arrastar-e-soltar ----------------

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        var apps = PinnedAppsStore.Load();
        foreach (var f in files)
        {
            string name = Directory.Exists(f) ? new DirectoryInfo(f).Name : Path.GetFileNameWithoutExtension(f);
            apps.Add(new PinnedApp { Name = name, Path = f });
        }
        PinnedAppsStore.Save(apps);
        LoadItems();
    }

    private void EditPinned_Click(object sender, RoutedEventArgs e)
    {
        PinnedAppsStore.Save(PinnedAppsStore.Load()); // garante que o arquivo existe
        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{PinnedAppsStore.FilePath}\"") { UseShellExecute = true });
        }
        catch { /* ignora */ }
    }

    private void Reload_Click(object sender, RoutedEventArgs e) => LoadItems();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
