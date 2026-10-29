using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

/// <summary>Extrai ícones grandes (até 256px) de executáveis, atalhos e pastas.</summary>
internal static class IconLoader
{
    /// <summary>Tamanhos em que os ícones costumam existir dentro dos .exe/.ico.</summary>
    private static readonly int[] StandardSizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };

    /// <summary>
    /// Pixels a pedir para um ícone mostrado com <paramref name="dips"/> DIPs numa tela com escala
    /// <paramref name="scale"/> (1.5 = 150%): o menor tamanho padrão que não precise ser ampliado, até 256.
    /// </summary>
    public static int PixelsFor(double dips, double scale)
    {
        if (!double.IsFinite(dips) || dips <= 0) dips = 32;
        if (!double.IsFinite(scale) || scale <= 0) scale = 1;
        double need = dips * scale;
        foreach (int s in StandardSizes)
            if (s >= need - 0.5) return s;
        return 256;
    }

    private static double? _displayScale;

    /// <summary>Maior escala entre os monitores conectados; recalculada quando a tela muda.</summary>
    public static double DisplayScale
    {
        get
        {
            if (_displayScale is double d) return d;
            var screens = Monitors.All();
            d = screens.Count > 0 ? screens.Max(x => x.Scale) : 1.0;
            _displayScale = d;
            return d;
        }
    }

    static IconLoader()
    {
        try { Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => _displayScale = null; }
        catch { }
    }

    /// <summary>Ícone para mostrar com <paramref name="dips"/> DIPs, nítido no monitor de maior escala.</summary>
    public static ImageSource? LoadForDisplay(string path, double dips) => Load(path, PixelsFor(dips, DisplayScale));

    public static ImageSource? Load(string path, int size = 256)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full = Environment.ExpandEnvironmentVariables(path);

        return TryShellImageFactory(full, size) ?? TrySystemImageList(full) ?? TrySHGetFileInfo(full);
    }

    /// <summary>Reserva: ícone de 48 px da lista de imagens do sistema (o SHGetFileInfo comum só dá 32 px).</summary>
    private static ImageSource? TrySystemImageList(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return null;
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var info = new SHFILEINFO();
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX) == IntPtr.Zero) return null;
            Guid iid = IID_IImageList;
            if (SHGetImageList(SHIL_EXTRALARGE, ref iid, out IntPtr list) != 0 || list == IntPtr.Zero) return null;
            // O ponteiro da IImageList também serve como HIMAGELIST.
            try { hIcon = ImageList_GetIcon(list, info.iIcon, ILD_TRANSPARENT); }
            finally { Marshal.Release(list); }
            if (hIcon == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
        }
    }

    private static ImageSource? TryShellImageFactory(string path, int size)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            Guid iid = IID_IShellItemImageFactory;
            SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory);
            try
            {
                int hr = factory.GetImage(new SIZE { cx = size, cy = size },
                    SIIGBF.IconOnly | SIIGBF.BiggerSizeOk, out hbm);
                if (hr != 0 || hbm == IntPtr.Zero) return null;
                return HBitmapToBitmapSource(hbm);
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hbm != IntPtr.Zero) DeleteObject(hbm);
        }
    }

    /// <summary>
    /// Converte um HBITMAP 32bpp (alfa pré-multiplicado, como o Shell devolve) em BitmapSource,
    /// preservando a transparência (Imaging.CreateBitmapSourceFromHBitmap perde o alfa).
    /// </summary>
    private static BitmapSource? HBitmapToBitmapSource(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out BITMAP bmp) == 0) return null;
        int w = bmp.bmWidth, h = Math.Abs(bmp.bmHeight);
        if (w <= 0 || h <= 0) return null;

        var bmi = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // de cima para baixo
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        };

        byte[] pixels = new byte[w * h * 4];
        IntPtr hdc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(hdc, hbm, 0, (uint)h, pixels, ref bmi, DIB_RGB_COLORS) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hdc);
        }

        // Alguns ícones antigos vêm sem canal alfa (tudo zero): trata como opaco.
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { hasAlpha = true; break; }
        }

        var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
        var src = BitmapSource.Create(w, h, 96, 96, format, null, pixels, w * 4);
        src.Freeze();
        return src;
    }

    private static ImageSource? TrySHGetFileInfo(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return null;
        var info = new SHFILEINFO();
        IntPtr res = SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        if (res == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }
}
