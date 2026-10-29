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
    public static ImageSource? Load(string path, int size = 256)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string full = Environment.ExpandEnvironmentVariables(path);

        return TryShellImageFactory(full, size) ?? TrySHGetFileInfo(full);
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
