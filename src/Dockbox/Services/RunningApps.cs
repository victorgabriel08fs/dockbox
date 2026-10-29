using System.Text;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

/// <summary>Lista as janelas de aplicativos abertas, agrupadas pelo caminho do executável.</summary>
public static class RunningApps
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    };

    /// <summary>exe (minúsculo) → janelas em ordem de empilhamento (a de cima primeiro).</summary>
    public static Dictionary<string, List<IntPtr>> Snapshot()
    {
        var result = new Dictionary<string, List<IntPtr>>();
        var pidCache = new Dictionary<uint, string?>();
        var cls = new StringBuilder(256);

        EnumWindows((hwnd, _) =>
        {
            if (!IsAppWindow(hwnd, cls)) return true;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!pidCache.TryGetValue(pid, out var exe))
            {
                exe = GetProcessPath(pid);
                pidCache[pid] = exe;
            }
            if (exe == null) return true;
            if (!result.TryGetValue(exe, out var list)) result[exe] = list = new List<IntPtr>();
            list.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static bool IsAppWindow(IntPtr hwnd, StringBuilder cls)
    {
        if (!IsWindowVisible(hwnd)) return false;
        if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if (GetWindowTextLength(hwnd) == 0) return false;
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        if ((ex & WS_EX_TOOLWINDOW) != 0) return false;
        if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        cls.Clear();
        GetClassName(hwnd, cls, cls.Capacity);
        return !IgnoredClasses.Contains(cls.ToString());
    }

    private static string? GetProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString().ToLowerInvariant() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>Janelas abertas de um app, com título, na ordem de empilhamento.</summary>
    public static List<(IntPtr Handle, string Title, bool Minimized)> WindowsOf(string exeKey)
    {
        var result = new List<(IntPtr, string, bool)>();
        if (!Snapshot().TryGetValue(exeKey, out var windows)) return result;
        var sb = new StringBuilder(512);
        foreach (var w in windows)
        {
            sb.Clear();
            GetWindowText(w, sb, sb.Capacity);
            result.Add((w, sb.ToString(), IsIconic(w)));
        }
        return result;
    }

    /// <summary>Traz uma janela para a frente, restaurando se estiver minimizada.</summary>
    public static void Activate(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        AllowSetForegroundWindow(ASFW_ANY);
        SetForegroundWindow(hwnd);
    }

    /// <summary>Pede para a janela fechar (como clicar no X; o app pode perguntar se quer salvar).</summary>
    public static void Close(IntPtr hwnd) => PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// Comportamento do Dock: se o app já está em primeiro plano, minimiza; senão traz a janela para a frente.
    /// Retorna false se o app não tem janelas abertas.
    /// </summary>
    public static bool ToggleWindows(string exeKey)
    {
        var snap = Snapshot();
        if (!snap.TryGetValue(exeKey, out var windows) || windows.Count == 0) return false;

        IntPtr fg = GetForegroundWindow();
        if (windows.Contains(fg) && !IsIconic(fg))
        {
            foreach (var w in windows) if (!IsIconic(w)) ShowWindow(w, SW_MINIMIZE);
            return true;
        }

        // Restaura minimizadas e traz a do topo para frente
        var target = windows[0];
        foreach (var w in windows.AsEnumerable().Reverse())
        {
            if (IsIconic(w)) ShowWindow(w, SW_RESTORE);
        }
        AllowSetForegroundWindow(ASFW_ANY);
        SetForegroundWindow(target);
        return true;
    }
}
