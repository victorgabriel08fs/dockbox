using System.Runtime.InteropServices;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

public static class SystemStats
{
    private static ulong _lastIdle, _lastTotal;

    /// <summary>Uso de CPU desde a última chamada (0 a 1).</summary>
    public static double CpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        ulong idleV = idle.Value, total = kernel.Value + user.Value; // kernel já inclui idle
        ulong dIdle = idleV - _lastIdle, dTotal = total - _lastTotal;
        bool first = _lastTotal == 0;
        _lastIdle = idleV;
        _lastTotal = total;
        if (first || dTotal == 0) return 0;
        return Math.Clamp(1.0 - (double)dIdle / dTotal, 0, 1);
    }

    public static (double usage, double usedGb, double totalGb) Memory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m) || m.ullTotalPhys == 0) return (0, 0, 0);
        double total = m.ullTotalPhys / 1073741824.0;
        double used = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        return (used / total, used, total);
    }

    /// <summary>Retorna null se não houver bateria.</summary>
    public static (int percent, bool charging)? Battery()
    {
        if (!GetSystemPowerStatus(out var s)) return null;
        if ((s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255 || s.BatteryLifePercent > 100) return null;
        return (s.BatteryLifePercent, s.ACLineStatus == 1);
    }
}
