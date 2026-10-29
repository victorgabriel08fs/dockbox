using System.Runtime.InteropServices;
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

    /// <summary>Uma janela de app: executável (minúsculo) e, para apps da Store, o AppUserModelID.</summary>
    public sealed record AppWindow(IntPtr Handle, string? Exe, string? Aumid)
    {
        /// <summary>Chave principal do app: AUMID quando for app da Store, senão o executável.</summary>
        public string? Key => Aumid != null ? AumidKey(Aumid) : Exe;

        /// <summary>Todas as chaves que podem casar com um item fixado.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                if (Exe != null) yield return Exe;
                if (Aumid != null) yield return AumidKey(Aumid);
            }
        }
    }

    /// <summary>Chave usada para apps identificados pelo AppUserModelID (ex.: shell:AppsFolder\...).</summary>
    public static string AumidKey(string aumid) => "aumid:" + aumid.ToLowerInvariant();

    /// <summary>Janelas de apps abertas, em ordem de empilhamento (a de cima primeiro), com o mesmo filtro do Alt+Tab.</summary>
    public static List<AppWindow> Windows()
    {
        var result = new List<AppWindow>();
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

            // Apps da Store rodam dentro do ApplicationFrameHost (ou de um exe em WindowsApps):
            // o que identifica o app é o AppUserModelID da janela.
            string? aumid = null;
            if (exe.EndsWith(@"\applicationframehost.exe", StringComparison.Ordinal) || exe.Contains(@"\windowsapps\", StringComparison.Ordinal))
                aumid = GetAumid(hwnd);
            if (exe.EndsWith(@"\applicationframehost.exe", StringComparison.Ordinal) && aumid == null) return true;

            result.Add(new AppWindow(hwnd, exe, aumid));
            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>chave (exe minúsculo ou aumid:...) → janelas em ordem de empilhamento.</summary>
    public static Dictionary<string, List<IntPtr>> Snapshot() => Snapshot(Windows());

    public static Dictionary<string, List<IntPtr>> Snapshot(List<AppWindow> windows)
    {
        var result = new Dictionary<string, List<IntPtr>>();
        foreach (var w in windows)
        {
            foreach (var key in w.Keys)
            {
                if (!result.TryGetValue(key, out var list)) result[key] = list = new List<IntPtr>();
                list.Add(w.Handle);
            }
        }
        return result;
    }

    private static string? GetAumid(IntPtr hwnd)
    {
        IPropertyStore? store = null;
        var pv = new PROPVARIANT();
        try
        {
            Guid iid = IID_IPropertyStore;
            SHGetPropertyStoreForWindow(hwnd, ref iid, out store);
            var key = PKEY_AppUserModel_ID;
            store.GetValue(ref key, out pv);
            if (pv.vt != VT_LPWSTR || pv.pointer == IntPtr.Zero) return null;
            string? id = Marshal.PtrToStringUni(pv.pointer);
            return string.IsNullOrWhiteSpace(id) ? null : id;
        }
        catch
        {
            return null;
        }
        finally
        {
            PropVariantClear(ref pv);
            if (store != null) Marshal.ReleaseComObject(store);
        }
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
