using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Dockbox.Core;
using Dockbox.Services;
using Dockbox.Widgets;
using Microsoft.Win32;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Views;

public sealed class ItemVm : INotifyPropertyChanged
{
    public DockEntry Entry { get; }
    private ImageSource? _icon;

    public ItemVm(DockEntry entry)
    {
        Entry = entry;
        _icon = IconLoader.Load(entry.Path, 64);
    }

    public string Name => string.IsNullOrWhiteSpace(Entry.Name) ? "(sem nome)" : Entry.Name;
    public string Path => Entry.Path;
    public ImageSource? Icon => _icon;

    public void Refresh(bool reloadIcon)
    {
        if (reloadIcon) _icon = IconLoader.Load(Entry.Path, 64);
        OnChanged(nameof(Name));
        OnChanged(nameof(Path));
        if (reloadIcon) OnChanged(nameof(Icon));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public partial class SettingsWindow : Window
{
    private static SettingsWindow? _instance;

    public static void ShowSingle()
    {
        if (_instance == null)
        {
            _instance = new SettingsWindow();
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
        _instance.Activate();
    }

    private static readonly string[] BgPalette =
        { "#1E1E22", "#2A2D3A", "#1F2A44", "#203A2E", "#3A2030", "#3B2F20", "#F2F2F6", "#E8EEF8", "#F6EEE6" };
    private static readonly string[] AccentPalette =
        { "#0A84FF", "#5E5CE6", "#BF5AF2", "#FF375F", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#FFFFFF" };

    private readonly ObservableCollection<ItemVm> _items = new();
    private readonly DispatcherTimer _commitDebounce;
    private bool _loading;
    private bool _editing;

    private DockSettings S => SettingsStore.Current;
    private Appearance A => S.Appearance;

    public SettingsWindow()
    {
        InitializeComponent();
        _commitDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _commitDebounce.Tick += (_, _) =>
        {
            _commitDebounce.Stop();
            _editing = true;
            try { SettingsStore.Commit(); }
            finally { _editing = false; }
        };

        ItemsList.ItemsSource = _items;
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        SettingsStore.Changed += OnExternalChange;
        Closed += (_, _) =>
        {
            SettingsStore.Changed -= OnExternalChange;
            if (_commitDebounce.IsEnabled) { _commitDebounce.Stop(); SettingsStore.Commit(); }
        };

        LoadAll();
    }

    /// <summary>Barra de título escura quando o tema é escuro (Windows 10 20H1+ / 11).</summary>
    private void ApplyTitleBarTheme()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int dark = DockTheme.From(A).IsLight ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch { }
    }

    private void OnExternalChange()
    {
        // O Dock mudou algo (ex.: removeu um item pelo menu). Recarrega sem perder a seleção.
        if (_editing) return;
        Dispatcher.BeginInvoke(() =>
        {
            ApplyTitleBarTheme();
            var selected = (ItemsList.SelectedItem as ItemVm)?.Entry;
            LoadAll();
            var again = _items.FirstOrDefault(i => i.Entry == selected);
            if (again != null) ItemsList.SelectedItem = again;
        });
    }

    private void Commit(bool debounce = false)
    {
        _editing = true;
        try
        {
            if (debounce)
            {
                _commitDebounce.Stop();
                _commitDebounce.Start();
            }
            else
            {
                SettingsStore.Commit();
            }
        }
        finally
        {
            _editing = false;
        }
    }

    private void LoadAll()
    {
        _loading = true;
        try
        {
            // Itens
            _items.Clear();
            foreach (var e in S.Items) _items.Add(new ItemVm(e));

            // Widgets
            BuildWidgetRows();
            WeatherCity.Text = S.WeatherCity;
            Clock24h.IsChecked = S.Clock24h;
            WeatherAuto.IsChecked = S.WeatherAutoLocate;

            // Estilo
            ThemeAuto.IsChecked = A.Theme == ThemeMode.Auto;
            ThemeLight.IsChecked = A.Theme == ThemeMode.Light;
            ThemeDark.IsChecked = A.Theme == ThemeMode.Dark;
            ThemeCustom.IsChecked = A.Theme == ThemeMode.Custom;
            CustomColorPanel.Visibility = A.Theme == ThemeMode.Custom ? Visibility.Visible : Visibility.Collapsed;
            BuildSwatches(BgSwatches, BgPalette, A.CustomColor, c => { A.CustomColor = c; A.Theme = ThemeMode.Custom; });
            BuildSwatches(AccentSwatches, AccentPalette, A.AccentColor, c => A.AccentColor = c);
            BgHex.Text = A.CustomColor;
            AccentHex.Text = A.AccentColor;

            OpacitySlider.Value = A.Opacity;
            RadiusSlider.Value = A.CornerRadius;
            SizeSlider.Value = A.IconSize;
            MagSlider.Value = Math.Max(1.1, A.Magnification);
            MagnifyToggle.IsChecked = A.Magnify;
            MagSlider.IsEnabled = A.Magnify;
            UpdateSliderLabels();

            EdgeBottom.IsChecked = A.Edge == DockEdge.Bottom;
            EdgeLeft.IsChecked = A.Edge == DockEdge.Left;
            EdgeRight.IsChecked = A.Edge == DockEdge.Right;
            EdgeTop.IsChecked = A.Edge == DockEdge.Top;

            // Comportamento
            LabelsToggle.IsChecked = A.ShowLabels;
            RunningToggle.IsChecked = A.ShowRunning;
            UnpinnedToggle.IsChecked = A.ShowRunningUnpinned;
            BounceToggle.IsChecked = A.BounceOnLaunch;
            SeparatorToggle.IsChecked = A.ShowSeparator;
            ReserveToggle.IsChecked = A.ReserveSpace;
            AutoHideToggle.IsChecked = A.AutoHide;
            AutoHideDelaySlider.Value = A.AutoHideDelay;
            UpdateAutoHideControls();
            AutostartToggle.IsChecked = Autostart.IsEnabled;
        }
        finally
        {
            _loading = false;
        }
    }

    // ================= Navegação =================

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageItems == null) return; // ainda carregando o XAML
        PageItems.Visibility = NavItems.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageWidgets.Visibility = NavWidgets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageStyle.Visibility = NavStyle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageBehavior.Visibility = NavBehavior.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ================= Itens =================

    private ItemVm? Selected => ItemsList.SelectedItem as ItemVm;

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = Selected;
        _loading = true;
        EditCard.IsEnabled = vm != null;
        EditName.Text = vm?.Entry.Name ?? "";
        EditPath.Text = vm?.Entry.Path ?? "";
        EditArgs.Text = vm?.Entry.Arguments ?? "";
        _loading = false;
    }

    private void Edit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || Selected is not ItemVm vm) return;
        vm.Entry.Name = EditName.Text;
        string? args = string.IsNullOrWhiteSpace(EditArgs.Text) ? null : EditArgs.Text;
        if (args != vm.Entry.Arguments) vm.Entry.Trusted = false; // comando mudou: pergunta de novo
        vm.Entry.Arguments = args;
        vm.Refresh(false);
        Commit(debounce: true);
    }

    /// <summary>O caminho só é aplicado ao sair do campo ou apertar Enter (evita carregar ícone a cada tecla).</summary>
    private void EditPath_Commit(object sender, RoutedEventArgs e)
    {
        if (_loading || Selected is not ItemVm vm) return;
        string path = EditPath.Text.Trim();
        if (path == vm.Entry.Path) return;
        if (string.IsNullOrWhiteSpace(path)) { EditPath.Text = vm.Entry.Path; return; }
        vm.Entry.Path = path;
        vm.Entry.Trusted = false; // caminho mudou: pergunta de novo se for script
        bool isNetwork = path.StartsWith(@"\\", StringComparison.Ordinal);
        vm.Refresh(!isNetwork && (File.Exists(path) || Directory.Exists(path)));
        Commit();
    }

    private void EditPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) EditPath_Commit(sender, e);
    }

    private void AddEntries(IEnumerable<DockEntry> entries)
    {
        ItemVm? last = null;
        foreach (var e in entries)
        {
            S.Items.Add(e);
            last = new ItemVm(e);
            _items.Add(last);
        }
        if (last == null) return;
        Commit();
        ItemsList.SelectedItem = last;
        ItemsList.ScrollIntoView(last);
    }

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        var picker = new AppPickerWindow { Owner = this };
        if (picker.ShowDialog() == true)
            AddEntries(picker.Selected.Select(a => new DockEntry { Name = a.Name, Path = a.Path }));
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Escolha um app ou arquivo",
            Filter = "Apps e atalhos (*.exe;*.lnk;*.url)|*.exe;*.lnk;*.url|Todos os arquivos (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true)
            AddEntries(dlg.FileNames.Select(f => new DockEntry { Name = System.IO.Path.GetFileNameWithoutExtension(f), Path = f }));
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Escolha uma pasta", Multiselect = true };
        if (dlg.ShowDialog(this) == true)
            AddEntries(dlg.FolderNames.Select(f => new DockEntry { Name = new DirectoryInfo(f).Name, Path = f }));
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        var dlg = new OpenFileDialog { Filter = "Apps e atalhos (*.exe;*.lnk)|*.exe;*.lnk|Todos os arquivos (*.*)|*.*" };
        if (dlg.ShowDialog(this) == true)
        {
            EditPath.Text = dlg.FileName;
            EditPath_Commit(EditPath, e);
        }
    }

    private void Move(int delta)
    {
        if (Selected is not ItemVm vm) return;
        int i = _items.IndexOf(vm), j = i + delta;
        if (j < 0 || j >= _items.Count) return;
        _items.Move(i, j);
        S.Items.RemoveAt(i);
        S.Items.Insert(j, vm.Entry);
        ItemsList.SelectedItem = vm;
        Commit();
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not ItemVm vm) return;
        int i = _items.IndexOf(vm);
        _items.Remove(vm);
        S.Items.Remove(vm.Entry);
        Commit();
        if (_items.Count > 0) ItemsList.SelectedIndex = Math.Min(i, _items.Count - 1);
    }

    // ================= Widgets =================

    private void BuildWidgetRows()
    {
        WidgetsPanel.Children.Clear();
        for (int idx = 0; idx < S.Widgets.Count; idx++)
        {
            var cfg = S.Widgets[idx];
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(9),
                Background = (Brush)FindResource("UiInputBg"),
                Child = new TextBlock
                {
                    Text = WidgetFactory.Glyph(cfg.Kind),
                    FontFamily = (FontFamily)FindResource("UiIconFont"),
                    FontSize = 16,
                    Foreground = (Brush)FindResource("UiAccent"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
            text.Children.Add(new TextBlock { Text = WidgetFactory.DisplayName(cfg.Kind), Style = (Style)FindResource("UiH2") });
            text.Children.Add(new TextBlock { Text = WidgetFactory.Description(cfg.Kind), Style = (Style)FindResource("UiSub") });

            var arrows = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            int index = idx;
            arrows.Children.Add(IconButton("", "Mover para cima", () => MoveWidget(index, -1)));
            arrows.Children.Add(IconButton("", "Mover para baixo", () => MoveWidget(index, +1)));

            var toggle = new CheckBox { Style = (Style)FindResource("UiToggle"), IsChecked = cfg.Enabled, VerticalAlignment = VerticalAlignment.Center };
            toggle.Click += (_, _) => { cfg.Enabled = toggle.IsChecked == true; Commit(); };

            Grid.SetColumn(text, 1);
            Grid.SetColumn(arrows, 2);
            Grid.SetColumn(toggle, 3);
            row.Children.Add(icon);
            row.Children.Add(text);
            row.Children.Add(arrows);
            row.Children.Add(toggle);

            WidgetsPanel.Children.Add(new Border { Style = (Style)FindResource("UiCard"), Child = row, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12, 10, 16, 10) });
        }
    }

    private Button IconButton(string glyph, string tip, Action action)
    {
        var b = new Button { Style = (Style)FindResource("UiIconButton"), Content = glyph, ToolTip = tip, Margin = new Thickness(4, 0, 0, 0) };
        b.Click += (_, _) => action();
        return b;
    }

    private void MoveWidget(int index, int delta)
    {
        int j = index + delta;
        if (j < 0 || j >= S.Widgets.Count) return;
        var w = S.Widgets[index];
        S.Widgets.RemoveAt(index);
        S.Widgets.Insert(j, w);
        Commit();
        BuildWidgetRows();
    }

    private void WeatherApply_Click(object sender, RoutedEventArgs e)
    {
        S.WeatherCity = WeatherCity.Text.Trim();
        Commit();
    }

    private void WeatherAuto_Click(object sender, RoutedEventArgs e)
    {
        S.WeatherAutoLocate = WeatherAuto.IsChecked == true;
        Commit();
    }

    private void Clock24h_Click(object sender, RoutedEventArgs e)
    {
        S.Clock24h = Clock24h.IsChecked == true;
        Commit();
    }

    // ================= Estilo =================

    private void BuildSwatches(WrapPanel panel, string[] colors, string current, Action<string> apply)
    {
        panel.Children.Clear();
        foreach (var hex in colors)
        {
            var c = ColorUtil.Parse(hex, Colors.Gray);
            bool selected = string.Equals(hex, current, StringComparison.OrdinalIgnoreCase);
            var sw = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(c),
                BorderBrush = selected ? (Brush)FindResource("UiFg") : new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                BorderThickness = new Thickness(selected ? 2.5 : 1),
                Cursor = Cursors.Hand,
                ToolTip = hex,
            };
            sw.MouseLeftButtonUp += (_, _) =>
            {
                apply(hex);
                Commit();
                LoadAll();
            };
            panel.Children.Add(sw);
        }
    }

    private void Hex_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            if (tb == BgHex) BgHex_LostFocus(tb, e);
            else AccentHex_LostFocus(tb, e);
        }
    }

    private static string? NormalizeHex(string text)
    {
        string t = text.Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (!ColorUtil.IsValid(t)) return null;
        return ColorUtil.ToHex(ColorUtil.Parse(t, Colors.Black));
    }

    private void BgHex_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var hex = NormalizeHex(BgHex.Text);
        if (hex == null) { BgHex.Text = A.CustomColor; return; }
        if (hex == A.CustomColor) return;
        A.CustomColor = hex;
        A.Theme = ThemeMode.Custom;
        Commit();
        LoadAll();
    }

    private void AccentHex_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var hex = NormalizeHex(AccentHex.Text);
        if (hex == null) { AccentHex.Text = A.AccentColor; return; }
        if (hex == A.AccentColor) return;
        A.AccentColor = hex;
        Commit();
        LoadAll();
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        A.Theme = ThemeLight.IsChecked == true ? ThemeMode.Light
                : ThemeDark.IsChecked == true ? ThemeMode.Dark
                : ThemeCustom.IsChecked == true ? ThemeMode.Custom
                : ThemeMode.Auto;
        CustomColorPanel.Visibility = A.Theme == ThemeMode.Custom ? Visibility.Visible : Visibility.Collapsed;
        Commit();
    }

    private void Edge_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        A.Edge = EdgeLeft.IsChecked == true ? DockEdge.Left
               : EdgeRight.IsChecked == true ? DockEdge.Right
               : EdgeTop.IsChecked == true ? DockEdge.Top
               : DockEdge.Bottom;
        Commit();
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityValue == null) return; // XAML ainda carregando
        UpdateSliderLabels();
        if (_loading) return;
        A.Opacity = Math.Round(OpacitySlider.Value, 2);
        A.CornerRadius = Math.Round(RadiusSlider.Value);
        A.IconSize = Math.Round(SizeSlider.Value);
        A.Magnification = Math.Round(MagSlider.Value, 2);
        A.AutoHideDelay = Math.Round(AutoHideDelaySlider.Value, 2);
        Commit(debounce: true);
    }

    private void UpdateSliderLabels()
    {
        OpacityValue.Text = $"{OpacitySlider.Value * 100:0}%";
        RadiusValue.Text = $"{RadiusSlider.Value:0} px";
        SizeValue.Text = $"{SizeSlider.Value:0} px";
        MagValue.Text = $"{MagSlider.Value:0.0}x";
        AutoHideDelayValue.Text = $"{AutoHideDelaySlider.Value:0.0} s";
    }

    // ================= Comportamento =================

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        A.Magnify = MagnifyToggle.IsChecked == true;
        MagSlider.IsEnabled = A.Magnify;
        A.ShowLabels = LabelsToggle.IsChecked == true;
        A.ShowRunning = RunningToggle.IsChecked == true;
        A.ShowRunningUnpinned = UnpinnedToggle.IsChecked == true;
        A.BounceOnLaunch = BounceToggle.IsChecked == true;
        A.ShowSeparator = SeparatorToggle.IsChecked == true;
        A.ReserveSpace = ReserveToggle.IsChecked == true;
        A.AutoHide = AutoHideToggle.IsChecked == true;
        UpdateAutoHideControls();
        Commit();
    }

    /// <summary>Com auto-ocultar, o tempo fica ativo e a reserva de espaço não se aplica.</summary>
    private void UpdateAutoHideControls()
    {
        bool on = AutoHideToggle.IsChecked == true;
        AutoHideDelaySlider.IsEnabled = on;
        AutoHideDelayRow.Opacity = on ? 1 : 0.5;
        ReserveToggle.IsEnabled = !on;
        ReserveToggle.ToolTip = on ? "Com o auto-ocultar ligado o Dock não reserva espaço na tela." : null;
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        Autostart.Set(AutostartToggle.IsChecked == true);
        AutostartToggle.IsChecked = Autostart.IsEnabled;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.FolderPath);
        AppLauncher.Open(SettingsStore.FolderPath);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, "Restaurar estilo, widgets e comportamento para o padrão?\nSeus apps fixados serão mantidos.",
            "Dockbox", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        S.Appearance = new Appearance();
        S.Widgets.Clear();
        S.WeatherCity = "";
        S.Clock24h = true;
        Commit();
        LoadAll();
    }
}
