using System.IO;
using System.Text;
using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

/// <summary>
/// Esconde e mostra a barra de tarefas do Windows (opção "Ocultar a barra de tarefas").
/// A barra fica em ocultação automática (para liberar a área de trabalho) e com as janelas escondidas.
/// Antes de mexer, grava o estado original num arquivo: se o Dockbox fechar sem restaurar
/// (travou, foi encerrado pelo Gerenciador de Tarefas), a próxima abertura restaura.
/// </summary>
internal static class Taskbar
{
    private const string MainClass = "Shell_TrayWnd";
    private const string SecondaryClass = "Shell_SecondaryTrayWnd";

    /// <summary>True enquanto o Dockbox está mantendo a barra escondida.</summary>
    public static bool Hidden { get; private set; }

    /// <summary>Arquivo com o estado original da barra (ABS_*) enquanto ela está escondida.</summary>
    internal static string MarkerPath => Path.Combine(SettingsStore.FolderPath, "barra-de-tarefas-oculta.txt");

    /// <summary>Lê o estado guardado no arquivo, ou null se não há arquivo válido.</summary>
    internal static int? ReadMarker()
    {
        try
        {
            if (!File.Exists(MarkerPath)) return null;
            return int.TryParse(File.ReadAllText(MarkerPath).Trim(), out int state) && state is >= 0 and <= 3 ? state : ABS_ALWAYSONTOP;
        }
        catch { return null; }
    }

    internal static void WriteMarker(int state)
    {
        Directory.CreateDirectory(SettingsStore.FolderPath);
        File.WriteAllText(MarkerPath, state.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    internal static void DeleteMarker()
    {
        try { File.Delete(MarkerPath); } catch { }
    }

    /// <summary>Barra principal e as dos outros monitores.</summary>
    private static List<IntPtr> Windows()
    {
        var list = new List<IntPtr>();
        var sb = new StringBuilder(64);
        EnumWindows((h, _) =>
        {
            sb.Clear();
            GetClassName(h, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls == MainClass || cls == SecondaryClass) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }

    private static int GetState(IntPtr tray)
    {
        var d = new APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(), hWnd = tray };
        return (int)SHAppBarMessage(ABM_GETSTATE, ref d).ToUInt32();
    }

    private static void SetState(IntPtr tray, int state)
    {
        var d = new APPBARDATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(), hWnd = tray, lParam = new IntPtr(state) };
        SHAppBarMessage(ABM_SETSTATE, ref d);
    }

    /// <summary>Esconde a barra (de novo, se o Explorer a trouxe de volta). Seguro chamar várias vezes.</summary>
    public static void Hide()
    {
        IntPtr tray = FindWindow(MainClass, null);
        if (tray == IntPtr.Zero) return; // Explorer fechado ou reiniciando: tenta de novo depois

        int current = GetState(tray);
        // Guarda o estado original só na primeira vez (se já existe arquivo, ele tem o estado de antes).
        if (ReadMarker() == null)
        {
            try { WriteMarker(current); }
            catch { return; } // sem como lembrar o estado, não esconde
        }
        if ((current & ABS_AUTOHIDE) == 0) SetState(tray, current | ABS_AUTOHIDE);
        foreach (var w in Windows())
            if (IsWindowVisible(w)) ShowWindow(w, SW_HIDE);
        Hidden = true;
    }

    /// <summary>Mostra a barra e volta ao estado de antes (ocultação automática ligada ou não).</summary>
    public static void Restore()
    {
        int? saved = ReadMarker();
        if (saved == null && !Hidden) return;
        try
        {
            IntPtr tray = FindWindow(MainClass, null);
            if (tray != IntPtr.Zero)
            {
                if (saved is int state) SetState(tray, state);
                foreach (var w in Windows()) ShowWindow(w, SW_SHOWNA);
            }
        }
        catch { }
        DeleteMarker();
        Hidden = false;
    }

    /// <summary>Na abertura: se a última sessão não restaurou a barra, restaura agora.</summary>
    public static void RecoverAfterCrash()
    {
        if (ReadMarker() != null) Restore();
    }

    /// <summary>
    /// True quando a barra reapareceu sozinha (novo monitor, tecla Windows) e não é o menu Iniciar
    /// ou a pesquisa que estão abertos, que dependem dela.
    /// </summary>
    public static bool ShouldHideAgain()
    {
        if (!Hidden) return false;
        if (!Windows().Any(IsWindowVisible)) return false;
        var sb = new StringBuilder(64);
        GetClassName(GetForegroundWindow(), sb, sb.Capacity);
        return !IsShellPopup(sb.ToString());
    }

    /// <summary>Janelas do Iniciar, pesquisa e painéis da barra no Windows 10 e 11.</summary>
    internal static bool IsShellPopup(string foregroundClass) => foregroundClass is
        MainClass or SecondaryClass or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow"
        or "NotifyIconOverflowWindow" or "TopLevelWindowForOverflowXamlIsland";

    /// <summary>Abre o menu Iniciar (como apertar a tecla Windows).</summary>
    public static void OpenStartMenu()
    {
        keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
        keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
