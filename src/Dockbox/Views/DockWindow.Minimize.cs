using System.Windows;
using System.Windows.Interop;
using Dockbox.Core;
using Dockbox.Services;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Views;

/// <summary>
/// Efeito ao minimizar: a janela encolhe até o ícone do app no Dock. O Windows avisa quando uma janela começa
/// a minimizar (SetWinEventHook com EVENT_SYSTEM_MINIMIZESTART); enquanto o efeito está ligado, a animação do
/// próprio Windows fica desligada (<see cref="SystemMinimizeAnimation"/>).
/// </summary>
public partial class DockWindow
{
    private WinEventProc? _minimizeProc; // guardado: o Windows chama esse delegate enquanto o gancho existir
    private IntPtr _minimizeHook;

    /// <summary>Efeito em uso: nenhum com "Mostrar animações" desligado no Windows.</summary>
    private MinimizeEffect ActiveMinimizeEffect => ReduceMotion ? MinimizeEffect.None : A.MinimizeEffect;

    /// <summary>Liga ou desliga o gancho e a animação do Windows conforme a opção.</summary>
    private void ApplyMinimizeEffect()
    {
        try
        {
            if (ActiveMinimizeEffect != MinimizeEffect.None)
            {
                if (_minimizeHook == IntPtr.Zero)
                {
                    _minimizeProc ??= OnWinEvent;
                    _minimizeHook = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZESTART, IntPtr.Zero,
                        _minimizeProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                }
                // Sem o gancho, deixa a animação do Windows como está.
                if (_minimizeHook != IntPtr.Zero) SystemMinimizeAnimation.Suppress();
            }
            else ReleaseMinimizeEffect();
        }
        catch (Exception ex) { ErrorLog.Write("Animação ao minimizar", ex); }
    }

    private void ReleaseMinimizeEffect()
    {
        if (_minimizeHook != IntPtr.Zero)
        {
            try { UnhookWinEvent(_minimizeHook); } catch { }
            _minimizeHook = IntPtr.Zero;
        }
        SystemMinimizeAnimation.Restore();
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType != EVENT_SYSTEM_MINIMIZESTART || idObject != OBJID_WINDOW || hwnd == IntPtr.Zero) return;
        try { AnimateMinimize(hwnd); }
        catch (Exception ex) { ErrorLog.Write("Animação ao minimizar", ex); }
    }

    private void AnimateMinimize(IntPtr hwnd)
    {
        var effect = ActiveMinimizeEffect;
        if (effect == MinimizeEffect.None || !IsVisible || PresentationSource.FromVisual(this) == null) return;
        if (WindowRectBeforeMinimize(hwnd) is not { } from || from.W < 8 || from.H < 8) return;
        if (TargetFor(hwnd) is not { } to) return;
        new MinimizeEffectWindow(hwnd, from, to, effect, Horizontal).Play();
    }

    /// <summary>Onde a janela estava: o retângulo atual ou, se já minimizou, o salvo pelo Windows.</summary>
    private static (double X, double Y, double W, double H)? WindowRectBeforeMinimize(IntPtr hwnd)
    {
        if (!IsIconic(hwnd) && GetWindowRect(hwnd, out RECT r) && r.Left > -30000)
            return (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

        var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(hwnd, ref wp)) return null;
        var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mi)) return null;
        var n = wp.rcNormalPosition;
        return MinimizeMath.RestoredRect((n.Left, n.Top, n.Right, n.Bottom), (wp.flags & WPF_RESTORETOMAXIMIZED) != 0,
            (mi.rcMonitor.Left, mi.rcMonitor.Top, mi.rcMonitor.Right, mi.rcMonitor.Bottom),
            (mi.rcWork.Left, mi.rcWork.Top, mi.rcWork.Right, mi.rcWork.Bottom));
    }

    /// <summary>Retângulo do ícone do app na tela (pixels físicos); sem ícone, o meio do Dock.</summary>
    private (double X, double Y, double W, double H)? TargetFor(IntPtr hwnd)
    {
        string[] keys = Array.Empty<string>();
        try
        {
            var w = RunningApps.Windows().FirstOrDefault(x => x.Handle == hwnd);
            if (w != null) keys = w.Keys.ToArray();
        }
        catch { }

        var tile = _tiles.FirstOrDefault(t => t.Kind is TileKind.App or TileKind.Running && t.ExeKey != null && keys.Contains(t.ExeKey));
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target) return null;
        var m = target.TransformToDevice;

        if (tile != null && PresentationSource.FromVisual(tile.Visual) != null)
        {
            var p = tile.Visual.PointToScreen(new Point(0, 0));
            double size = tile.Size * m.M11;
            return (p.X, p.Y, size, size);
        }
        // App sem ícone no Dock: vai para o meio do Dock.
        double main = _dockStart + _dockLength / 2, cross = EdgeMargin + Pad;
        var c = DockCanvas.PointToScreen(DockToCanvasPoint(main, cross));
        double s = BaseSize * m.M11 * 0.6;
        return (c.X - s / 2, c.Y - s / 2, s, s);
    }

    private Point DockToCanvasPoint(double main, double cross) => A.Edge switch
    {
        DockEdge.Top => new Point(main, cross),
        DockEdge.Left => new Point(cross, main),
        DockEdge.Right => new Point(W - cross, main),
        _ => new Point(main, H - cross),
    };
}
