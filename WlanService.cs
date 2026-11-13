using System.Runtime.InteropServices;
using System.Text;

namespace MinePix;

public sealed record WifiNetwork(string Ssid, int SignalQuality, bool SecurityEnabled, bool Connected, bool HasProfile);

/// <summary>
/// Native WLAN client API. Fully local: lists networks this PC can see, and connects
/// to networks with a stored profile. Never sees or stores passwords.
/// </summary>
internal static class WlanService
{
    public const int WlanMaxNameLength = 256;
    public const int Dot11SsidMaxLength = 32;
    private const int WlanApiVersion2 = 2;
    private const uint Dot11BssTypeInfrastructure = 1;
    private const int WlanProfileTypeAllUser = 0; // WLAN_PROFILE_TYPE_ALL_USER (for SetProfile call below)

    [StructLayout(LayoutKind.Sequential)]
    private struct DOT11_SSID
    {
        public uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = Dot11SsidMaxLength)]
        public byte[] ucSSID;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_AVAILABLE_NETWORK
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WlanMaxNameLength)]
        public string strProfileName;
        public DOT11_SSID dot11Ssid;
        public uint dot11BssType;
        public uint uNumberOfBssids;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bNetworkConnectable;
        public uint wlanNotConnectableReason;
        public uint uNumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] dot11PhyTypes;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bMorePhyTypes;
        public uint wlanSignalQuality;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bSecurityEnabled;
        public uint dot11DefaultAuthAlgorithm;
        public uint dot11DefaultCipherAlgorithm;
        public uint dwFlags;
        public uint dwReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = WlanMaxNameLength)]
        public string strInterfaceDescription;
        public uint isState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_INTERFACE_INFO_LIST
    {
        public uint dwNumberOfItems;
        public uint dwIndex;
        // followed by dwNumberOfItems * WLAN_INTERFACE_INFO — read via pointer walk
        public WLAN_INTERFACE_INFO InterfaceInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WLAN_AVAILABLE_NETWORK_LIST
    {
        public uint dwNumberOfItems;
        public uint dwIndex;
        public WLAN_AVAILABLE_NETWORK Network; // variable-length, walk manually
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_CONNECTION_PARAMETERS
    {
        public uint wlanConnectionMode;
        [MarshalAs(UnmanagedType.LPWStr)] public string strProfile;
        public IntPtr pDot11Ssid;
        public IntPtr pDesiredBssidList;
        public uint dot11BssType;
        public uint dwFlags;
    }

    [DllImport("wlanapi.dll")]
    private static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanGetAvailableNetworkList(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint dwFlags, IntPtr pReserved, out IntPtr ppAvailableNetworkList);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanConnect(IntPtr hClientHandle, ref Guid pInterfaceGuid, ref WLAN_CONNECTION_PARAMETERS pConnectionParameters, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanDisconnect(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern uint WlanScan(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);

    [DllImport("wlanapi.dll")]
    private static extern void WlanFreeMemory(IntPtr pMemory);

    private const uint WlanConnected = 0x1;
    private const uint WlanHasProfile = 0x2;

    private static IReadOnlyList<Guid> InterfaceGuids(IntPtr clientHandle)
    {
        var guids = new List<Guid>();
        if (WlanEnumInterfaces(clientHandle, IntPtr.Zero, out var listPtr) != 0 || listPtr == IntPtr.Zero)
            return guids;

        // WLAN_INTERFACE_INFO_LIST is a length-prefixed flexible array — compute offsets by hand
        int headerSize = Marshal.SizeOf<uint>() * 2; // dwNumberOfItems + dwIndex
        int infoSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
        uint count = (uint)Marshal.ReadInt32(listPtr);
        for (int i = 0; i < count; i++)
        {
            var entry = listPtr + headerSize + i * infoSize;
            var bytes = new byte[16];
            Marshal.Copy(entry, bytes, 0, 16);
            guids.Add(new Guid(bytes));
        }
        WlanFreeMemory(listPtr);
        return guids;
    }

    private static int AvailableNetworkOffset(int index)
    {
        // WLAN_AVAILABLE_NETWORK_LIST header: DWORD count + DWORD index; then flexible array
        int header = Marshal.SizeOf<uint>() * 2;
        int networkSize = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();
        return header + index * networkSize;
    }

    public static List<WifiNetwork> Networks()
    {
        var result = new List<WifiNetwork>();
        if (WlanOpenHandle(WlanApiVersion2, IntPtr.Zero, out _, out var handle) != 0)
            return result;

        foreach (var iface in InterfaceGuids(handle))
        {
            var copy = iface;
            if (WlanGetAvailableNetworkList(handle, ref copy, 1 /* include all manual hidden profiles */, IntPtr.Zero, out var listPtr) != 0 || listPtr == IntPtr.Zero)
                continue;

            uint count = (uint)Marshal.ReadInt32(listPtr);
            int networkSize = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();
            int header = Marshal.SizeOf<uint>() * 2;

            for (int i = 0; i < count; i++)
            {
                var addr = listPtr + header + i * networkSize;
                var network = Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(addr);

                string ssid = SsidToString(network.dot11Ssid);
                if (string.IsNullOrWhiteSpace(ssid)) continue;
                if (result.Any(n => n.Ssid == ssid)) continue; // same SSID on multiple interfaces -> show once

                result.Add(new WifiNetwork(
                    ssid,
                    (int)network.wlanSignalQuality,
                    network.bSecurityEnabled,
                    (network.dwFlags & WlanConnected) != 0,
                    (network.dwFlags & WlanHasProfile) != 0));
            }
            WlanFreeMemory(listPtr);
        }

        // strongest signal first, connected first
        return result
            .OrderByDescending(n => n.Connected)
            .ThenByDescending(n => n.SignalQuality)
            .ToList();
    }

    public static bool Connect(string profileName)
    {
        if (WlanOpenHandle(WlanApiVersion2, IntPtr.Zero, out _, out var handle) != 0)
            return false;

        foreach (var iface in InterfaceGuids(handle))
        {
            var copy = iface;
            var parameters = new WLAN_CONNECTION_PARAMETERS
            {
                wlanConnectionMode = 0, // profile
                strProfile = profileName,
                pDot11Ssid = IntPtr.Zero,
                pDesiredBssidList = IntPtr.Zero,
                dot11BssType = Dot11BssTypeInfrastructure,
                dwFlags = 0,
            };
            if (WlanConnect(handle, ref copy, ref parameters, IntPtr.Zero) == 0)
                return true;
        }
        return false;
    }

    private const uint WlanProfileTypeAllUserUint = 0; // for WlanSetProfile API

    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
    private static extern uint WlanSetProfile(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint dwFlags, string strProfileXml, string? strAllUserProfiles, [MarshalAs(UnmanagedType.Bool)] bool bOverwrite, IntPtr pReserved, out uint pdwReasonCode);

    /// <summary>
    /// Join an open network by installing a temporary all-user open profile and connecting to it.
    /// Never handles passwords — this only works for networks with no security.
    /// </summary>
    public static bool EnsureOpenProfileAndConnect(string ssid)
    {
        if (WlanOpenHandle(WlanApiVersion2, IntPtr.Zero, out _, out var handle) != 0)
            return false;

        var escaped = System.Security.SecurityElement.Escape(ssid);
        var xml = $@"<?xml version=""1.0""?>
<WLANProfile xmlns=""http://www.microsoft.com/networking/WLAN/profile/v1"">
    <name>{escaped}</name>
    <SSIDConfig><SSID><hex>{SsidToHex(ssid)}</hex><name>{escaped}</name></SSID></SSIDConfig>
    <connectionType>ESS</connectionType>
    <MSM><security><authEncryption><authentication>open</authentication><encryption>none</encryption><useOneX>false</useOneX></authEncryption></security></MSM>
</WLANProfile>";

        foreach (var iface in InterfaceGuids(handle))
        {
            var copy = iface;
            WlanSetProfile(handle, ref copy, WlanProfileTypeAllUserUint, xml, null, true, IntPtr.Zero, out _);
            var parameters = new WLAN_CONNECTION_PARAMETERS
            {
                wlanConnectionMode = 0,
                strProfile = ssid,
                pDot11Ssid = IntPtr.Zero,
                pDesiredBssidList = IntPtr.Zero,
                dot11BssType = Dot11BssTypeInfrastructure,
                dwFlags = 0,
            };
            if (WlanConnect(handle, ref copy, ref parameters, IntPtr.Zero) == 0)
                return true;
        }
        return false;
    }

    private static string SsidToHex(string ssid)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(ssid);
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.AppendFormat("{0:x2}", b);
        return sb.ToString();
    }

    public static bool Disconnect()
    {
        if (WlanOpenHandle(WlanApiVersion2, IntPtr.Zero, out _, out var handle) != 0)
            return false;

        foreach (var iface in InterfaceGuids(handle))
        {
            var copy = iface;
            WlanDisconnect(handle, ref copy, IntPtr.Zero);
        }
        return true;
    }

    public static void Scan()
    {
        if (WlanOpenHandle(WlanApiVersion2, IntPtr.Zero, out _, out var handle) != 0)
            return;

        foreach (var iface in InterfaceGuids(handle))
        {
            var copy = iface;
            WlanScan(handle, ref copy, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private static string SsidToString(DOT11_SSID ssid)
    {
        if (ssid.uSSIDLength == 0 || ssid.ucSSID is null) return "";
        int len = (int)Math.Min(ssid.uSSIDLength, (uint)Dot11SsidMaxLength);
        // SSIDs are usually ASCII but can be raw UTF-8 bytes
        return Encoding.UTF8.GetString(ssid.ucSSID, 0, len).TrimEnd('\0');
    }
}
