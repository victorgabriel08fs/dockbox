using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dockbox.Core;
using Dockbox.Services;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Views;

/// <summary>
/// Busca estilo Spotlight: digite e Enter abre o app ou arquivo. Procura nos itens do Dock, nos apps do Menu
/// Iniciar e nos arquivos recentes, da Área de Trabalho, de Documentos e de Downloads. Fecha com Esc ou ao perder o foco.
/// </summary>
internal sealed class SearchWindow : Window
{
    private static SearchWindow? _open;
    private static List<SearchItem>? _cache;
    private static DateTime _cacheTime;
    private static Task<List<SearchItem>>? _loading;
    private static readonly TimeSpan CacheLife = TimeSpan.FromMinutes(2);

    private readonly TextBox _box;
    private readonly ListBox _list;
    private readonly TextBlock _hint;
    private readonly DockTheme _theme;
    private readonly Dictionary<string, ImageSource?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private bool _closing;

    /// <summary>Abre a busca, ou fecha se já está aberta.</summary>
    public static void Toggle()
    {
        if (_open != null)
        {
            _open.Close();
            return;
        }
        _open = new SearchWindow();
        _open.Closed += (_, _) => _open = null;
        _open.ShowCentered();
    }

    public static void CloseIfOpen() => _open?.Close();

    private SearchWindow()
    {
        _theme = DockTheme.Current(SettingsStore.Current.Appearance);
        Title = Loc.T("Search_Title");
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 640;
        SizeToContent = SizeToContent.Height;

        var fg = _theme.LabelForeground;
        _box = new TextBox
        {
            FontSize = 22,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = fg,
            CaretBrush = fg,
            Padding = new Thickness(6, 4, 6, 4),
            FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI"),
        };
        System.Windows.Automation.AutomationProperties.SetName(_box, Loc.T("Search_Title"));
        _hint = new TextBlock
        {
            Text = Loc.T("Search_Placeholder"),
            FontSize = 22,
            Foreground = fg,
            Opacity = 0.45,
            IsHitTestVisible = false,
            Margin = new Thickness(9, 4, 6, 4),
            FontFamily = _box.FontFamily,
        };
        var glyph = new TextBlock
        {
            Text = "",
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 20,
            Foreground = fg,
            Opacity = 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 8, 0),
        };
        var boxGrid = new Grid();
        boxGrid.Children.Add(_hint);
        boxGrid.Children.Add(_box);
        var top = new DockPanel();
        DockPanel.SetDock(glyph, Dock.Left);
        top.Children.Add(glyph);
        top.Children.Add(boxGrid);

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = fg,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
            Focusable = false,
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.MouseUp += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Left && _list.SelectedItem is ListBoxItem { Tag: SearchItem item }) Open(item, showInFolder: false);
        };

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(_list);
        Content = new Border
        {
            Background = _theme.LabelBackground,
            BorderBrush = _theme.LabelBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(20), // espaço para a sombra
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.35 },
            Child = stack,
        };

        _box.TextChanged += (_, _) => Refresh();
        PreviewKeyDown += OnKey;
        Deactivated += (_, _) => { if (!_closing) Close(); };
        Closing += (_, _) => _closing = true;
        Loaded += (_, _) =>
        {
            Activate();
            _box.Focus();
            Keyboard.Focus(_box);
        };
        EnsureSources();
    }

    private void ShowCentered()
    {
        // No monitor do mouse, no terço de cima, como o Spotlight.
        try
        {
            GetCursorPos(out var p);
            var screen = Monitors.All().FirstOrDefault(s => p.X >= s.Bounds.Left && p.X < s.Bounds.Right && p.Y >= s.Bounds.Top && p.Y < s.Bounds.Bottom)
                ?? Monitors.All().FirstOrDefault(s => s.Primary);
            if (screen != null)
            {
                double scale = screen.Scale > 0 ? screen.Scale : 1;
                double workLeft = screen.Work.Left / scale, workTop = screen.Work.Top / scale;
                double workWidth = (screen.Work.Right - screen.Work.Left) / scale, workHeight = (screen.Work.Bottom - screen.Work.Top) / scale;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = workLeft + (workWidth - Width) / 2;
                Top = workTop + workHeight * 0.22;
            }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        catch { WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        Show();
        // O atalho global e o clique no Dock dão a este processo o direito de pegar o foco.
        try { SetForegroundWindow(new WindowInteropHelper(this).Handle); } catch { }
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (_box.Text.Length > 0) _box.Clear();
                else Close();
                break;
            case Key.Down:
            case Key.Up:
                e.Handled = true;
                if (_list.Items.Count == 0) break;
                int i = _list.SelectedIndex + (e.Key == Key.Down ? 1 : -1);
                _list.SelectedIndex = (i + _list.Items.Count) % _list.Items.Count;
                _list.ScrollIntoView(_list.SelectedItem);
                break;
            case Key.Enter:
                e.Handled = true;
                if (_list.SelectedItem is ListBoxItem { Tag: SearchItem item })
                    Open(item, showInFolder: Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
                break;
        }
    }

    private void Open(SearchItem item, bool showInFolder)
    {
        Close();
        if (showInFolder)
        {
            AppLauncher.ShowInExplorer(item.Path);
            return;
        }
        // Passa pelas mesmas confirmações dos itens do Dock (scripts e links perguntam antes).
        AppLauncher.Launch(new DockEntry { Name = item.Name, Path = item.Path, Arguments = item.Arguments });
    }

    private void Refresh()
    {
        string q = _box.Text;
        _hint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _list.Items.Clear();
        if (q.Trim().Length == 0)
        {
            _list.Visibility = Visibility.Collapsed;
            return;
        }

        var results = SearchRanker.Rank(q, AllItems());
        if (results.Count == 0)
        {
            _list.Items.Add(new ListBoxItem
            {
                Content = new TextBlock { Text = _loading != null ? Loc.T("Search_Loading") : Loc.T("Search_Nothing"), Opacity = 0.6, Margin = new Thickness(6) },
                IsEnabled = false,
            });
        }
        foreach (var r in results) _list.Items.Add(Row(r));
        if (results.Count > 0) _list.SelectedIndex = 0;
        _list.Visibility = Visibility.Visible;
    }

    private ListBoxItem Row(SearchItem item)
    {
        if (!_icons.TryGetValue(item.Path, out var icon))
        {
            try { icon = IconLoader.LoadForDisplay(item.Path, 28); } catch { icon = null; }
            _icons[item.Path] = icon;
        }
        var grid = new Grid { Margin = new Thickness(4, 3, 4, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new Image { Source = icon, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Left });
        var name = new TextBlock { Text = item.Name, FontSize = 15, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 1);
        grid.Children.Add(name);
        var kind = new TextBlock
        {
            Text = item.Kind switch
            {
                SearchKind.Dock => Loc.T("Search_KindDock"),
                SearchKind.App => Loc.T("Search_KindApp"),
                _ => Loc.T("Search_KindFile"),
            },
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 4, 0),
        };
        Grid.SetColumn(kind, 2);
        grid.Children.Add(kind);
        var row = new ListBoxItem { Content = grid, Tag = item, Foreground = _theme.LabelForeground, Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(row, $"{item.Name}, {kind.Text}");
        if (item.Kind == SearchKind.File) row.ToolTip = item.Path;
        return row;
    }

    // ================= Fontes =================

    /// <summary>Itens do Dock (sempre atuais) mais os apps e arquivos guardados em cache.</summary>
    private static IEnumerable<SearchItem> AllItems()
    {
        foreach (var e in SettingsStore.Current.Items)
            yield return new SearchItem(e.Name, e.Path, SearchKind.Dock, e.Arguments);
        if (_cache != null)
            foreach (var i in _cache) yield return i;
    }

    /// <summary>Carrega apps e arquivos em segundo plano (na primeira vez e a cada 2 minutos).</summary>
    private void EnsureSources()
    {
        if (_loading != null || (_cache != null && DateTime.Now - _cacheTime < CacheLife)) return;
        _loading = Task.Run(LoadSources);
        _loading.ContinueWith(t =>
        {
            if (t.Status == TaskStatus.RanToCompletion)
            {
                _cache = t.Result;
                _cacheTime = DateTime.Now;
            }
            _loading = null;
            Dispatcher.BeginInvoke(() => { if (!_closing) Refresh(); });
        }, TaskScheduler.Default);
    }

    private static List<SearchItem> LoadSources()
    {
        var items = new List<SearchItem>();
        try
        {
            foreach (var app in StartMenuApps.Enumerate())
                items.Add(new SearchItem(app.Name, app.Path, SearchKind.App));
        }
        catch { }

        // Recentes: atalhos .lnk que o Windows cria ao abrir arquivos.
        AddFiles(items, Environment.GetFolderPath(Environment.SpecialFolder.Recent), "*.lnk", stripLnk: true);
        foreach (var folder in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        })
            AddFiles(items, folder, "*", stripLnk: false);
        return items;
    }

    private static void AddFiles(List<SearchItem> items, string folder, string pattern, bool stripLnk)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var path in Directory.EnumerateFileSystemEntries(folder, pattern, options).Take(500))
            {
                string name = stripLnk ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new SearchItem(name, path, SearchKind.File));
            }
        }
        catch { }
    }
}
