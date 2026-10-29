using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dockbox.Core;
using Dockbox.Services;

namespace Dockbox.Views;

/// <summary>Pilhas de pastas: clicar numa pasta fixada abre o conteúdo; soltar arquivos nela move ou copia.</summary>
public partial class DockWindow
{
    private StackWindow? _stack;
    private DockEntry? _stackEntry;
    private readonly Dictionary<DockEntry, FileSystemWatcher> _latestWatchers = new();

    private bool StackOpen => _stack != null;

    private static string FolderOf(DockEntry e) => Environment.ExpandEnvironmentVariables(e.Path);

    private static bool IsFolder(Tile? t) =>
        t is { Kind: TileKind.App, Entry: not null } && Directory.Exists(FolderOf(t.Entry));

    /// <summary>Abre a pilha da pasta, ou fecha se ela já está aberta.</summary>
    private void ToggleStack(Tile tile)
    {
        var entry = tile.Entry!;
        bool same = _stackEntry == entry && StackOpen;
        CloseStack();
        if (same || _screen == null || PresentationSource.FromVisual(this) == null) return;

        try
        {
            double main = tile.Main + tile.Size / 2;
            double cross = EdgeMargin + Pad * 2 + BaseSize * MaxScale + 10;
            var p = DockCanvas.PointToScreen(DockToCanvas(main, cross));
            var stack = new StackWindow(entry, _theme, A.CornerRadius, (int)Math.Round(p.X), (int)Math.Round(p.Y), A.Edge, _screen.Bounds, _screen.Scale);
            stack.Closed += (_, _) =>
            {
                if (_stack != stack) return;
                _stack = null;
                _stackEntry = null;
                StartAnimating(); // com auto-ocultar, o Dock pode sumir agora
            };
            _stack = stack;
            _stackEntry = entry;
            ClosePreview();
            NameLabel.Visibility = Visibility.Collapsed;
            stack.Open();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Pilha", ex);
            CloseStack();
            AppLauncher.Launch(entry); // sem pilha, abre no Explorador como antes
        }
    }

    private void CloseStack()
    {
        var s = _stack;
        _stack = null;
        _stackEntry = null;
        try { s?.CloseOnce(); } catch { }
    }

    /// <summary>Opções da pasta no menu do item: ver como, ordenar e ícone.</summary>
    private void AddStackItems(ItemsControl menu, DockEntry entry)
    {
        var view = new MenuItem { Header = Loc.T("Stack_ViewAs") };
        foreach (var (v, key) in new[] { (StackView.Auto, "Stack_Auto"), (StackView.Fan, "Stack_Fan"), (StackView.Grid, "Stack_Grid"), (StackView.List, "Stack_List") })
            view.Items.Add(Check(Loc.T(key), entry.StackView == v, () => { entry.StackView = v; SettingsStore.Commit(); }));
        menu.Items.Add(view);

        var sort = new MenuItem { Header = Loc.T("Stack_SortBy") };
        foreach (var (v, key) in new[] { (StackSort.Name, "Stack_SortName"), (StackSort.DateAdded, "Stack_SortAdded"), (StackSort.DateModified, "Stack_SortModified"), (StackSort.Kind, "Stack_SortKind") })
            sort.Items.Add(Check(Loc.T(key), entry.StackSort == v, () => { entry.StackSort = v; SettingsStore.Commit(); }));
        menu.Items.Add(sort);

        var icon = new MenuItem { Header = Loc.T("Stack_ShowAs") };
        icon.Items.Add(Check(Loc.T("Stack_ShowFolder"), !entry.StackShowsLatest, () => { entry.StackShowsLatest = false; SettingsStore.Commit(); }));
        icon.Items.Add(Check(Loc.T("Stack_ShowLatest"), entry.StackShowsLatest, () => { entry.StackShowsLatest = true; SettingsStore.Commit(); }));
        menu.Items.Add(icon);
    }

    private static MenuItem Check(string header, bool on, Action action)
    {
        var mi = new MenuItem { Header = header, IsCheckable = true, IsChecked = on };
        mi.Click += (_, _) => action();
        return mi;
    }

    // ================= Ícone com o item mais recente =================

    /// <summary>Ícone do tile de uma pasta: o do item mais recente (se pedido e houver) ou o da pasta.</summary>
    private ImageSource? FolderIcon(DockEntry entry, ImageSource? folderIcon)
    {
        if (!entry.StackShowsLatest) return folderIcon;
        var latest = StackContent.Latest(StackContent.List(FolderOf(entry)));
        if (latest == null) return folderIcon;
        try { return IconLoader.Load(latest.Path) ?? folderIcon; }
        catch { return folderIcon; }
    }

    /// <summary>Acompanha as pastas que mostram o item mais recente para trocar o ícone quando algo entra.</summary>
    private void WatchLatestFolders()
    {
        foreach (var w in _latestWatchers.Values) w.Dispose();
        _latestWatchers.Clear();
        foreach (var t in _tiles.Where(t => IsFolder(t) && t.Entry!.StackShowsLatest))
        {
            var entry = t.Entry!;
            try
            {
                var w = new FileSystemWatcher(FolderOf(entry)) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
                FileSystemEventHandler changed = (_, _) => Dispatcher.BeginInvoke(() => RefreshFolderIcon(entry));
                w.Created += changed;
                w.Deleted += changed;
                w.Renamed += (_, _) => Dispatcher.BeginInvoke(() => RefreshFolderIcon(entry));
                w.EnableRaisingEvents = true;
                _latestWatchers[entry] = w;
            }
            catch { }
        }
    }

    private void RefreshFolderIcon(DockEntry entry)
    {
        var t = _tiles.FirstOrDefault(x => x.Entry == entry);
        if (t?.Visual is not Image img) return;
        _iconCache.TryGetValue(entry.Path, out var folderIcon);
        img.Source = FolderIcon(entry, folderIcon);
    }

    // ================= Soltar arquivos numa pasta =================

    /// <summary>True se dá para soltar esses arquivos dentro da pasta (nenhum é a própria pasta ou a contém).</summary>
    private static bool CanDropInto(Tile? t, string[] files) =>
        IsFolder(t) && files.Length > 0 && !files.Any(f => StackContent.IsInside(f, FolderOf(t!.Entry!)));

    private static DragDropEffects FolderDropEffect(Tile t, string[] files)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        return StackContent.ShouldMove(files[0], FolderOf(t.Entry!), ctrl, shift) ? DragDropEffects.Move : DragDropEffects.Copy;
    }

    /// <summary>Move ou copia para a pasta, com as janelas de progresso e de conflito do próprio Windows.</summary>
    private void DropIntoFolder(Tile t, string[] files)
    {
        string folder = FolderOf(t.Entry!);
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        foreach (var f in files)
        {
            try
            {
                string dest = Path.Combine(folder, Path.GetFileName(f.TrimEnd('\\')));
                bool move = StackContent.ShouldMove(f, folder, ctrl, shift);
                var ui = Microsoft.VisualBasic.FileIO.UIOption.AllDialogs;
                var cancel = Microsoft.VisualBasic.FileIO.UICancelOption.DoNothing;
                if (Directory.Exists(f))
                {
                    if (move) Microsoft.VisualBasic.FileIO.FileSystem.MoveDirectory(f, dest, ui, cancel);
                    else Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(f, dest, ui, cancel);
                }
                else if (File.Exists(f))
                {
                    if (move) Microsoft.VisualBasic.FileIO.FileSystem.MoveFile(f, dest, ui, cancel);
                    else Microsoft.VisualBasic.FileIO.FileSystem.CopyFile(f, dest, ui, cancel);
                }
            }
            catch (Exception ex) { ErrorLog.Write("Soltar na pasta", ex); }
        }
        if (A.BounceOnLaunch && !ReduceMotion) Bounce(t);
    }
}
