using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Views;

/// <summary>
/// Janela temporária que mostra a janela minimizada encolhendo até o ícone no Dock. O conteúdo é a
/// miniatura ao vivo do Windows (DwmRegisterThumbnail) ocupando a janela toda; a cada frame a janela
/// muda de posição e tamanho. Não é transparente, porque miniaturas do DWM não aparecem em janelas em
/// camadas; como a miniatura cobre tudo, o fundo não aparece. Fecha sozinha no fim.
/// </summary>
internal sealed class MinimizeEffectWindow : Window
{
    private const int WS_EX_TRANSPARENT = 0x20;
    private readonly IntPtr _source;
    private readonly (double X, double Y, double W, double H) _from, _to;
    private readonly MinimizeEffect _effect;
    private readonly bool _horizontalDock;
    private readonly Stopwatch _clock = new();
    private IntPtr _hwnd, _thumb;
    private bool _running;

    public MinimizeEffectWindow(IntPtr source, (double X, double Y, double W, double H) from,
        (double X, double Y, double W, double H) to, MinimizeEffect effect, bool horizontalDock)
    {
        _source = source;
        _from = from;
        _to = to;
        _effect = effect;
        _horizontalDock = horizontalDock;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Black;
        Width = Height = 1;
        Left = Top = -32000;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
            int round = DWMWCP_DONOTROUND;
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        };
        Closed += (_, _) => Stop();
    }

    /// <summary>Começa a animação. False se o Windows não deu a miniatura da janela (aí não anima).</summary>
    public bool Play()
    {
        Show();
        if (_hwnd == IntPtr.Zero || DwmRegisterThumbnail(_hwnd, _source, out _thumb) != 0 || _thumb == IntPtr.Zero)
        {
            Close();
            return false;
        }
        Frame(0);
        _running = true;
        _clock.Start();
        CompositionTarget.Rendering += OnRendering;
        return true;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        double duration = Math.Max(0.05, MinimizeMath.Duration(_effect));
        double t = _clock.Elapsed.TotalSeconds / duration;
        if (t >= 1)
        {
            Close();
            return;
        }
        Frame(t);
    }

    private void Frame(double t)
    {
        var (x, y, w, h) = MinimizeMath.At(_effect, _horizontalDock, _from, _to, t);
        int iw = Math.Max(1, (int)Math.Round(w)), ih = Math.Max(1, (int)Math.Round(h));
        SetWindowPos(_hwnd, HWND_TOPMOST, (int)Math.Round(x), (int)Math.Round(y), iw, ih, SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        var props = new DWM_THUMBNAIL_PROPERTIES
        {
            dwFlags = DWM_TNP_RECTDESTINATION | DWM_TNP_VISIBLE | DWM_TNP_OPACITY | DWM_TNP_SOURCECLIENTAREAONLY,
            rcDestination = new RECT { Left = 0, Top = 0, Right = iw, Bottom = ih },
            opacity = (byte)Math.Round(255 * MinimizeMath.Opacity(t)),
            fVisible = true,
            fSourceClientAreaOnly = false,
        };
        DwmUpdateThumbnailProperties(_thumb, ref props);
    }

    private void Stop()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnRendering;
            _running = false;
        }
        if (_thumb != IntPtr.Zero)
        {
            try { DwmUnregisterThumbnail(_thumb); } catch { }
            _thumb = IntPtr.Zero;
        }
    }
}
