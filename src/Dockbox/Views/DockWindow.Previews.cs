using System.Windows;
using System.Windows.Threading;
using Dockbox.Core;
using Dockbox.Services;

namespace Dockbox.Views;

/// <summary>Miniaturas das janelas ao passar o mouse num app com várias janelas abertas.</summary>
public partial class DockWindow
{
    /// <summary>Segundos com o mouse fora do ícone e do painel até o painel sumir.</summary>
    private const double PreviewGrace = 0.35;

    private WindowPreview? _preview;
    private Tile? _previewTile;            // ícone cujo painel está aberto
    private Tile? _previewCandidate;       // ícone sob o mouse, esperando o atraso
    private double _previewSince = -1;
    private double _previewAway = -1;
    private DispatcherTimer? _previewTimer;

    private bool PreviewOpen => _previewTile != null;

    private static bool HasWindows(Tile? t) =>
        t is { Kind: TileKind.App or TileKind.Running, Running: true, ExeKey: not null };

    /// <summary>Chamado a cada frame do Dock e pelo timer do painel: abre, troca ou fecha o painel.</summary>
    private void UpdatePreview((double main, double cross)? mouse)
    {
        if (!A.WindowPreviews || _menuOpen || _pressTile != null || _externalSlot >= 0 || !IsVisible || !_shown)
        {
            ClosePreview();
            return;
        }

        double now = _clock.Elapsed.TotalSeconds;
        Tile? hot = _hovering && mouse is (double m, double) ? TileAt(m) : null;

        if (HasWindows(hot))
        {
            _previewAway = -1;
            if (hot == _previewTile) return;
            if (PreviewOpen)
            {
                // Já tem um painel aberto: passa direto para o outro app, sem esperar.
                if (!OpenPreview(hot!)) ClosePreview();
                return;
            }
            if (_previewCandidate != hot)
            {
                _previewCandidate = hot;
                _previewSince = now;
                EnsurePreviewTimer();
            }
            else if (_previewSince >= 0 && now - _previewSince >= A.PreviewDelay)
            {
                // Uma janela só: não abre e não pergunta de novo até o mouse sair do ícone.
                if (!OpenPreview(hot!)) _previewSince = -1;
            }
            return;
        }

        _previewCandidate = null;
        if (!PreviewOpen)
        {
            _previewTimer?.Stop();
            return;
        }
        if (_preview!.ContainsCursor())
        {
            _previewAway = -1;
            return;
        }
        // Em cima de outro ícone do Dock: fecha já. Fora do Dock: espera um pouco (caminho até o painel).
        if (hot != null) { ClosePreview(); return; }
        if (_previewAway < 0) _previewAway = now;
        else if (now - _previewAway >= PreviewGrace) ClosePreview();
    }

    private void EnsurePreviewTimer()
    {
        if (_previewTimer == null)
        {
            _previewTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            _previewTimer.Tick += (_, _) =>
            {
                // Com o mouse no painel, o Dock para de animar; o timer continua conferindo.
                if (!_animating) UpdatePreview(GetMouseDock());
            };
        }
        _previewTimer.Start();
    }

    /// <summary>Abre o painel do app. False se o app tem menos janelas que o mínimo.</summary>
    private bool OpenPreview(Tile tile)
    {
        List<(IntPtr Handle, string Title, bool Minimized)> windows;
        try { windows = RunningApps.WindowsOf(tile.ExeKey!); }
        catch { return false; }
        if (windows.Count < PreviewLayout.MinWindows) return false;
        return ShowPreview(tile, windows);
    }

    private bool ShowPreview(Tile tile, List<(IntPtr Handle, string Title, bool Minimized)> windows)
    {
        if (_screen == null || PresentationSource.FromVisual(this) == null) return false;
        try
        {
            if (_preview == null)
            {
                _preview = new WindowPreview();
                _preview.WindowChosen += hwnd =>
                {
                    ClosePreview();
                    RunningApps.Activate(hwnd);
                };
                _preview.CloseRequested += OnPreviewCloseRequested;
            }

            // Ponto de apoio: centro do ícone, na altura máxima dos ícones ampliados (onde o Dock deixa de "segurar" o mouse).
            double main = tile.Main + tile.Size / 2;
            double cross = EdgeMargin + Pad * 2 + BaseSize * MaxScale + 10;
            var p = DockCanvas.PointToScreen(DockToCanvas(main, cross));
            var icon = (tile.Visual as System.Windows.Controls.Image)?.Source;
            var items = windows.Select(w => new WindowPreview.Item(w.Handle, w.Title, w.Minimized)).ToList();
            _preview.ShowFor(items, icon, _theme, A.CornerRadius, (int)Math.Round(p.X), (int)Math.Round(p.Y), A.Edge, _screen.Bounds, _screen.Scale);

            _previewTile = tile;
            _previewCandidate = null;
            _previewAway = -1;
            NameLabel.Visibility = Visibility.Collapsed;
            EnsurePreviewTimer();
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Miniaturas", ex);
            ClosePreview();
            return false;
        }
    }

    /// <summary>Fecha a janela pedida e, logo depois, atualiza o painel com as que sobraram.</summary>
    private void OnPreviewCloseRequested(IntPtr hwnd)
    {
        RunningApps.Close(hwnd);
        var tile = _previewTile;
        var later = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        later.Tick += (_, _) =>
        {
            later.Stop();
            if (tile == null || _previewTile != tile || !_tiles.Contains(tile)) return;
            List<(IntPtr Handle, string Title, bool Minimized)> windows;
            try { windows = RunningApps.WindowsOf(tile.ExeKey!); }
            catch { windows = new(); }
            // Depois de fechar, mostra até a última janela; sem nenhuma, fecha o painel.
            if (windows.Count == 0 || !ShowPreview(tile, windows)) ClosePreview();
        };
        later.Start();
    }

    private void ClosePreview()
    {
        _previewTile = null;
        _previewCandidate = null;
        _previewSince = -1;
        _previewAway = -1;
        _previewTimer?.Stop();
        try { _preview?.HidePanel(); } catch { }
    }

    /// <summary>Os ícones foram recriados: o painel aberto passa para o ícone novo do mesmo app, ou fecha.</summary>
    private void RemapPreview()
    {
        if (_previewTile == null) return;
        string? key = _previewTile.ExeKey;
        var same = _tiles.FirstOrDefault(t => HasWindows(t) && t.ExeKey == key);
        if (same == null) ClosePreview();
        else _previewTile = same;
    }

    /// <summary>Ponto nas coordenadas do Dock (eixo principal, distância da borda) → ponto no Canvas.</summary>
    private Point DockToCanvas(double main, double cross) => A.Edge switch
    {
        DockEdge.Top => new Point(main, cross),
        DockEdge.Left => new Point(cross, main),
        DockEdge.Right => new Point(W - cross, main),
        _ => new Point(main, H - cross),
    };
}
