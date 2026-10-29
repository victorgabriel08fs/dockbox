using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Dockbox.Services;

namespace Dockbox.Widgets;

/// <summary>O que está tocando (Spotify, navegador...) com anterior / tocar-pausar / próxima.</summary>
public sealed class MediaWidget : DockWidget
{
    private Border _tile = null!;
    private Image _art = null!;
    private TextBlock _note = null!, _playGlyph = null!;
    private bool _playing;

    public override string Kind => "media";
    public override string DisplayName => "Mídia";
    public override string Description => "Música ou vídeo tocando agora, com controles de tocar, pausar e pular.";
    public override TimeSpan Interval => TimeSpan.FromSeconds(2);

    public override void Build()
    {
        _tile = Tile(Vertical("#FF375F", "#AF52DE"));
        var g = new Grid();

        _art = new Image { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(_art, BitmapScalingMode.HighQuality);
        _note = Glyph("", 22, Brushes.White); // nota musical
        _note.Margin = new Thickness(0, 0, 0, 14);

        var bar = new Border
        {
            Height = 18,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)),
        };
        var buttons = new UniformGrid3();
        buttons.Add(Button("", 8, MediaService.Previous));
        var play = Button("", 10, MediaService.PlayPause);
        _playGlyph = (TextBlock)play.Child;
        buttons.Add(play);
        buttons.Add(Button("", 8, MediaService.Next));
        bar.Child = buttons.Panel;

        g.Children.Add(_art);
        g.Children.Add(_note);
        g.Children.Add(bar);
        _tile.Child = g;
        Visual.Children.Add(_tile);
        Label = "Nada tocando";
    }

    private Border Button(string glyph, double size, Action action)
    {
        var b = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = Glyph(glyph, size, Brushes.White) };
        b.MouseLeftButtonDown += (_, e) => e.Handled = true;
        b.MouseLeftButtonUp += (_, e) =>
        {
            action();
            e.Handled = true;
            NextUpdate = DateTime.Now.AddMilliseconds(400);
        };
        return b;
    }

    public override async Task UpdateAsync()
    {
        var info = await MediaService.GetAsync();
        if (info == null || (string.IsNullOrEmpty(info.Title) && string.IsNullOrEmpty(info.Artist)))
        {
            Label = "Nada tocando";
            _art.Source = null;
            _note.Visibility = Visibility.Visible;
            SetPlaying(false);
            return;
        }

        _art.Source = info.Thumbnail;
        _note.Visibility = info.Thumbnail == null ? Visibility.Visible : Visibility.Collapsed;
        SetPlaying(info.IsPlaying);
        Label = string.IsNullOrEmpty(info.Artist) ? info.Title : $"{info.Title} — {info.Artist}";
    }

    private void SetPlaying(bool playing)
    {
        _playing = playing;
        _playGlyph.Text = playing ? "" : "";
    }

    public override void OnClick(FrameworkElement anchor)
    {
        MediaService.PlayPause();
        SetPlaying(!_playing);
        NextUpdate = DateTime.Now.AddMilliseconds(500);
    }

    /// <summary>Três colunas iguais (evita depender de UniformGrid de Primitives).</summary>
    private sealed class UniformGrid3
    {
        public Grid Panel { get; } = new();
        public void Add(UIElement e)
        {
            Panel.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(e, Panel.Children.Count);
            Panel.Children.Add(e);
        }
    }
}
