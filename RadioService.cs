using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace MinePix;

/// <summary>Wi-Fi and Bluetooth via the modern Windows.Devices.Radios API. Never needs admin.</summary>
internal static class RadioService
{
    public sealed record Snapshot(Radio? Wifi, Radio? Bluetooth);

    public static async Task<Snapshot> GetRadiosAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            Radio? wifi = null, bt = null;
            foreach (var radio in radios)
            {
                if (radio.Kind == RadioKind.WiFi) wifi = radio;
                else if (radio.Kind == RadioKind.Bluetooth) bt = radio;
            }
            return new Snapshot(wifi, bt);
        }
        catch
        {
            return new Snapshot(null, null);
        }
    }

    public static async Task<bool> SetAsync(Radio? radio, bool on)
    {
        if (radio is null) return false;
        try
        {
            var result = await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
            return result == RadioAccessStatus.Allowed;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsOn(Radio? radio)
    {
        if (radio is null) return false;
        try
        {
            return radio.State == RadioState.On;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Name of the active Wi-Fi network (SSID), or null if not on one.</summary>
    public static string? WifiName()
    {
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null || !profile.IsWlanConnectionProfile) return null;
            return string.IsNullOrWhiteSpace(profile.ProfileName) ? null : profile.ProfileName;
        }
        catch
        {
            return null;
        }
    }
}
