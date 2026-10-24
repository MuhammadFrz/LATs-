using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LinkDesk;

public sealed class NativeWifi : IDisposable
{
    private IntPtr handle;
    private readonly NotificationCallback callback;
    private Guid scanningAdapter;
    private TaskCompletionSource<uint>? scanCompletion;
    private readonly object scanLock = new();
    private Guid? pendingConnection;

    public NativeWifi()
    {
        Check(WlanOpenHandle(2, IntPtr.Zero, out _, out handle), "Open Wi-Fi service");
        callback = OnNotification;
        try { Check(WlanRegisterNotification(handle, 8, false, callback, IntPtr.Zero, IntPtr.Zero, out _), "Register Wi-Fi notifications"); }
        catch { WlanCloseHandle(handle, IntPtr.Zero); handle = IntPtr.Zero; throw; }
    }

    public async Task<IReadOnlyList<WifiNetwork>> ScanAsync(CancellationToken cancellationToken)
    {
        var networks = new List<WifiNetwork>();
        Check(WlanEnumInterfaces(handle, IntPtr.Zero, out var list), "List Wi-Fi adapters");
        try
        {
            int count = Marshal.ReadInt32(list);
            if (count == 0) throw new InvalidOperationException("No Wi-Fi adapters found. Enable your wireless adapter in Windows Settings.");
            for (int i = 0; i < count; i++)
            {
                var adapter = Marshal.PtrToStructure<InterfaceInfo>(IntPtr.Add(list, 8 + i * Marshal.SizeOf<InterfaceInfo>()));
                var completion = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (scanLock) { scanningAdapter = adapter.Id; scanCompletion = completion; }
                try
                {
                    Check(WlanScan(handle, ref adapter.Id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero), "Scan for hotspots");
                    uint reason;
                    try { reason = await completion.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); }
                    catch (TimeoutException) { throw new TimeoutException("Windows did not finish scanning within 15 seconds. Check Wi-Fi and Location settings, then scan again."); }
                    if (reason != 0) throw new InvalidOperationException($"Windows Wi-Fi scan failed (reason {reason}). Check airplane mode and that the adapter is enabled.");
                    ReadNetworks(adapter, networks);
                }
                finally { lock (scanLock) { scanCompletion = null; } }
            }
        }
        finally { WlanFreeMemory(list); }
        return networks.OrderByDescending(n => n.Signal).DistinctBy(n => n.Identity).ToArray();
    }

    private void ReadNetworks(InterfaceInfo adapter, List<WifiNetwork> networks)
    {
        Check(WlanGetAvailableNetworkList(handle, ref adapter.Id, 0, IntPtr.Zero, out var list), "Read nearby hotspots");
        try
        {
            int count = Marshal.ReadInt32(list);
            for (int i = 0; i < count; i++)
            {
                var network = Marshal.PtrToStructure<AvailableNetwork>(IntPtr.Add(list, 8 + i * Marshal.SizeOf<AvailableNetwork>()));
                if (network.Ssid.Length is 0 or > 32 || network.BssType != 1) continue;
                networks.Add(new WifiNetwork(adapter.Id, adapter.Description, network.Ssid.Bytes[..(int)network.Ssid.Length],
                    network.Signal, network.Auth, network.Cipher, network.Connectable, network.NotConnectableReason));
            }
        }
        finally { WlanFreeMemory(list); }
    }

    public async Task ConnectAsync(WifiNetwork network, string password, CancellationToken cancellationToken)
    {
        // Temporary profiles avoid writing the supplied password to a saved Wi-Fi profile.
        var parameters = new ConnectionParameters { Mode = 1, Profile = WifiProfile.Create(network, password), BssType = 1 };
        Guid adapter = network.AdapterId;
        cancellationToken.ThrowIfCancellationRequested();
        Check(WlanConnect(handle, ref adapter, ref parameters, IntPtr.Zero), "Start Wi-Fi connection");
        pendingConnection = adapter;
        try
        {
            var elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsConnected(network)) { pendingConnection = null; return; }
                await Task.Delay(350, cancellationToken);
            }
            throw new TimeoutException("Windows did not associate with the phone within 30 seconds. Check the phone password, restart its hotspot, and try again. Internet forwarding has not been tested.");
        }
        finally
        {
            // Stop our pending request on cancellation/failure, not an unrelated adapter.
            if (pendingConnection is Guid ownedAdapter)
            {
                Check(WlanDisconnect(handle, ref ownedAdapter, IntPtr.Zero), "Stop pending Wi-Fi connection");
                pendingConnection = null;
            }
        }
    }

    private bool IsConnected(WifiNetwork network)
    {
        Guid adapter = network.AdapterId;
        uint result = WlanQueryInterface(handle, ref adapter, 7, IntPtr.Zero, out uint size, out var data, out _);
        if (result == 5023) return false; // ERROR_INVALID_STATE while disconnected/transitioning.
        Check(result, "Read Wi-Fi connection state");
        try
        {
            // WLAN_CONNECTION_ATTRIBUTES: state, mode, profile[256], association.ssid.
            if (size < 556 || Marshal.ReadInt32(data) != 1) return false;
            int length = Marshal.ReadInt32(data, 520);
            if (length is < 1 or > 32) return false;
            byte[] ssid = new byte[length];
            Marshal.Copy(IntPtr.Add(data, 524), ssid, 0, length);
            return ssid.AsSpan().SequenceEqual(network.Ssid);
        }
        finally { WlanFreeMemory(data); }
    }

    private void OnNotification(ref Notification data, IntPtr context)
    {
        if (data.Source != 8 || data.Code is not (7 or 8)) return;
        lock (scanLock)
        {
            if (data.Adapter != scanningAdapter) return;
            uint reason = data.Code == 7 ? 0 : data.Size >= 4 && data.Data != IntPtr.Zero ? unchecked((uint)Marshal.ReadInt32(data.Data)) : 1;
            scanCompletion?.TrySetResult(reason);
        }
    }

    private static void Check(uint code, string operation)
    {
        if (code == 0) return;
        if (code == 5) throw new UnauthorizedAccessException("Windows denied Wi-Fi access. Open Settings → Privacy & security → Location, enable Location services and allow apps to access location, then retry.");
        throw new Win32Exception((int)code, $"{operation}: {new Win32Exception((int)code).Message} (Windows error {code}).");
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero) return;
        // WlanCloseHandle waits for notification callbacks to finish; do not hold scanLock here.
        WlanCloseHandle(handle, IntPtr.Zero);
        handle = IntPtr.Zero;
        GC.KeepAlive(callback);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct InterfaceInfo
    {
        public Guid Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        public uint State;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Ssid
    {
        public uint Length;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Bytes;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct AvailableNetwork
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Profile;
        public Ssid Ssid;
        public uint BssType, BssidCount;
        [MarshalAs(UnmanagedType.Bool)] public bool Connectable;
        public uint NotConnectableReason, PhyCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)] public uint[] PhyTypes;
        [MarshalAs(UnmanagedType.Bool)] public bool MorePhyTypes;
        public uint Signal;
        [MarshalAs(UnmanagedType.Bool)] public bool SecurityEnabled;
        public uint Auth, Cipher, Flags, Reserved;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ConnectionParameters
    {
        public uint Mode;
        [MarshalAs(UnmanagedType.LPWStr)] public string Profile;
        public IntPtr Ssid, BssidList;
        public uint BssType, Flags;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Notification
    {
        public uint Source, Code;
        public Guid Adapter;
        public uint Size;
        public IntPtr Data;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void NotificationCallback(ref Notification data, IntPtr context);
    [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
    [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanScan(IntPtr handle, ref Guid adapter, IntPtr ssid, IntPtr ieData, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanGetAvailableNetworkList(IntPtr handle, ref Guid adapter, uint flags, IntPtr reserved, out IntPtr list);
    [DllImport("wlanapi.dll")] private static extern uint WlanRegisterNotification(IntPtr handle, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicates, NotificationCallback callback, IntPtr context, IntPtr reserved, out uint previous);
    [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)] private static extern uint WlanConnect(IntPtr handle, ref Guid adapter, ref ConnectionParameters parameters, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanDisconnect(IntPtr handle, ref Guid adapter, IntPtr reserved);
    [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid adapter, uint opcode, IntPtr reserved, out uint size, out IntPtr data, out uint valueType);
}
