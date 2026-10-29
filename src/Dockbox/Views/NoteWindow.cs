using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Dockbox.Core;

namespace Dockbox.Views;

/// <summary>Bloquinho flutuante do widget de notas. Salva sozinho enquanto digita e ao fechar.</summary>
internal sealed class NoteWindow : Window
{
    private readonly TextBox _box;
    private readonly DispatcherTimer _save;
    private string _saved;

    /// <summary>Texto mudou (já salvo ou a salvar); o widget atualiza o rótulo.</summary>
    public event Action<string>? TextChanged;

    public NoteWindow()
    {
        Title = Loc.T("W_Notes_Name");
        Width = 320;
        Height = 340;
        MinWidth = 200;
        MinHeight = 160;
        WindowStyle = WindowStyle.ToolWindow;
        ShowInTaskbar = false;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xB8));

        _saved = QuickNotes.Load();
        _box = new TextBox
        {
            Text = _saved,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0x2B, 0x2B, 0x2B)),
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 14,
            Padding = new Thickness(12, 10, 12, 10),
            MaxLength = QuickNotes.MaxLength,
        };
        System.Windows.Automation.AutomationProperties.SetName(_box, Loc.T("W_Notes_Name"));
        Content = _box;

        _save = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _save.Tick += (_, _) => SaveNow();
        _box.TextChanged += (_, _) =>
        {
            _save.Stop();
            _save.Start();
            TextChanged?.Invoke(_box.Text);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        };
        Deactivated += (_, _) => SaveNow();
        Closing += (_, _) => SaveNow();
        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.CaretIndex = _box.Text.Length;
        };
    }

    private void SaveNow()
    {
        _save.Stop();
        if (_box.Text == _saved) return;
        if (QuickNotes.Save(_box.Text)) _saved = _box.Text;
    }

    /// <summary>Abre perto do widget: centrado nele, acima (ou abaixo, se não couber), dentro da tela.</summary>
    public void ShowNear(FrameworkElement anchor)
    {
        try
        {
            var source = PresentationSource.FromVisual(anchor);
            if (source?.CompositionTarget != null)
            {
                var toDip = source.CompositionTarget.TransformFromDevice;
                var topLeft = toDip.Transform(anchor.PointToScreen(new Point(0, 0)));
                var bottomRight = toDip.Transform(anchor.PointToScreen(new Point(anchor.ActualWidth, anchor.ActualHeight)));
                var a = new Rect(topLeft, bottomRight);
                var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                    SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                double left = a.Left + a.Width / 2 - Width / 2;
                double top = a.Top - Height - 16;
                if (top < screen.Top) top = a.Bottom + 16;
                if (top + Height > screen.Bottom) top = Math.Max(screen.Top, a.Top + a.Height / 2 - Height / 2);
                if (left + Width > screen.Right) left = Math.Min(a.Left - Width - 16, screen.Right - Width);
                Left = Math.Max(screen.Left, left);
                Top = Math.Max(screen.Top, top);
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }
        catch { WindowStartupLocation = WindowStartupLocation.CenterScreen; }
        Show();
        Activate();
    }
}
