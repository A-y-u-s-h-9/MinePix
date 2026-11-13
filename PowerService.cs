using System.Management;
using System.Diagnostics;

namespace MinePix;

/// <summary>
/// Battery + power-plan state for the quick-controls grid.
/// Percent + charging via Win32_Battery (WMI); plan switching via powercfg.exe.
/// Desktops with no battery report IsSupported = false; the tiles grey out.
/// </summary>
internal static class PowerService
{
    public static bool IsSupported { get; private set; } = true;

    public sealed record Snapshot(byte Percent, string Status, string ActiveScheme, bool PluggedIn);

    public static Snapshot? GetSnapshot()
    {
        try
        {
            byte pct = 255;
            string status = "";
            bool plugged = false;
            bool found = false;

            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            foreach (ManagementObject b in searcher.Get())
            {
                found = true;
                try
                {
                    if (b["EstimatedChargeRemaining"] is byte p) pct = p;
                    else if (b["EstimatedChargeRemaining"] is ushort p2) pct = (byte)p2;
                    else if (b["EstimatedChargeRemaining"] is uint p3) pct = (byte)p3;
                    else if (b["EstimatedChargeRemaining"] is int p4) pct = (byte)p4;

                    plugged = Convert.ToInt32(b["BatteryStatus"]) switch
                    {
                        2 => true,   // on AC
                        6 => true,   // charging
                        7 => true,   // charging + high
                        8 => true,   // charging + low
                        9 => true,   // charging + critical
                        _ => false,  // 1=discharging, 3=fully charged-on-battery, 4=low, 5=critical
                    };

                    status = plugged ? "Plugged in" : "On battery";
                    IsSupported = true;
                    break;
                }
                catch { }
            }

            if (!found) { IsSupported = false; return null; }

            string scheme = "Balanced";
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo("powercfg", "/list")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.Unicode,
                    },
                };
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(800);

                int starIdx = output.IndexOf('*');
                if (starIdx >= 0)
                {
                    // find the parenthesized scheme name on the same line as '*'
                    int open = output.IndexOf('(', starIdx);
                    int close = open >= 0 ? output.IndexOf(')', open) : -1;
                    if (open >= 0 && close > open)
                    {
                        string name = output.Substring(open + 1, close - open - 1).ToLowerInvariant();
                        if (name.Contains("high"))         scheme = "Performance";
                        else if (name.Contains("saver"))   scheme = "Battery saver";
                        else                               scheme = "Balanced";
                    }
                }
            }
            catch { }

            return new Snapshot(pct, status, scheme, plugged);
        }
        catch
        {
            IsSupported = false;
            return null;
        }
    }

    public static void SetScheme(string name)
    {
        try
        {
            string alias = name switch
            {
                "Performance" => "SCHEME_MAX",   // High Performance
                "Balanced"    => "SCHEME_BALANCED",  // Balanced
                "Battery"     => "SCHEME_MIN",   // Power Saver
                _ => "SCHEME_BALANCED",
            };

            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("powercfg", $"-setactive {alias}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            p.Start();
            p.WaitForExit(1000);
        }
        catch { }
    }
}
