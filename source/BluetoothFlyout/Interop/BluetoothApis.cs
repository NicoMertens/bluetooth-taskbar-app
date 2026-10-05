using System.Runtime.InteropServices;

namespace BluetoothFlyout.Interop;

/// <summary>
/// P/Invoke for bthprops.cpl (BluetoothAPIs). Windows exposes no public API to
/// connect or disconnect a paired BR/EDR device; toggling its service state is
/// the supported-ish mechanism the Settings app ultimately drives too.
/// </summary>
internal static class BluetoothApis
{
    public const int BLUETOOTH_MAX_NAME_SIZE = 248;

    public const uint BLUETOOTH_SERVICE_DISABLE = 0x00;
    public const uint BLUETOOTH_SERVICE_ENABLE = 0x01;

    private const int ERROR_SUCCESS = 0;
    private const int ERROR_MORE_DATA = 234;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BLUETOOTH_DEVICE_INFO
    {
        public int dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public int fConnected;
        public int fRemembered;
        public int fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = BLUETOOTH_MAX_NAME_SIZE)]
        public string szName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public int dwSize;
    }

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool BluetoothFindNextRadio(IntPtr hFind, out IntPtr phRadio);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
    private static extern int BluetoothGetDeviceInfo(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
    private static extern int BluetoothEnumerateInstalledServices(
        IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref int pcServiceInout, [In, Out] Guid[]? pGuidServices);

    [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
    private static extern int BluetoothSetServiceState(
        IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwServiceFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>Enumerates the local Bluetooth radio handles. Caller must dispose the result.</summary>
    public static RadioHandles OpenRadios()
    {
        var handles = new List<IntPtr>();
        var p = new BLUETOOTH_FIND_RADIO_PARAMS { dwSize = Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };

        IntPtr find = BluetoothFindFirstRadio(ref p, out IntPtr radio);
        if (find == IntPtr.Zero)
            return new RadioHandles(handles);

        try
        {
            handles.Add(radio);
            while (BluetoothFindNextRadio(find, out IntPtr next))
                handles.Add(next);
        }
        finally
        {
            BluetoothFindRadioClose(find);
        }

        return new RadioHandles(handles);
    }

    /// <summary>
    /// Looks the device up on any local radio. Returns false when no radio knows the address.
    /// </summary>
    public static bool TryGetDeviceInfo(RadioHandles radios, ulong address,
        out IntPtr radio, out BLUETOOTH_DEVICE_INFO info)
    {
        foreach (IntPtr candidate in radios.Handles)
        {
            var btdi = new BLUETOOTH_DEVICE_INFO
            {
                dwSize = Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>(),
                Address = address,
                szName = string.Empty,
            };

            if (BluetoothGetDeviceInfo(candidate, ref btdi) == ERROR_SUCCESS)
            {
                radio = candidate;
                info = btdi;
                return true;
            }
        }

        radio = IntPtr.Zero;
        info = default;
        return false;
    }

    /// <summary>Service GUIDs currently installed for the device (empty after a disconnect).</summary>
    public static Guid[] GetInstalledServices(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info)
    {
        int count = 0;
        int result = BluetoothEnumerateInstalledServices(radio, ref info, ref count, null);
        if (count == 0 || (result != ERROR_SUCCESS && result != ERROR_MORE_DATA))
            return [];

        var services = new Guid[count];
        result = BluetoothEnumerateInstalledServices(radio, ref info, ref count, services);
        if (result != ERROR_SUCCESS)
            return [];

        return count == services.Length ? services : services[..count];
    }

    /// <summary>Returns the Win32 error code; 0 means success.</summary>
    public static int SetServiceState(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, Guid service, bool enable)
        => BluetoothSetServiceState(radio, ref info, ref service,
            enable ? BLUETOOTH_SERVICE_ENABLE : BLUETOOTH_SERVICE_DISABLE);

    internal sealed class RadioHandles(List<IntPtr> handles) : IDisposable
    {
        public IReadOnlyList<IntPtr> Handles { get; } = handles;

        public void Dispose()
        {
            foreach (IntPtr h in Handles)
            {
                if (h != IntPtr.Zero)
                    CloseHandle(h);
            }
        }
    }
}
