using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

/// <summary>
/// Ícones escolhidos pelo usuário para os itens do Dock. A imagem é copiada (como PNG de até 256 px) para
/// %APPDATA%\Dockbox\icons, então continua funcionando se o arquivo original for movido ou apagado.
/// </summary>
internal static class CustomIcons
{
    /// <summary>Maior lado do PNG guardado. Imagens maiores são reduzidas com boa qualidade.</summary>
    public const int MaxSize = 256;

    public static string Folder => Path.Combine(SettingsStore.FolderPath, "icons");

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff" };
    private static readonly string[] ModuleExtensions = { ".exe", ".dll", ".icl", ".cpl" };

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    public static bool IsModule(string path) => ModuleExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>Caminho completo do ícone guardado, ou null.</summary>
    public static string? FullPath(string? icon) =>
        string.IsNullOrWhiteSpace(icon) ? null : Path.Combine(Folder, Path.GetFileName(icon));

    /// <summary>Carrega o ícone guardado (sem travar o arquivo). Null se não tem ou se o arquivo sumiu.</summary>
    public static ImageSource? Load(string? icon)
    {
        string? path = FullPath(icon);
        if (path == null || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Copia uma imagem (.png, .jpg, .ico...) para a pasta de ícones. Devolve o nome do arquivo guardado.</summary>
    public static string ImportImage(string source)
    {
        BitmapSource frame;
        using (var stream = File.OpenRead(source))
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            // .ico tem vários tamanhos: usa o maior.
            frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
        }
        return Save(frame);
    }

    /// <summary>Copia o ícone número <paramref name="index"/> de um .exe ou .dll para a pasta de ícones.</summary>
    public static string ImportFromModule(string module, int index)
    {
        var icons = new IntPtr[1];
        var ids = new uint[1];
        uint got = PrivateExtractIcons(module, index, MaxSize, MaxSize, icons, ids, 1, 0);
        if (got == 0 || got == uint.MaxValue || icons[0] == IntPtr.Zero) throw new InvalidDataException(Loc.T("Icon_NoneInFile"));
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(icons[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            return Save(src);
        }
        finally { DestroyIcon(icons[0]); }
    }

    /// <summary>Tamanho final mantendo a proporção, com o maior lado até <see cref="MaxSize"/> (nunca aumenta).</summary>
    internal static (int W, int H) FitSize(int w, int h)
    {
        if (w <= 0 || h <= 0) return (1, 1);
        double k = Math.Min(1.0, (double)MaxSize / Math.Max(w, h));
        return (Math.Max(1, (int)Math.Round(w * k)), Math.Max(1, (int)Math.Round(h * k)));
    }

    /// <summary>Reduz (se precisar) com interpolação de alta qualidade e grava como PNG com nome novo.</summary>
    private static string Save(BitmapSource src)
    {
        var (w, h) = FitSize(src.PixelWidth, src.PixelHeight);
        BitmapSource output = src;
        if (w != src.PixelWidth || h != src.PixelHeight)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (var dc = visual.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, w, h));
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            output = rtb;
        }

        Directory.CreateDirectory(Folder);
        string name = Guid.NewGuid().ToString("N") + ".png";
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(output));
        using (var file = File.Create(Path.Combine(Folder, name))) encoder.Save(file);
        return name;
    }

    /// <summary>Apaga o arquivo do ícone, a menos que outro item ainda use o mesmo.</summary>
    public static void Delete(string? icon, IEnumerable<DockEntry> items, DockEntry? except = null)
    {
        string? path = FullPath(icon);
        if (path == null) return;
        if (items.Any(e => e != except && string.Equals(e.Icon, icon, StringComparison.OrdinalIgnoreCase))) return;
        try { File.Delete(path); } catch { }
    }

    /// <summary>
    /// "Trocar ícone…": escolhe uma imagem ou um ícone de outro programa e aplica ao item.
    /// True se trocou. Erros viram aviso para o usuário.
    /// </summary>
    public static bool Choose(Window? owner, DockEntry entry)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("Icon_PickTitle"), Filter = Loc.T("Icon_Filter") };
        if (dlg.ShowDialog(owner) != true) return false;
        string file = dlg.FileName;
        try
        {
            string name;
            if (IsModule(file))
            {
                // Arquivo com vários ícones: a janela do Windows deixa escolher qual.
                var sb = new System.Text.StringBuilder(file, 1024);
                int index = 0;
                IntPtr hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
                if (!PickIconDlg(hwnd, sb, sb.Capacity, ref index)) return false;
                name = ImportFromModule(Environment.ExpandEnvironmentVariables(sb.ToString()), index);
            }
            else name = ImportImage(file);

            string? old = entry.Icon;
            entry.Icon = name;
            Delete(old, SettingsStore.Current.Items, entry);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.F("Icon_Failed", ex.Message), "Dockbox", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>"Restaurar ícone padrão": volta ao ícone do app e apaga a cópia (se ninguém mais usa).</summary>
    public static void Restore(DockEntry entry)
    {
        string? old = entry.Icon;
        entry.Icon = null;
        Delete(old, SettingsStore.Current.Items, entry);
    }
}
