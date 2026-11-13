using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace MinePix;

public sealed record BluetoothDeviceInfo(string Id, string Name, string Kind);

/// <summary>Paired Bluetooth devices via WinRT — no kernel drivers involved.</summary>
internal static class BluetoothService
{
    public static async Task<List<BluetoothDeviceInfo>> PairedAsync()
    {
        var list = new List<BluetoothDeviceInfo>();

        async Task AddClassic()
        {
            try
            {
                var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
                var devices = await DeviceInformation.FindAllAsync(selector);
                foreach (var d in devices)
                    list.Add(new BluetoothDeviceInfo(d.Id, d.Name, "Classic"));
            }
            catch { }
        }

        async Task AddLowEnergy()
        {
            try
            {
                var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
                var devices = await DeviceInformation.FindAllAsync(selector);
                foreach (var d in devices)
                    list.Add(new BluetoothDeviceInfo(d.Id, d.Name, "BLE"));
            }
            catch { }
        }

        await Task.WhenAll(AddClassic(), AddLowEnergy());
        return list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
