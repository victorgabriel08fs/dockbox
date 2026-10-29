using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Dockbox.Core;
using Dockbox.Services;
using Dockbox.Views;

namespace Dockbox.Widgets;

/// <summary>Notas rápidas: o ícone mostra o começo da nota; clique abre o bloquinho.</summary>
public sealed class NotesWidget : DockWidget
{
    private TextBlock _preview = null!;
    private NoteWindow? _window;

    public override string Kind => "notes";
    public override string DisplayName => Loc.T("W_Notes_Name");
    public override string Description => Loc.T("W_Notes_Desc");
    public override TimeSpan Interval => TimeSpan.FromSeconds(30);

    public override void Build()
    {
        var tile = Tile(Vertical("#FFF3A6", "#FFE36B"), Solid("#40B08A00"));
        var g = new Grid();
        // Faixa de cima, como num bloco de notas
        g.Children.Add(new Border { Height = 9, VerticalAlignment = VerticalAlignment.Top, Background = Solid("#E8C547") });
        _preview = new TextBlock
        {
            FontSize = 7.5,
            Foreground = Solid("#5A4A10"),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 12, 5, 4),
            LineHeight = 9,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
        };
        g.Children.Add(_preview);
        tile.Child = g;
        Visual.Children.Add(tile);
        Show(QuickNotes.Load());
    }

    private void Show(string text)
    {
        string trimmed = text.Trim();
        _preview.Text = trimmed.Length > 120 ? trimmed[..120] : trimmed;
        string first = QuickNotes.FirstLine(text);
        Label = first.Length > 0 ? Loc.F("W_Notes_Label", first) : Loc.T("W_Notes_Empty");
    }

    public override Task UpdateAsync()
    {
        // O arquivo pode ter sido editado fora (ou num backup restaurado); com a janela aberta, ela manda.
        if (_window == null) Show(QuickNotes.Load());
        return Task.CompletedTask;
    }

    public override void OnClick(FrameworkElement anchor)
    {
        if (_window != null)
        {
            _window.Close();
            return;
        }
        _window = new NoteWindow();
        _window.TextChanged += Show;
        _window.Closed += (_, _) => _window = null;
        _window.ShowNear(anchor);
    }

    public override IEnumerable<(string Header, Action Action)> MenuItems()
    {
        yield return (Loc.T("W_Notes_OpenFile"), () =>
        {
            if (!System.IO.File.Exists(QuickNotes.FilePath)) QuickNotes.Save("");
            AppLauncher.Open(QuickNotes.FilePath);
        });
    }

    public override void Dispose()
    {
        _window?.Close();
        _window = null;
    }
}
