using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Views;

/// <summary>
/// Janela sem conteúdo logo abaixo do Dock, do tamanho do fundo dele, que pede ao Windows para
/// desfocar o que está atrás (ACCENT_ENABLE_BLURBEHIND, Windows 10 e 11). Os cantos arredondados
/// vêm de uma região da janela. O Dock continua desenhando a cor e a borda por cima.
/// </summary>
internal sealed class BlurBackdrop : Window
{
    private const int WS_EX_TRANSPARENT = 0x20;
    private IntPtr _hwnd;
    private (int X, int Y, int W, int H, int R) _last = (-1, -1, -1, -1, -1);

    public BlurBackdrop()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Background = Brushes.Transparent;
        Width = Height = 1;
        Left = Top = -32000;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Sem fundo próprio: os pixels ficam transparentes e o Windows mostra o desfoque.
            if (HwndSource.FromHwnd(_hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
            int ex = GetWindowLong(_hwnd, GWL_EXSTYLE);
            SetWindowLong(_hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT);
            int round = DWMWCP_DONOTROUND; // os cantos vêm da região, não do Windows 11
            DwmSetWindowAttribute(_hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            SetAccent(ACCENT_ENABLE_BLURBEHIND);
        };
    }

    private void SetAccent(int state)
    {
        var accent = new ACCENT_POLICY { AccentState = state };
        int size = Marshal.SizeOf<ACCENT_POLICY>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(_hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    /// <summary>
    /// Coloca o desfoque no retângulo <paramref name="x"/>, <paramref name="y"/>, <paramref name="w"/>×<paramref name="h"/>
    /// (pixels físicos da tela), com cantos de raio <paramref name="radius"/>, logo abaixo de <paramref name="above"/>.
    /// </summary>
    public void Place(IntPtr above, int x, int y, int w, int h, int radius)
    {
        if (_hwnd == IntPtr.Zero || w <= 0 || h <= 0) return;
        var now = (x, y, w, h, radius);
        bool resized = now.w != _last.W || now.h != _last.H || now.radius != _last.R;
        SetWindowPos(_hwnd, above, x, y, w, h, SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        if (resized)
        {
            // A região passa a pertencer ao Windows depois do SetWindowRgn.
            int d = Math.Max(0, radius * 2);
            SetWindowRgn(_hwnd, CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d), true);
        }
        _last = (x, y, w, h, radius);
    }

    /// <summary>Volta a ficar logo abaixo do Dock (depois de o Dock reafirmar "sempre no topo").</summary>
    public void StayBelow(IntPtr above)
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, above, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>
    /// Desfoque permitido agora: Windows com "Efeitos de transparência" ligado, sem economia de bateria
    /// e sem alto contraste.
    /// </summary>
    public static bool SystemAllows()
    {
        if (SystemParameters.HighContrast) return false;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("EnableTransparency") is int t && t == 0) return false;
        }
        catch { }
        if (GetSystemPowerStatus(out var power) && power.SystemStatusFlag == 1) return false; // economia de bateria
        return true;
    }
}
