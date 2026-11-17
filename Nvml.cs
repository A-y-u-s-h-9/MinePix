using System;
using System.Runtime.InteropServices;

namespace MinePix;

/// <summary>
/// Minimal NVML surface: init, device count/handle/name, util, temperature, power, memory.
/// Everything here is optional at runtime — any failure disables NVIDIA telemetry silently.
/// </summary>
internal static class Nvml
{
    private const int NvmlTempGpu = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    private static extern int nvmlInit_v2();

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    private static extern int nvmlShutdown();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetCount_v2")]
    private static extern int nvmlDeviceGetCount_v2(out uint count);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
    private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out NvmlUtilization utilization);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int type, out uint temp);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);

    private static bool? _available;
    private static IntPtr _device = IntPtr.Zero;
    private static string _name = "";

    /// <summary>True when NVML initialized and at least one device is present.</summary>
    public static bool Available
    {
        get
        {
            if (_available is null)
                _available = TryInit();
            return _available.Value;
        }
    }

    /// <summary>GPU name as NVML reports it (e.g. "NVIDIA GeForce RTX 3050 Laptop GPU").</summary>
    public static string Name => Available ? _name : "";

    private static bool TryInit()
    {
        try
        {
            if (nvmlInit_v2() != 0)
                return false;
            if (nvmlDeviceGetCount_v2(out uint count) != 0 || count == 0)
            {
                _ = nvmlShutdown();
                return false;
            }
            if (nvmlDeviceGetHandleByIndex_v2(0, out _device) != 0 || _device == IntPtr.Zero)
            {
                _ = nvmlShutdown();
                return false;
            }
            var buf = new byte[96];
            if (nvmlDeviceGetName(_device, buf, (uint)buf.Length) == 0)
            {
                int end = Array.IndexOf(buf, (byte)0);
                _name = end < 0 ? "" : System.Text.Encoding.ASCII.GetString(buf, 0, end);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetSnapshot(out double memUsedMb, out double memTotalMb, out uint gpuUtilPct, out uint memUtilPct, out uint tempC, out double powerW)
    {
        memUsedMb = memTotalMb = 0;
        gpuUtilPct = memUtilPct = tempC = 0;
        powerW = 0;
        if (!Available)
            return false;
        try
        {
            if (nvmlDeviceGetMemoryInfo(_device, out var mem) != 0)
                return false;
            memUsedMb = mem.Used / (1024.0 * 1024.0);
            memTotalMb = mem.Total / (1024.0 * 1024.0);

            gpuUtilPct = 0;
            memUtilPct = 0;
            _ = nvmlDeviceGetUtilizationRates(_device, out var util); // non-fatal if it fails
            gpuUtilPct = util.Gpu;
            memUtilPct = util.Memory;

            tempC = 0;
            _ = nvmlDeviceGetTemperature(_device, NvmlTempGpu, out var t);
            tempC = t;

            powerW = 0;
            if (nvmlDeviceGetPowerUsage(_device, out var mw) == 0)
                powerW = mw / 1000.0;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
