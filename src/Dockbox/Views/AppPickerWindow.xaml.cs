using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dockbox.Services;

namespace Dockbox.Views;

public sealed class PickerItem : INotifyPropertyChanged
{
    public required InstalledApp App { get; init; }
    public string Name => App.Name;
    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
    }
    public bool IconRequested { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class AppPickerWindow : Window
{
    private List<PickerItem> _all = new();
    public List<InstalledApp> Selected { get; } = new();

    public AppPickerWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            Search.Focus();
            var apps = await Task.Run(StartMenuApps.Enumerate);
            _all = apps.Select(a => new PickerItem { App = a }).ToList();
            ApplyFilter();
            LoadIconsInBackground();
        };
    }

    /// <summary>Carrega os ícones aos poucos, sem travar a janela.</summary>
    private void LoadIconsInBackground()
    {
        var queue = new Queue<PickerItem>(_all);
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(1) };
        timer.Tick += (_, _) =>
        {
            for (int i = 0; i < 6 && queue.Count > 0; i++)
            {
                var item = queue.Dequeue();
                if (item.IconRequested) continue;
                item.IconRequested = true;
                item.Icon = IconLoader.Load(item.App.Path, 48);
            }
            if (queue.Count == 0 || !IsLoaded) timer.Stop();
        };
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }

    private void ApplyFilter()
    {
        string q = Search.Text.Trim();
        List.ItemsSource = string.IsNullOrEmpty(q)
            ? _all
            : _all.Where(i => i.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AddButton.IsEnabled = List.SelectedItems.Count > 0;

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (List.SelectedItems.Count > 0) Add_Click(sender, e);
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        Selected.Clear();
        Selected.AddRange(List.SelectedItems.Cast<PickerItem>().Select(p => p.App));
        DialogResult = true;
    }
}
