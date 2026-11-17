using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MinePix;

/// <summary>DXGI adapter snapshot: name, LUID and dedicated VRAM for every GPU on the box.</summary>
internal sealed class DxgiAdapterInfo
{
    public string Description = "";
    public ulong Luid = 0;
    public ulong DedicatedVideoMemory = 0;
}

/// <summary>
/// Minimal DXGI enumeration. Used only to label the game strip with the right adapter
/// (RTX 3050 vs Radeon 680M) and its VRAM total — never to render anything.
/// </summary>
internal static class DxgiAdapters
{
    private static List<DxgiAdapterInfo>? _cache;

private static void DxgiLog(string message)
{
    if (!string.Equals(Environment.GetEnvironmentVariable("MINEPIX_APILOG"), "1", StringComparison.Ordinal))
        return;
    try
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MinePix");
        Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir, "dxgi.log"), $"{DateTime.Now:o} {message}{Environment.NewLine}");
    }
    catch { }
}

public static IReadOnlyList<DxgiAdapterInfo> Enumerate()
{
        if (_cache is not null)
            return _cache;
        var list = new List<DxgiAdapterInfo>();
        try
        {
            var iid = IID_IDXGIFactory1;
            int hr = CreateDXGIFactory1(ref iid, out IntPtr factoryPtr);
            DxgiLog($"factory hr=0x{hr:X8} ptr=0x{factoryPtr:X}");
            if (hr != 0 || factoryPtr == IntPtr.Zero)
            {
                _cache = list;
                return _cache;
            }

            var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
            for (uint i = 0; ; i++)
            {
                int r;
                IntPtr adapterPtr;
                try
                {
                    r = factory.EnumAdapters1(i, out adapterPtr);
                }
                catch (System.Runtime.InteropServices.COMException ex)
                {
                    DxgiLog($"enum stop at i={i}: 0x{ex.HResult:X8}");
                    break;
                }
                DxgiLog($"enum i={i} hr=0x{r:X8} ptr=0x{adapterPtr:X}");
                if (r != 0 || adapterPtr == IntPtr.Zero)
                    break;
                var adapter = (IDXGIAdapter1)Marshal.GetObjectForIUnknown(adapterPtr);
                int rg = adapter.GetDesc1(out DXGI_ADAPTER_DESC1 desc);
                DxgiLog($"desc hr=0x{rg:X8} desc='{desc.Description}' vendor=0x{desc.VendorId:X8} dedicated={desc.DedicatedVideoMemory / (1024 * 1024):0}MB luidHi=0x{desc.AdapterLuid.Part2:X8}");
                list.Add(new DxgiAdapterInfo
                {
                    Description = desc.Description ?? "",
                    Luid = ((ulong)desc.AdapterLuid.Part2 << 32) | (uint)desc.AdapterLuid.Part1,
                    DedicatedVideoMemory = desc.DedicatedVideoMemory,
                });
            }
            // factory disposed by RCW finalizer
        }
        catch (Exception ex)
        {
            DxgiLog("enumerate threw: " + ex);
        }
        _cache = list;
        return _cache;
    }

    /// <summary>Match a perf-counter LUID token ("0x<hi32>_0x<lo32>") to a DXGI adapter, or null.</summary>
    public static DxgiAdapterInfo? FindByPerfCounterLuid(string luidToken)
    {
        var parts = (luidToken ?? "").Split('_');
        if (parts.Length < 2)
            return null;

        static ulong Hex(string s) => ulong.TryParse(s.TrimStart('0', 'x', 'X'), System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0;
        ulong hi = Hex(parts[0]);
        ulong lo = Hex(parts[1]);

        foreach (var a in Enumerate())
        {
            if ((a.Luid >> 32) == hi && (a.Luid & 0xFFFFFFFFUL) == lo)
                return a;
        }
        // fall back: some drivers swap the hi/lo groups
        foreach (var a in Enumerate())
        {
            if ((a.Luid >> 32) == lo && (a.Luid & 0xFFFFFFFFUL) == hi)
                return a;
        }
        return null;
    }

    private static readonly Guid IID_IDXGIFactory1 = new("{770aae78-f26f-4dba-a829-253c83d1b387}");

    [DllImport("dxgi.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = false)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        // IUnknown (3) handled by InterfaceIsIUnknown
        int SetPrivateData(in Guid name, uint dataSize, IntPtr data);
        int SetPrivateDataInterface(in Guid name, IntPtr unknown);
        int GetPrivateData(in Guid name, ref uint dataSize, IntPtr data);
        int GetParent(in Guid riid, out IntPtr parent);
        int MakeWindowAssociation(IntPtr hwnd, uint flags);
        int GetWindowAssociation(out IntPtr hwnd);
        int CreateSwapChain(IntPtr device, IntPtr desc, out IntPtr swapChain);
        int CreateSoftwareAdapter(IntPtr module, out IntPtr adapter);
        int EnumAdapters(uint index, out IntPtr adapter);
        int EnumAdapters1(uint index, out IntPtr adapter);
        int IsCurrent();
    }

    [ComImport]
    [Guid("29038F61-3839-4626-91FD-086879011A05")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        int SetPrivateData(in Guid name, uint dataSize, IntPtr data);
        int SetPrivateDataInterface(in Guid name, IntPtr unknown);
        int GetPrivateData(in Guid name, ref uint dataSize, IntPtr data);
        int GetParent(in Guid riid, out IntPtr parent);
        int EnumOutputs(uint index, out IntPtr output);
        int GetDesc(out DXGI_ADAPTER_DESC desc);
        int CheckInterfaceSupport(in Guid interfaceName, out long userModeVersion);
        int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public int Part1;
        public uint Part2;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public ulong DedicatedVideoMemory;
        public ulong DedicatedSystemMemory;
        public ulong SharedSystemMemory;
        public Luid AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public ulong DedicatedVideoMemory;
        public ulong DedicatedSystemMemory;
        public ulong SharedSystemMemory;
        public Luid AdapterLuid;
        public uint Flags;
    }
}
