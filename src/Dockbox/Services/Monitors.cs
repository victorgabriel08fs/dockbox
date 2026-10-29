using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

/// <summary>Lista os monitores conectados.</summary>
internal static class Monitors
{
    public static List<ScreenInfo> All()
    {
        var list = new List<ScreenInfo>();
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) =>
            {
                var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(h, ref info))
                {
                    double scale = 1.0;
                    try
                    {
                        if (GetDpiForMonitor(h, MDT_EFFECTIVE_DPI, out uint dx, out _) == 0 && dx > 0) scale = dx / 96.0;
                    }
                    catch { }
                    list.Add(new ScreenInfo(info.szDevice ?? "", info.rcMonitor, info.rcWork,
                        (info.dwFlags & MONITORINFOF_PRIMARY) != 0, scale));
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Listar monitores", ex);
        }
        return list.OrderBy(s => s.Number == 0 ? int.MaxValue : s.Number).ThenBy(s => s.Bounds.Left).ToList();
    }
}
