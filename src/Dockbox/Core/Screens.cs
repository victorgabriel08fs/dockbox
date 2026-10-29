using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Core;

public enum MonitorMode { Primary, Choose, FollowMouse }

/// <summary>Um monitor, em pixels físicos. <see cref="Scale"/> é a escala do Windows (1.0 = 100%).</summary>
internal sealed record ScreenInfo(string Device, RECT Bounds, RECT Work, bool Primary, double Scale)
{
    public int Width => Bounds.Right - Bounds.Left;
    public int Height => Bounds.Bottom - Bounds.Top;

    /// <summary>Número do monitor como o Windows mostra (\\.\DISPLAY2 → 2); 0 se não der para saber.</summary>
    public int Number => Screens.NumberOf(Device);

    public string Label =>
        (Number > 0 ? $"{Number} · " : "") + $"{Width}×{Height}" + (Primary ? " (principal)" : "");
}

/// <summary>Regras de qual monitor recebe o Dock. Sem chamadas ao Windows, para poder testar.</summary>
internal static class Screens
{
    public static int NumberOf(string device)
    {
        int i = device.Length;
        while (i > 0 && char.IsAsciiDigit(device[i - 1])) i--;
        return i < device.Length && int.TryParse(device.AsSpan(i), out int n) ? n : 0;
    }

    /// <summary>
    /// Monitor do Dock. <paramref name="followed"/> é o último monitor para onde o mouse levou o Dock.
    /// Se o monitor pedido não estiver conectado, volta para o principal.
    /// </summary>
    public static ScreenInfo? Pick(IReadOnlyList<ScreenInfo> screens, MonitorMode mode, string? chosen, string? followed)
    {
        if (screens.Count == 0) return null;
        string? want = mode switch
        {
            MonitorMode.Choose => chosen,
            MonitorMode.FollowMouse => followed,
            _ => null,
        };
        return Find(screens, want) ?? screens.FirstOrDefault(s => s.Primary) ?? screens[0];
    }

    public static ScreenInfo? Find(IReadOnlyList<ScreenInfo> screens, string? device) =>
        string.IsNullOrEmpty(device)
            ? null
            : screens.FirstOrDefault(s => string.Equals(s.Device, device, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Monitor cuja borda do Dock está sob o mouse (até <paramref name="tolerance"/> pixels de uma borda externa),
    /// ou null. Usado no modo "Seguir o mouse".
    /// </summary>
    public static ScreenInfo? AtEdge(IReadOnlyList<ScreenInfo> screens, int x, int y, DockEdge edge, int tolerance = 2)
    {
        foreach (var s in screens)
        {
            var b = s.Bounds;
            if (x < b.Left || x >= b.Right || y < b.Top || y >= b.Bottom) continue;
            bool hit = edge switch
            {
                DockEdge.Top => y < b.Top + tolerance,
                DockEdge.Left => x < b.Left + tolerance,
                DockEdge.Right => x >= b.Right - tolerance,
                _ => y >= b.Bottom - tolerance,
            };
            if (!hit) return null;
            // Borda encostada em outro monitor não é borda: o mouse só passa por ela.
            var (ox, oy) = edge switch
            {
                DockEdge.Top => (x, b.Top - 1),
                DockEdge.Left => (b.Left - 1, y),
                DockEdge.Right => (b.Right, y),
                _ => (x, b.Bottom),
            };
            bool shared = screens.Any(o => ox >= o.Bounds.Left && ox < o.Bounds.Right && oy >= o.Bounds.Top && oy < o.Bounds.Bottom);
            return shared ? null : s;
        }
        return null;
    }
}
