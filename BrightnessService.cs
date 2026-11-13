using System.Management;

namespace MinePix;

/// <summary>
/// Internal-panel brightness via WMI (root\wmi). External monitors and some desktops
/// report nothing — IsSupported is re-probed lazily and the UI disables the row.
/// </summary>
internal static class BrightnessService
{
    public static bool IsSupported { get; private set; } = true;

    public static int? GetBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                IsSupported = true;
                return Convert.ToInt32(obj["CurrentBrightness"]);
            }
            IsSupported = false;
            return null;
        }
        catch
        {
            IsSupported = false;
            return null;
        }
    }

    public static bool SetBrightness(int level)
    {
        try
        {
            level = Math.Clamp(level, 0, 100);
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            int setCount = 0;
            foreach (ManagementObject methods in searcher.Get())
            {
                using var inParams = methods.GetMethodParameters("WmiSetBrightness");
                inParams["Brightness"] = Convert.ToByte(level);
                inParams["Timeout"] = Convert.ToUInt32(0);
                methods.InvokeMethod("WmiSetBrightness", inParams, null);
                setCount++;
            }
            IsSupported = setCount > 0;
            return setCount > 0;
        }
        catch
        {
            IsSupported = false;
            return false;
        }
    }
}
