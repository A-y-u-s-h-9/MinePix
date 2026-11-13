using System.Runtime.InteropServices;

namespace MinePix;

/// <summary>
/// Core Audio COM interop for the master endpoint volume slider.
/// Lazy-initialized; every call re-acquires interfaces if something went bad,
/// so a device swap won't leave the controls dead.
/// </summary>
internal static class CoreAudio
{
    private static readonly Guid ClsidMMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IidIAudioEndpointVolume = new("5CDF2C82-841E-4546-9722-0CF74078229A");

    private enum EDataFlow { eRender = 0, eCapture = 1, eAll = 2 }
    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
    private const int CLSCTX_ALL = 23;

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        // vtable order matters; only GetDefaultAudioEndpoint is used
        int EnumAudioEndpoints(int dataFlow, int dwState, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, out IntPtr ppProps);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out int pdwState);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out int pcChannels);
        int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(int nChannel, float fLevelDB, Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(int nChannel, float fLevel, Guid pguidEventContext);
        int GetChannelVolumeLevel(int nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(int nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
        int GetVolumeStepInfo(out int pnStep, out int pnStepCount);
        int VolumeStepUp(Guid pguidEventContext);
        int VolumeStepDown(Guid pguidEventContext);
        int QueryHardwareSupport(out int pdwHardwareSupportMask);
        int GetVolumeRange(out float pflMinVolumeDB, out float pflMaxVolumeDB, out float pflVolumeIncrementDB);
    }

    public static bool IsAvailable { get; private set; } = true;

    private static void Log(string message)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(dir, "controls.log"),
                $"{DateTime.Now:s} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static IAudioEndpointVolume? Acquire()
    {
        try
        {
            var clsidType = Type.GetTypeFromCLSID(ClsidMMDeviceEnumerator);
            if (clsidType is null) { Log("CLSID lookup failed"); IsAvailable = false; return null; }

            var enumerator = (IMMDeviceEnumerator?)Activator.CreateInstance(clsidType);
            if (enumerator is null) { Log("coclass activation returned null"); IsAvailable = false; return null; }

            int hr = enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out var device);
            if (hr != 0 || device == null) { Log($"GetDefaultAudioEndpoint hr=0x{hr:x8} device-null={device == null}"); IsAvailable = false; return null; }

            var iid = IidIAudioEndpointVolume;
            int hr2 = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var volObj);
            if (hr2 != 0 || volObj is not IAudioEndpointVolume volume) { Log($"Activate hr=0x{hr2:x8} obj-type={volObj?.GetType().Name ?? "null"}"); IsAvailable = false; return null; }

            IsAvailable = true;
            return volume;
        }
        catch (Exception ex)
        {
            Log("Acquire exception: " + ex.GetType().Name + ": " + ex.Message);
            IsAvailable = false;
            return null;
        }
    }

    public static float GetVolume()
    {
        var vol = Acquire();
        if (vol is null) return 0f;
        try { vol.GetMasterVolumeLevelScalar(out float level); return Math.Clamp(level, 0f, 1f); }
        catch { IsAvailable = false; return 0f; }
    }

    public static void SetVolume(float fraction)
    {
        var vol = Acquire();
        if (vol is null) return;
        try { vol.SetMasterVolumeLevelScalar(Math.Clamp(fraction, 0f, 1f), Guid.Empty); }
        catch { IsAvailable = false; }
    }

    public static bool GetMuted()
    {
        var vol = Acquire();
        if (vol is null) return false;
        try { vol.GetMute(out bool muted); return muted; }
        catch { IsAvailable = false; return false; }
    }

    public static void SetMuted(bool muted)
    {
        var vol = Acquire();
        if (vol is null) return;
        try { vol.SetMute(muted, Guid.Empty); }
        catch { IsAvailable = false; }
    }
}
