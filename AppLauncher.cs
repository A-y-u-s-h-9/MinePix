using System.Runtime.InteropServices;

namespace MinePix;

/// <summary>
/// Activates an installed app by its Application User Model ID (AUMID) — the same
/// identity that surfaces on a notification's <c>AppInfo</c>. Used as the
/// "open notification" redirect target, since the notification-center API does not
/// expose per-toast activation arguments.
/// </summary>
internal static class AppLauncher
{
    [Flags]
    internal enum ACTIVATEOPTIONS : uint
    {
        None = 0,
        DesignMode = 1,
        NoErrorUI = 2,
        NoSplashScreen = 4,
        Prelaunch = 8,
    }

    [ComImport]
    [Guid("45BA127D-10B5-4B82-BF94-38A78BD91A21")]
    private class ApplicationActivationManager { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("2E941FC8-5D90-4617-9C81-F83A3C6409C9")]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ACTIVATEOPTIONS options,
            out uint processId);
    }

    /// <summary>
    /// Launches (or focuses) the app identified by <paramref name="aumid"/>.
    /// Returns true on success; on failure <paramref name="error"/> carries the reason.
    /// </summary>
    public static bool TryActivate(string aumid, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(aumid))
        {
            error = "no app id on this notification";
            return false;
        }

        try
        {
            var aam = (IApplicationActivationManager)new ApplicationActivationManager();
            int hr = aam.ActivateApplication(aumid, "", ACTIVATEOPTIONS.None, out _);
            if (hr != 0)
            {
                error = $"activation returned 0x{hr:X8}";
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
