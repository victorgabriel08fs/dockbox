using System.Runtime.InteropServices;
using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

/// <summary>
/// Registra a janela do Dock como "AppBar" do Windows: a área de trabalho encolhe e as
/// janelas maximizadas param antes do Dock, como a barra de tarefas. Tudo em pixels físicos.
/// </summary>
internal sealed class AppBar : IDisposable
{
    private readonly IntPtr _hwnd;

    /// <summary>Mensagem que o Windows manda para a janela quando algo muda (ABN_*).</summary>
    public int CallbackMessage { get; }
    public bool Registered { get; private set; }

    /// <summary>Última faixa reservada, em pixels físicos da tela.</summary>
    public RECT Bounds { get; private set; }
    public DockEdge? Edge { get; private set; }
    public int Thickness { get; private set; }

    public AppBar(IntPtr hwnd)
    {
        _hwnd = hwnd;
        CallbackMessage = RegisterWindowMessage("Dockbox.AppBar");
    }

    private APPBARDATA Data() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };

    public bool Register()
    {
        if (Registered) return true;
        var d = Data();
        d.uCallbackMessage = (uint)CallbackMessage;
        Registered = SHAppBarMessage(ABM_NEW, ref d) != UIntPtr.Zero;
        return Registered;
    }

    /// <summary>
    /// Reserva uma faixa de <paramref name="thickness"/> pixels na borda do monitor principal.
    /// O Windows ajusta a faixa para não ficar em cima de outras AppBars (ex.: barra de tarefas).
    /// </summary>
    public RECT SetPos(DockEdge edge, int thickness)
    {
        var d = Data();
        d.uEdge = edge switch
        {
            DockEdge.Left => ABE_LEFT,
            DockEdge.Top => ABE_TOP,
            DockEdge.Right => ABE_RIGHT,
            _ => ABE_BOTTOM,
        };
        // O monitor principal sempre começa em (0, 0).
        var screen = new RECT { Left = 0, Top = 0, Right = GetSystemMetrics(SM_CXSCREEN), Bottom = GetSystemMetrics(SM_CYSCREEN) };
        d.rc = Strip(screen, edge, thickness);
        SHAppBarMessage(ABM_QUERYPOS, ref d);
        // QUERYPOS pode ter empurrado a faixa (para cima da barra de tarefas, por exemplo); refaz a espessura.
        d.rc = Strip(d.rc, edge, thickness);
        SHAppBarMessage(ABM_SETPOS, ref d);

        Bounds = d.rc;
        Edge = edge;
        Thickness = thickness;
        return d.rc;
    }

    /// <summary>Faixa de <paramref name="thickness"/> pixels encostada na borda <paramref name="edge"/> de <paramref name="area"/>.</summary>
    public static RECT Strip(RECT area, DockEdge edge, int thickness)
    {
        var r = area;
        switch (edge)
        {
            case DockEdge.Top: r.Bottom = r.Top + thickness; break;
            case DockEdge.Left: r.Right = r.Left + thickness; break;
            case DockEdge.Right: r.Left = r.Right - thickness; break;
            default: r.Top = r.Bottom - thickness; break;
        }
        return r;
    }

    /// <summary>Devolve a área de trabalho inteira para o Windows.</summary>
    public void Remove()
    {
        if (!Registered) return;
        var d = Data();
        SHAppBarMessage(ABM_REMOVE, ref d);
        Registered = false;
        Edge = null;
    }

    public void Dispose() => Remove();
}
