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
        _icon = LoadIcon(entry);
    }

    private static ImageSource? LoadIcon(DockEntry e) => CustomIcons.Load(e.Icon) ?? IconLoader.LoadForDisplay(e.Path, 30);

    public string Name => string.IsNullOrWhiteSpace(Entry.Name) ? Loc.T("Item_NoName") : Entry.Name;
    public string Path => Entry.Path;
    public ImageSource? Icon => _icon;

    public void Refresh(bool reloadIcon)
    {
        if (reloadIcon) _icon = LoadIcon(Entry);
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
        PreviewKeyDown += (_, e) =>
        {
            // Ctrl+Z desfaz a última remoção (dentro de uma caixa de texto, fica o desfazer do texto).
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
            {
                e.Handled = true;
                UndoRemoval();
            }
        };
        SourceInitialized += (_, _) => ApplyTitleBarTheme();
        SettingsStore.Changed += OnExternalChange;
        Loc.Changed += OnLanguageChanged;
        Action<string> onFail = _ => Dispatcher.BeginInvoke(UpdateSaveBanner);
        Action onRecover = () => Dispatcher.BeginInvoke(UpdateSaveBanner);
        SettingsStore.SaveFailed += onFail;
        SettingsStore.SaveRecovered += onRecover;
        Loaded += (_, _) => UpdateSaveBanner();
        Action onShortcut = () => Dispatcher.BeginInvoke(UpdateShortcutHint);
        DockWindow.ShortcutStatusChanged += onShortcut;
        EventHandler onDisplay = (_, _) => Dispatcher.BeginInvoke(UpdateMonitorControls);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += onDisplay;
        Closed += (_, _) =>
        {
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= onDisplay;
            DockWindow.ShortcutStatusChanged -= onShortcut;
            SettingsStore.Changed -= OnExternalChange;
            Loc.Changed -= OnLanguageChanged;
            SettingsStore.SaveFailed -= onFail;
            SettingsStore.SaveRecovered -= onRecover;
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
            int dark = DockTheme.Current(A).IsLight ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch { }
    }

    /// <summary>Mostra ou esconde a faixa de "não foi possível salvar".</summary>
    private void UpdateSaveBanner()
    {
        string? error = SettingsStore.LastSaveError;
        SaveErrorBanner.Visibility = error == null ? Visibility.Collapsed : Visibility.Visible;
        SaveErrorText.Text = error?.Replace("\n\n", " ") ?? "";
    }

    private void RetrySave_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.Save();
        UpdateSaveBanner();
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
            // Itens: reaproveita os já carregados para não ler os ícones do disco de novo
            // a cada clique de cor ou mudança de estilo.
            var loaded = new Dictionary<DockEntry, ItemVm>(ReferenceEqualityComparer.Instance);
            foreach (var vm in _items) loaded[vm.Entry] = vm;
            _items.Clear();
            foreach (var e in S.Items)
                _items.Add(loaded.TryGetValue(e, out var vm) ? vm : new ItemVm(e));

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
            BlurToggle.IsChecked = A.Blur;
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
            UpdateMoveTooltips();
            MonitorPrimary.IsChecked = A.Monitor == MonitorMode.Primary;
            MonitorChoose.IsChecked = A.Monitor == MonitorMode.Choose;
            MonitorFollow.IsChecked = A.Monitor == MonitorMode.FollowMouse;
            UpdateMonitorControls();

            // Comportamento
            LabelsToggle.IsChecked = A.ShowLabels;
            RunningToggle.IsChecked = A.ShowRunning;
            UnpinnedToggle.IsChecked = A.ShowRunningUnpinned;
            PreviewsToggle.IsChecked = A.WindowPreviews;
            PreviewDelaySlider.Value = A.PreviewDelay;
            BounceToggle.IsChecked = A.BounceOnLaunch;
            MinimizeNone.IsChecked = A.MinimizeEffect == MinimizeEffect.None;
            MinimizeScale.IsChecked = A.MinimizeEffect == MinimizeEffect.Scale;
            MinimizeGenie.IsChecked = A.MinimizeEffect == MinimizeEffect.Genie;
            AttentionToggle.IsChecked = A.BounceOnAttention;
            BadgesToggle.IsChecked = A.ShowBadges;
            SeparatorToggle.IsChecked = A.ShowSeparator;
            ReserveToggle.IsChecked = A.ReserveSpace;
            AutoHideToggle.IsChecked = A.AutoHide;
            HideTaskbarToggle.IsChecked = A.HideTaskbar;
            AutoHideDelaySlider.Value = A.AutoHideDelay;
            UpdateAutoHideControls();
            AutostartToggle.IsChecked = Autostart.IsEnabled;
            BuildShortcutChoices();
            BuildLanguageChoices();
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
        RestoreIconButton.IsEnabled = vm?.Entry.Icon != null;
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
        bool added = false;
        foreach (var e in entries)
        {
            // Já fixado (mesmo caminho ou atalho para o mesmo app): seleciona o existente em vez de duplicar.
            if (DockItems.FindDuplicate(S.Items, e.Path, e.Arguments) is DockEntry existing)
            {
                last = _items.FirstOrDefault(i => i.Entry == existing) ?? last;
                continue;
            }
            S.Items.Add(e);
            last = new ItemVm(e);
            _items.Add(last);
            added = true;
        }
        if (last == null) return;
        if (added) Commit();
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
            Title = Loc.T("Dlg_PickApp"),
            Filter = Loc.T("Dlg_FilterApps"),
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) == true)
            AddEntries(dlg.FileNames.Select(f => new DockEntry { Name = System.IO.Path.GetFileNameWithoutExtension(f), Path = f }));
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Dlg_PickFolder"), Multiselect = true };
        if (dlg.ShowDialog(this) == true)
            AddEntries(dlg.FolderNames.Select(f => new DockEntry { Name = new DirectoryInfo(f).Name, Path = f }));
    }

    private void ChangeIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not ItemVm vm || !CustomIcons.Choose(this, vm.Entry)) return;
        vm.Refresh(reloadIcon: true);
        RestoreIconButton.IsEnabled = true;
        Commit();
    }

    private void RestoreIcon_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not ItemVm vm || vm.Entry.Icon == null) return;
        CustomIcons.Restore(vm.Entry);
        vm.Refresh(reloadIcon: true);
        RestoreIconButton.IsEnabled = false;
        Commit();
    }

    private void BrowsePath_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        var dlg = new OpenFileDialog { Filter = Loc.T("Dlg_FilterAppsLnk") };
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

    /// <summary>As setas falam de "antes/depois" no sentido do Dock: esquerda/direita ou acima/abaixo.</summary>
    private void UpdateMoveTooltips()
    {
        bool horizontal = A.Edge is DockEdge.Bottom or DockEdge.Top;
        MoveUpButton.ToolTip = Loc.T(horizontal ? "Move_BeforeLeft" : "Move_BeforeUp");
        MoveDownButton.ToolTip = Loc.T(horizontal ? "Move_AfterRight" : "Move_AfterDown");
        System.Windows.Automation.AutomationProperties.SetName(MoveUpButton, (string)MoveUpButton.ToolTip);
        System.Windows.Automation.AutomationProperties.SetName(MoveDownButton, (string)MoveDownButton.ToolTip);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);

    private void UndoRemoval()
    {
        var entry = RemovalHistory.Undo(S.Items);
        if (entry == null) return;
        Commit();
        LoadAll();
        var vm = _items.FirstOrDefault(i => i.Entry == entry);
        if (vm != null)
        {
            NavItems.IsChecked = true;
            ItemsList.SelectedItem = vm;
            ItemsList.ScrollIntoView(vm);
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not ItemVm vm) return;
        int i = _items.IndexOf(vm);
        _items.Remove(vm);
        RemovalHistory.Remove(S.Items, vm.Entry);
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
            arrows.Children.Add(IconButton("", Loc.T("Move_Up"), () => MoveWidget(index, -1)));
            arrows.Children.Add(IconButton("", Loc.T("Move_Down"), () => MoveWidget(index, +1)));

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

    private void MinimizeEffect_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        A.MinimizeEffect = MinimizeNone.IsChecked == true ? MinimizeEffect.None
                         : MinimizeGenie.IsChecked == true ? MinimizeEffect.Genie
                         : MinimizeEffect.Scale;
        Commit();
    }

    private void Edge_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        A.Edge = EdgeLeft.IsChecked == true ? DockEdge.Left
               : EdgeRight.IsChecked == true ? DockEdge.Right
               : EdgeTop.IsChecked == true ? DockEdge.Top
               : DockEdge.Bottom;
        UpdateMonitorControls();
        Commit();
    }

    private void BuildLanguageChoices()
    {
        LanguageChoices.Children.Clear();
        var style = (Style)FindResource("UiSegment");
        var options = new List<(string Code, string Name)> { ("", Loc.F("Language_Auto", Loc.AutoName)) };
        options.AddRange(Loc.Languages);
        foreach (var (code, name) in options)
        {
            var rb = new RadioButton
            {
                Style = style,
                GroupName = "language",
                Content = name,
                IsChecked = S.Language == code,
            };
            // Nome do idioma na própria língua, para quem não entende a atual achar o seu.
            System.Windows.Automation.AutomationProperties.SetName(rb, code == "" ? name : $"{Loc.T("S_Language")}: {name}");
            rb.Checked += (_, _) =>
            {
                if (_loading || S.Language == code) return;
                S.Language = code;
                Commit();
            };
            LanguageChoices.Children.Add(rb);
        }
    }

    /// <summary>Idioma mudou: reabre a janela no mesmo lugar e na mesma página, com os textos novos.</summary>
    private void OnLanguageChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var bounds = (Left, Top, Width, Height, WindowState);
            string page = NavItems.IsChecked == true ? "items" : NavWidgets.IsChecked == true ? "widgets"
                : NavStyle.IsChecked == true ? "style" : "behavior";
            Close();
            ShowSingle();
            if (_instance is not SettingsWindow w) return;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            (w.Left, w.Top, w.Width, w.Height) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            if (bounds.WindowState == WindowState.Maximized) w.WindowState = WindowState.Maximized;
            (page switch { "items" => w.NavItems, "widgets" => w.NavWidgets, "style" => w.NavStyle, _ => w.NavBehavior }).IsChecked = true;
        });
    }

    private void BuildShortcutChoices()
    {
        ShortcutChoices.Children.Clear();
        var style = (Style)FindResource("UiSegment");
        foreach (var choice in Hotkey.Choices)
        {
            var rb = new RadioButton
            {
                Style = style,
                GroupName = "shortcut",
                Content = Hotkey.Display(choice),
                IsChecked = string.Equals(choice, A.KeyboardShortcut, StringComparison.OrdinalIgnoreCase),
            };
            System.Windows.Automation.AutomationProperties.SetName(rb, Loc.F("Shortcut_A11y", Hotkey.Display(choice)));
            rb.Checked += (_, _) =>
            {
                if (_loading || A.KeyboardShortcut == choice) return;
                A.KeyboardShortcut = choice;
                Commit();
            };
            ShortcutChoices.Children.Add(rb);
        }
        UpdateShortcutHint();
    }

    private void UpdateShortcutHint()
    {
        ShortcutHint.Text = DockWindow.ShortcutError ?? "";
        ShortcutHint.Visibility = ShortcutHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MonitorMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        A.Monitor = MonitorChoose.IsChecked == true ? MonitorMode.Choose
                  : MonitorFollow.IsChecked == true ? MonitorMode.FollowMouse
                  : MonitorMode.Primary;
        // Ao escolher pela primeira vez, começa no monitor onde esta janela está.
        if (A.Monitor == MonitorMode.Choose && Screens.Find(Monitors.All(), A.MonitorDevice) == null)
            A.MonitorDevice = MonitorOfThisWindow() ?? "";
        UpdateMonitorControls();
        Commit();
    }

    private string? MonitorOfThisWindow()
    {
        var screens = Monitors.All();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT r)) return screens.FirstOrDefault(s => s.Primary)?.Device;
        int cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
        return screens.FirstOrDefault(s => cx >= s.Bounds.Left && cx < s.Bounds.Right && cy >= s.Bounds.Top && cy < s.Bounds.Bottom)?.Device
               ?? screens.FirstOrDefault(s => s.Primary)?.Device;
    }

    /// <summary>Lista de monitores (modo Escolher) e a dica abaixo dela.</summary>
    private void UpdateMonitorControls()
    {
        var screens = Monitors.All();
        bool choose = A.Monitor == MonitorMode.Choose;
        MonitorListBorder.Visibility = choose ? Visibility.Visible : Visibility.Collapsed;

        MonitorList.Children.Clear();
        if (choose)
        {
            var style = (Style)FindResource("UiSegment");
            foreach (var screen in screens)
            {
                var rb = new RadioButton
                {
                    Style = style,
                    GroupName = "monitorPick",
                    Content = screen.Label,
                    ToolTip = screen.Device,
                    IsChecked = string.Equals(screen.Device, A.MonitorDevice, StringComparison.OrdinalIgnoreCase),
                };
                string device = screen.Device;
                rb.Checked += (_, _) =>
                {
                    if (string.Equals(A.MonitorDevice, device, StringComparison.OrdinalIgnoreCase)) return;
                    A.MonitorDevice = device;
                    Commit();
                };
                MonitorList.Children.Add(rb);
            }
        }

        string followHint = A.Edge switch
        {
            DockEdge.Top => "Monitor_FollowTop",
            DockEdge.Left => "Monitor_FollowLeft",
            DockEdge.Right => "Monitor_FollowRight",
            _ => "Monitor_FollowBottom",
        };
        MonitorHint.Text = A.Monitor switch
        {
            MonitorMode.Choose when Screens.Find(screens, A.MonitorDevice) == null =>
                Loc.T("Monitor_Missing"),
            MonitorMode.FollowMouse when screens.Count < 2 =>
                Loc.T("Monitor_OnlyOne"),
            MonitorMode.FollowMouse =>
                Loc.T(followHint),
            _ => "",
        };
        MonitorHint.Visibility = MonitorHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Os sliders disparam ValueChanged enquanto o XAML carrega (Minimum ajusta o Value),
        // antes de os textos de valor que vêm depois existirem.
        if (!IsInitialized) return;
        UpdateSliderLabels();
        if (_loading) return;
        A.Opacity = Math.Round(OpacitySlider.Value, 2);
        A.CornerRadius = Math.Round(RadiusSlider.Value);
        A.IconSize = Math.Round(SizeSlider.Value);
        A.Magnification = Math.Round(MagSlider.Value, 2);
        A.AutoHideDelay = Math.Round(AutoHideDelaySlider.Value, 2);
        A.PreviewDelay = Math.Round(PreviewDelaySlider.Value, 2);
        Commit(debounce: true);
    }

    private void UpdateSliderLabels()
    {
        OpacityValue.Text = $"{OpacitySlider.Value * 100:0}%";
        RadiusValue.Text = $"{RadiusSlider.Value:0} px";
        SizeValue.Text = $"{SizeSlider.Value:0} px";
        MagValue.Text = $"{MagSlider.Value:0.0}x";
        AutoHideDelayValue.Text = $"{AutoHideDelaySlider.Value:0.0} s";
        PreviewDelayValue.Text = $"{PreviewDelaySlider.Value:0.0} s";
    }

    // ================= Comportamento =================

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        A.Magnify = MagnifyToggle.IsChecked == true;
        MagSlider.IsEnabled = A.Magnify;
        A.ShowLabels = LabelsToggle.IsChecked == true;
        A.ShowRunning = RunningToggle.IsChecked == true;
        A.ShowRunningUnpinned = UnpinnedToggle.IsChecked == true;
        A.WindowPreviews = PreviewsToggle.IsChecked == true;
        A.BounceOnLaunch = BounceToggle.IsChecked == true;
        A.BounceOnAttention = AttentionToggle.IsChecked == true;
        A.ShowBadges = BadgesToggle.IsChecked == true;
        A.ShowSeparator = SeparatorToggle.IsChecked == true;
        A.ReserveSpace = ReserveToggle.IsChecked == true;
        A.AutoHide = AutoHideToggle.IsChecked == true;
        A.HideTaskbar = HideTaskbarToggle.IsChecked == true;
        A.Blur = BlurToggle.IsChecked == true;
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
        ReserveToggle.ToolTip = on ? Loc.T("Reserve_AutoHideTip") : null;
        bool previews = PreviewsToggle.IsChecked == true;
        PreviewDelaySlider.IsEnabled = previews;
        PreviewDelayRow.Opacity = previews ? 1 : 0.5;
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        bool enable = AutostartToggle.IsChecked == true;
        string? exe = Environment.ProcessPath;
        if (enable && exe != null && Autostart.IsInFolders(exe, Autostart.VolatileFolders()))
        {
            // Rodando de Downloads, Temp ou Área de Trabalho: o arquivo pode sumir ou ser trocado.
            string folder = System.IO.Path.GetDirectoryName(exe) ?? exe;
            if (Autostart.IsSingleFile(exe))
            {
                var answer = MessageBox.Show(this,
                    Loc.F("Autostart_Volatile_Ask", folder, System.IO.Path.GetDirectoryName(Autostart.InstallPath)),
                    "Dockbox", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
                if (answer == MessageBoxResult.Cancel) { AutostartToggle.IsChecked = Autostart.IsEnabled; return; }
                if (answer == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (_commitDebounce.IsEnabled) { _commitDebounce.Stop(); SettingsStore.Commit(); }
                        Autostart.InstallAndRelaunch(exe);
                        Application.Current.Shutdown();
                        return;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, Loc.F("Autostart_CopyFailed", ex.Message),
                            "Dockbox", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            else
            {
                MessageBox.Show(this, Loc.F("Autostart_FolderWarn", folder), "Dockbox", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        Autostart.Set(enable);
        AutostartToggle.IsChecked = Autostart.IsEnabled;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(SettingsStore.FolderPath);
        AppLauncher.Open(SettingsStore.FolderPath);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, Loc.T("Reset_Confirm"),
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
