using System.Runtime.InteropServices;

namespace Dockbox.Services;

/// <summary>
/// Volume e mudo do alto-falante padrão do Windows (Core Audio: IMMDeviceEnumerator e IAudioEndpointVolume).
/// Tudo devolve null ou false quando não há dispositivo de saída, sem lançar exceção.
/// </summary>
internal static class AudioVolume
{
    public sealed record State(double Level, bool Muted, string DeviceName);

    private static IMMDeviceEnumerator? _enumerator;
    private static Guid _context = Guid.NewGuid();

    /// <summary>Estado atual do dispositivo padrão, ou null se não há saída de som.</summary>
    public static State? Get() => WithEndpoint((volume, device) =>
    {
        if (volume.GetMasterVolumeLevelScalar(out float level) != 0) return null;
        volume.GetMute(out bool muted);
        return new State(Math.Clamp(level, 0, 1), muted, FriendlyName(device));
    });

    public static bool SetLevel(double level) => WithEndpoint((volume, _) =>
    {
        float v = (float)Math.Clamp(level, 0, 1);
        bool ok = volume.SetMasterVolumeLevelScalar(v, ref _context) == 0;
        // Subir o volume tira do mudo, como no Windows e no macOS.
        if (ok && v > 0) volume.SetMute(false, ref _context);
        return (object)ok;
    }) is true;

    public static bool SetMute(bool muted) => WithEndpoint((volume, _) =>
        (object)(volume.SetMute(muted, ref _context) == 0)) is true;

    private static T? WithEndpoint<T>(Func<IAudioEndpointVolume, IMMDevice, T?> action) where T : class
    {
        IMMDevice? device = null;
        object? endpoint = null;
        try
        {
            _enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device) != 0 || device == null) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out endpoint) != 0 || endpoint is not IAudioEndpointVolume volume) return null;
            return action(volume, device);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            _enumerator = null; // serviço de áudio reiniciou: cria de novo na próxima
            return null;
        }
        finally
        {
            if (endpoint != null) Marshal.ReleaseComObject(endpoint);
            if (device != null) Marshal.ReleaseComObject(device);
        }
    }

    private static string FriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        var pv = new PROPVARIANT();
        try
        {
            if (device.OpenPropertyStore(STGM_READ, out store) != 0 || store == null) return "";
            var key = PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out pv) != 0) return "";
            return pv.vt == VT_LPWSTR && pv.pointer != IntPtr.Zero ? Marshal.PtrToStringUni(pv.pointer) ?? "" : "";
        }
        catch { return ""; }
        finally
        {
            try { PropVariantClear(ref pv); } catch { }
            if (store != null) Marshal.ReleaseComObject(store);
        }
    }

    // ================= Interop =================

    private const int CLSCTX_ALL = 23;
    private const int STGM_READ = 0;
    private const ushort VT_LPWSTR = 31;
    private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

    private enum EDataFlow { Render = 0, Capture = 1, All = 2 }
    private enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    /// <summary>Só o necessário para ler uma string (VT_LPWSTR); o tamanho cobre o union inteiro.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr pointer;
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT pvar);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice device);
        // (os demais métodos não são usados e ficam fora: só a ordem dos usados importa)
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore properties);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
