using BluetoothFlyout.Interop;

namespace BluetoothFlyout.Bluetooth;

/// <summary>
/// Flags paired BR/EDR devices that belong to a different adapter.
/// <para>
/// Windows keeps a pairing listed after the adapter it was made with is gone —
/// swap a dongle and the entry survives, usually for hardware that is long gone
/// too. The current radio cannot reach those devices at all, so the flyout drops
/// them; they stay visible in the Windows Bluetooth settings, where they can be
/// removed for good.
/// </para>
/// </summary>
public static class AvailabilityProbe
{
    /// <summary>
    /// Sets <see cref="BluetoothDeviceModel.IsReachable"/> for each device. Cheap
    /// enough for the UI thread — these are local lookups against the radio.
    /// </summary>
    public static void Populate(IReadOnlyList<BluetoothDeviceModel> devices)
    {
        try
        {
            using BluetoothApis.RadioHandles radios = BluetoothApis.OpenRadios();

            // No radio means we cannot judge anything — "unknown" must not be
            // mistaken for "orphaned", or switching Bluetooth off would empty the list.
            if (radios.Handles.Count == 0)
            {
                MarkAllReachable(devices);
                return;
            }

            foreach (BluetoothDeviceModel device in devices)
            {
                // LE pairings are not kept in the BR/EDR store, so there is nothing
                // to check against — assume reachable and let the attempt report.
                device.IsReachable = !device.SupportsClassic
                    || BluetoothApis.TryGetDeviceInfo(radios, device.Address, out _, out _);
            }
        }
        catch (Exception)
        {
            // Never let a probe failure hide devices; fall back to "reachable".
            MarkAllReachable(devices);
        }
    }

    private static void MarkAllReachable(IReadOnlyList<BluetoothDeviceModel> devices)
    {
        foreach (BluetoothDeviceModel device in devices)
            device.IsReachable = true;
    }
}
