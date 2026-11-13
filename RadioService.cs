using Windows.Devices.Radios;

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
}
