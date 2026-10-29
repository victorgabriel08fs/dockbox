using System.Runtime.InteropServices;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

public static class RecycleBin
{
    public static long ItemCount()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64NumItems : 0;
    }

    public static void Empty() => SHEmptyRecycleBin(IntPtr.Zero, null, 0);

    public static void Open() => AppLauncher.Open("shell:RecycleBinFolder");

    /// <summary>Move arquivos para a Lixeira (com desfazer).</summary>
    public static void Delete(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = string.Join("\0", list) + "\0\0",
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_WANTNUKEWARNING),
        };
        SHFileOperation(ref op);
    }
}
