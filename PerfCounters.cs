using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MinePix;

/// <summary>
/// Minimal persistent-session PDH wrapper: per-process GPU 3D engine utilization and
/// per-process dedicated GPU memory usage. GPU Engine counters are rate counters, so the
/// query must be created once and re-collected on every sample — a fresh query per sample
/// would always report 0.
/// </summary>
internal static class PerfCounters
{
    private const uint PdhMoreData = 0x800007D2;
    private const uint ErrorSuccess = 0x00000000;
    private const uint PdhFmtDouble = 0x00000200;
    // NOTE: do NOT pass PDH_FMT_NOSCALE here — for rate counters (Timer100Ns) PDH then
    // returns raw counts instead of the cooked percentage we want.

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PdhOpenQuery(IntPtr dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PdhAddEnglishCounterW(IntPtr query, string fullPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern uint PdhCloseQuery(IntPtr query);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PdhFmtCountervalueItem
    {
        public IntPtr Name;
        public uint Status;
        public uint Padding;
        public ulong Bits;
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private struct DoubleBits
    {
        [FieldOffset(0)] public ulong UInt64;
        [FieldOffset(0)] public double Double;
    }

    private static IntPtr _query;
    private static IntPtr _engineCounter;
    private static IntPtr _memCounter;
    private static bool _initDone;

    /// <summary>Create the shared query and counters, and prime the first sample.</summary>
    public static void Initialize()
    {
        if (_initDone)
            return;
        _initDone = true;

        if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out _query) != ErrorSuccess)
            return;
        if (PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engineCounter) != ErrorSuccess)
            return;
        if (PdhAddEnglishCounterW(_query, @"\GPU Process Memory(*)\Dedicated Usage", IntPtr.Zero, out _memCounter) != ErrorSuccess)
            return;

        // prime the rate-counter delta basis; first real sample needs this to be older than the live tick
        PdhCollectQueryData(_query);
    }

    /// <summary>Re-collect the shared query (rate counters need this cadence).</summary>
    public static bool Collect()
    {
        Initialize();
        if (_query == IntPtr.Zero)
            return false;
        return PdhCollectQueryData(_query) == ErrorSuccess;
    }

    /// <summary>Full sample for every 3D engine of the given pid: returns best utilization + ludi string.</summary>
    public static double? Max3DPercentForPid(int pid, out string luid)
    {
        luid = "";
        if (_engineCounter == IntPtr.Zero)
            return null;

        double best = -1;
        string pidPrefix = $"pid_{pid}_";
        foreach (var (name, value) in SampleArray(_engineCounter))
        {
            if (!name.StartsWith(pidPrefix, StringComparison.OrdinalIgnoreCase))
                continue;
            int typeIdx = name.LastIndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
            if (typeIdx < 0)
                continue;
            if (!name.Substring(typeIdx + "_engtype_".Length).Equals("3d", StringComparison.OrdinalIgnoreCase))
                continue;
            if (value > best)
            {
                best = value;
                int luidStart = name.IndexOf("_luid_", StringComparison.OrdinalIgnoreCase);
                int physStart = name.LastIndexOf("_phys_", StringComparison.OrdinalIgnoreCase);
                if (luidStart >= 0 && physStart > luidStart)
                    luid = name.Substring(luidStart + "_luid_".Length, physStart - luidStart - "_luid_".Length);
            }
        }
        return best < 0 ? null : best;
    }

    /// <summary>Max 3D utilization across all pids (for the "any game ticking on GPU" case, reserved).</summary>
    public static double? Max3DPercentOverall()
    {
        if (_engineCounter == IntPtr.Zero)
            return null;
        double best = -1;
        foreach (var (name, value) in SampleArray(_engineCounter))
        {
            int typeIdx = name.LastIndexOf("_engtype_", StringComparison.OrdinalIgnoreCase);
            if (typeIdx < 0)
                continue;
            if (!name.Substring(typeIdx + "_engtype_".Length).Equals("3d", StringComparison.OrdinalIgnoreCase))
                continue;
            if (value > best)
                best = value;
        }
        return best < 0 ? null : best;
    }

    /// <summary>Per-process dedicated GPU memory in MB, max across physical GPUs.</summary>
    public static double? GpuDedicatedMemoryMbForPid(int pid)
    {
        if (_memCounter == IntPtr.Zero)
            return null;
        double best = -1;
        string pidPrefix = $"pid_{pid}_";
        bool any = false;
        foreach (var (name, value) in SampleArray(_memCounter))
        {
            if (name.StartsWith(pidPrefix, StringComparison.OrdinalIgnoreCase))
            {
                any = true;
                if (value > best)
                    best = value;
            }
        }
        return any ? best / (1024.0 * 1024.0) : null;
    }

    private static IEnumerable<(string name, double value)> SampleArray(IntPtr counter)
    {
        var result = new List<(string, double)>();
        if (counter == IntPtr.Zero)
            return result;

        uint bufferSize = 0;
        _ = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref bufferSize, out _, IntPtr.Zero);
        if (bufferSize == 0)
            return result;

        IntPtr buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            uint hr = PdhGetFormattedCounterArrayW(counter, PdhFmtDouble, ref bufferSize, out uint itemCount, buffer);
            if (hr != ErrorSuccess)
                return result;

            int itemSize = Marshal.SizeOf<PdhFmtCountervalueItem>();
            for (int i = 0; i < itemCount; i++)
            {
                IntPtr itemPtr = new IntPtr(buffer.ToInt64() + i * itemSize);
                var item = Marshal.PtrToStructure<PdhFmtCountervalueItem>(itemPtr);
                string? name = Marshal.PtrToStringUni(item.Name);
                double value = new DoubleBits { UInt64 = item.Bits }.Double;
                if (!string.IsNullOrWhiteSpace(name) && !double.IsNaN(value))
                    result.Add((name, value));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }
}
