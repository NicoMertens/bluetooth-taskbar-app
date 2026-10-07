using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration.Pnp;

namespace Nc2.BluetoothTaskbarApp.Bluetooth;

/// <summary>
/// Fills in the per-device detail that needs a PnP lookup: battery level, and
/// whether Windows' own HID stack owns the connection.
/// <para>
/// Both answers come from one enumeration of the device tree, because that call
/// returns every node on the machine and is far too expensive to make twice.
/// </para>
/// </summary>
public static class DeviceDetailsProbe
{
    /// <summary>GUID_DEVCLASS_HIDCLASS — the setup class of keyboards, mice and gamepads.</summary>
    private static readonly Guid HidDeviceClass = new("745a17a0-74d3-11d0-b6fe-00a0c90f57da");

    private const string ClassGuidKey = "System.Devices.ClassGuid";

    /// <summary>DEVPKEY_Bluetooth_Battery — the level Windows shows in Settings.</summary>
    private const string BatteryPropertyKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    private const string ContainerIdKey = "System.Devices.ContainerId";

    private static readonly string[] PnpProperties = [BatteryPropertyKey, ContainerIdKey, ClassGuidKey];

    /// <summary>Reading GATT touches the radio; keep it from hanging the refresh.</summary>
    private static readonly TimeSpan GattTimeout = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Fills in battery level and HID ownership for every device we can resolve.
    /// Never throws; unresolved devices simply keep their defaults.
    /// </summary>
    public static async Task PopulateAsync(IReadOnlyList<BluetoothDeviceModel> devices, CancellationToken token)
    {
        (Dictionary<string, int> battery, HashSet<string> hidContainers) = await ReadPnpAsync().ConfigureAwait(false);

        foreach (BluetoothDeviceModel device in devices)
        {
            if (battery.TryGetValue(device.Key, out int percent))
                device.BatteryPercent = percent;

            // Only LE-only devices are a problem: a BR/EDR peripheral can still be
            // torn down through BluetoothSetServiceState, HID or not.
            device.IsHeldByWindows = device.SupportsLe
                && !device.SupportsClassic
                && hidContainers.Contains(device.Key);
        }

        // GATT fallback, only where it can actually succeed and is still missing.
        List<BluetoothDeviceModel> needGatt = [.. devices.Where(d =>
            d.BatteryPercent is null && d.IsConnected && d.SupportsLe)];

        if (needGatt.Count == 0)
            return;

        await Task.WhenAll(needGatt.Select(async device =>
        {
            device.BatteryPercent = await ReadGattBatteryAsync(device.LeId!, token).ConfigureAwait(false);
        })).ConfigureAwait(false);
    }

    private static async Task<(Dictionary<string, int> Battery, HashSet<string> Hid)> ReadPnpAsync()
    {
        var battery = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // There is no AQS predicate for "has this vendor property", so we ask for
            // the property across device nodes and keep the ones that carry a value.
            PnpObjectCollection objects = await PnpObject
                .FindAllAsync(PnpObjectType.Device, PnpProperties)
                .AsTask()
                .ConfigureAwait(false);

            foreach (PnpObject item in objects)
            {
                if (!item.Properties.TryGetValue(ContainerIdKey, out object? container) || container is null)
                    continue;

                string key = BluetoothMonitor.NormalizeContainerId(container.ToString()!);

                if (item.Properties.TryGetValue(ClassGuidKey, out object? classGuid)
                    && classGuid is not null
                    && Guid.TryParse(classGuid.ToString(), out Guid parsed)
                    && parsed == HidDeviceClass)
                {
                    hid.Add(key);
                }

                if (!item.Properties.TryGetValue(BatteryPropertyKey, out object? raw) || raw is null)
                    continue;

                int percent;
                try
                {
                    percent = Convert.ToInt32(raw);
                }
                catch (Exception e) when (e is FormatException or InvalidCastException or OverflowException)
                {
                    continue;
                }

                if (percent is < 0 or > 100)
                    continue;

                battery[key] = percent;
            }
        }
        catch (Exception)
        {
            // PnP enumeration can fail transiently; treat it as "nothing known".
        }

        return (battery, hid);
    }

    private static async Task<int?> ReadGattBatteryAsync(string leDeviceId, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(GattTimeout);

        BluetoothLEDevice? device = null;
        try
        {
            device = await BluetoothLEDevice.FromIdAsync(leDeviceId).AsTask(timeout.Token).ConfigureAwait(false);
            if (device is null)
                return null;

            GattDeviceServicesResult services = await device
                .GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached)
                .AsTask(timeout.Token)
                .ConfigureAwait(false);

            if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                return null;

            using GattDeviceService service = services.Services[0];

            GattCharacteristicsResult characteristics = await service
                .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Uncached)
                .AsTask(timeout.Token)
                .ConfigureAwait(false);

            if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
                return null;

            GattReadResult read = await characteristics.Characteristics[0]
                .ReadValueAsync(BluetoothCacheMode.Uncached)
                .AsTask(timeout.Token)
                .ConfigureAwait(false);

            if (read.Status != GattCommunicationStatus.Success || read.Value.Length == 0)
                return null;

            var buffer = new byte[read.Value.Length];
            Windows.Storage.Streams.DataReader.FromBuffer(read.Value).ReadBytes(buffer);

            return buffer[0] <= 100 ? buffer[0] : null;
        }
        catch (Exception)
        {
            // Device asleep, out of range, or busy — no level this round.
            return null;
        }
        finally
        {
            device?.Dispose();
        }
    }
}
