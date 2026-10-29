using System.IO;
using System.Runtime.InteropServices;
using Dockbox.Core;
using static Dockbox.Interop.NativeMethods;

namespace Dockbox.Services;

/// <summary>
/// Liga e desliga a animação do próprio Windows ao minimizar e restaurar janelas (a que vai para a barra de
/// tarefas). Com o efeito do Dockbox ligado, a do Windows é desligada para as duas não aparecerem juntas.
/// A mudança vale só para esta sessão do Windows (não é gravada no registro). Antes de desligar, grava um
/// arquivo: se o Dockbox fechar sem religar (travou), a próxima abertura religa.
/// </summary>
internal static class SystemMinimizeAnimation
{
    /// <summary>True enquanto o Dockbox mantém a animação do Windows desligada.</summary>
    public static bool Suppressed { get; private set; }

    internal static string MarkerPath => Path.Combine(SettingsStore.FolderPath, "animacao-do-windows-desligada.txt");

    private static bool? Get()
    {
        var info = new ANIMATIONINFO { cbSize = Marshal.SizeOf<ANIMATIONINFO>() };
        return SystemParametersInfo(SPI_GETANIMATION, (uint)info.cbSize, ref info, 0) ? info.iMinAnimate != 0 : null;
    }

    private static void Set(bool on)
    {
        var info = new ANIMATIONINFO { cbSize = Marshal.SizeOf<ANIMATIONINFO>(), iMinAnimate = on ? 1 : 0 };
        SystemParametersInfo(SPI_SETANIMATION, (uint)info.cbSize, ref info, SPIF_SENDCHANGE);
    }

    /// <summary>Desliga a animação do Windows (se estava ligada). Seguro chamar várias vezes.</summary>
    public static void Suppress()
    {
        if (Suppressed) return;
        if (Get() != true) return; // já desligada pelo usuário: nada a fazer nem a devolver
        try
        {
            Directory.CreateDirectory(SettingsStore.FolderPath);
            File.WriteAllText(MarkerPath, "1");
        }
        catch { return; } // sem como lembrar, não mexe
        Set(false);
        Suppressed = true;
    }

    /// <summary>Religa a animação do Windows se foi o Dockbox que desligou.</summary>
    public static void Restore()
    {
        if (!Suppressed && !File.Exists(MarkerPath)) return;
        try { Set(true); } catch { }
        try { File.Delete(MarkerPath); } catch { }
        Suppressed = false;
    }

    /// <summary>Na abertura: se a última sessão não religou, religa agora.</summary>
    public static void RecoverAfterCrash()
    {
        try { if (File.Exists(MarkerPath)) Restore(); } catch { }
    }
}
