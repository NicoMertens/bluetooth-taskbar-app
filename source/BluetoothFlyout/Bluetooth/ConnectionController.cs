using System.ComponentModel;
using BluetoothFlyout.Interop;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;

namespace BluetoothFlyout.Bluetooth;

/// <summary>
/// Connects and disconnects paired devices.
/// <para>
/// Windows exposes no public "connect this paired device" API. For BR/EDR we
/// drive <c>BluetoothSetServiceState</c>, which is what makes the stack bring the
/// link up or tear it down. For LE we hold a <see cref="GattSession"/> with
/// <c>MaintainConnection</c>, which is the documented way to keep a link alive.
/// </para>
/// </summary>
public sealed class ConnectionController : IDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";

    /// <summary>Waking a sleeping peripheral and pairing up its profiles takes a while.</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(8);

    /// <summary>The stack drops an unreferenced LE link after about a second.</summary>
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(400);

    private readonly ServiceCache _cache = new();

    /// <summary>Sessions we hold open to keep LE devices connected.</summary>
    private readonly Dictionary<string, GattSession> _leSessions = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _gate = new();

    public async Task<OperationResult> SetConnectedAsync(BluetoothDeviceModel device, bool connect)
    {
        var failures = new List<string>();
        bool anyAccepted = false;

        if (device.SupportsClassic)
        {
            OperationResult classic = await Task.Run(() => SetClassicState(device, connect)).ConfigureAwait(false);
            if (classic.Success)
                anyAccepted = true;
            else if (classic.Message is not null)
                failures.Add(classic.Message);
        }

        if (device.SupportsLe)
        {
            OperationResult le = await SetLeStateAsync(device, connect).ConfigureAwait(false);
            if (le.Success)
                anyAccepted = true;
            else if (le.Message is not null)
                failures.Add(le.Message);
        }

        if (!anyAccepted)
        {
            return failures.Count > 0
                ? OperationResult.Fail(failures[0])
                : OperationResult.Fail("Device has no controllable connection.");
        }

        // Accepting the request is not the same as it having worked: a disconnect
        // can be silently ignored when another component still holds the link.
        // Confirm against what the stack actually reports before claiming success.
        if (await ReachedStateAsync(device, connect).ConfigureAwait(false))
            return OperationResult.Ok();

        return OperationResult.Fail(DescribeStall(device, connect));
    }

    /// <summary>Polls the association endpoints until they report the wanted state.</summary>
    private static async Task<bool> ReachedStateAsync(BluetoothDeviceModel device, bool connect)
    {
        TimeSpan budget = connect ? ConnectTimeout : DisconnectTimeout;
        DateTime deadline = DateTime.UtcNow + budget;

        while (true)
        {
            await Task.Delay(PollInterval).ConfigureAwait(false);

            bool? state = await ReadConnectedAsync(device).ConfigureAwait(false);
            if (state == connect)
                return true;

            if (DateTime.UtcNow >= deadline)
                return false;
        }
    }

    /// <summary>
    /// Reads <c>System.Devices.Aep.IsConnected</c> for the device. This is a
    /// passive property read — unlike <c>BluetoothLEDevice.FromIdAsync</c> it does
    /// not itself take a reference that could bring the link back up.
    /// </summary>
    private static async Task<bool?> ReadConnectedAsync(BluetoothDeviceModel device)
    {
        bool? result = null;

        foreach (string? id in new[] { device.ClassicId, device.LeId })
        {
            if (id is null)
                continue;

            try
            {
                DeviceInformation info = await DeviceInformation
                    .CreateFromIdAsync(id, [IsConnectedProperty], DeviceInformationKind.AssociationEndpoint)
                    .AsTask()
                    .ConfigureAwait(false);

                if (info.Properties.TryGetValue(IsConnectedProperty, out object? value) && value is bool connected)
                    result = (result ?? false) || connected;
            }
            catch (Exception)
            {
                // Endpoint vanished or is momentarily unreadable; other transport may still answer.
            }
        }

        return result;
    }

    private static string DescribeStall(BluetoothDeviceModel device, bool connect)
    {
        if (connect)
            return "Device not responding";

        // HID over GATT is held open by the system's own driver. Windows exposes no
        // way for an app to release that, which is why Settings offers no Disconnect
        // for these devices either.
        bool hidOverGatt = device.SupportsLe && !device.SupportsClassic
            && device.Kind is DeviceKind.Keyboard or DeviceKind.Mouse or DeviceKind.Gamepad;

        return hidOverGatt
            ? "Windows will not release HID devices"
            : "Connection is still held";
    }

    // ---- BR/EDR ---------------------------------------------------------

    private OperationResult SetClassicState(BluetoothDeviceModel device, bool connect)
    {
        using BluetoothApis.RadioHandles radios = BluetoothApis.OpenRadios();

        if (radios.Handles.Count == 0)
            return OperationResult.Fail("No Bluetooth adapter found.");

        if (!BluetoothApis.TryGetDeviceInfo(radios, device.Address, out IntPtr radio,
                out BluetoothApis.BLUETOOTH_DEVICE_INFO info))
        {
            return OperationResult.Fail("Device is unknown to the adapter.");
        }

        Guid[] services = BluetoothApis.GetInstalledServices(radio, ref info);

        if (connect)
        {
            // After a disconnect the services are gone from enumeration, so fall
            // back to what we cached and then to the profiles the class implies.
            if (services.Length == 0)
                services = _cache.Get(device.Address) ?? BluetoothProfiles.ForKind(device.Kind);
        }
        else if (services.Length > 0)
        {
            // Remember them while we still can; the next connect needs this list.
            _cache.Set(device.Address, services);
        }

        if (services.Length == 0)
            return OperationResult.Fail("No known services for this device.");

        int lastError = 0;
        bool anySucceeded = false;

        foreach (Guid service in services)
        {
            int result = BluetoothApis.SetServiceState(radio, ref info, service, connect);
            if (result == 0)
                anySucceeded = true;
            else
                lastError = result;
        }

        if (anySucceeded)
            return OperationResult.Ok();

        string reason = lastError != 0
            ? new Win32Exception(lastError).Message
            : "Unknown error.";

        return OperationResult.Fail(reason);
    }

    // ---- LE -------------------------------------------------------------

    private async Task<OperationResult> SetLeStateAsync(BluetoothDeviceModel device, bool connect)
    {
        string id = device.LeId!;

        if (!connect)
        {
            CloseLeSession(id);
            return OperationResult.Ok();
        }

        lock (_gate)
        {
            if (_leSessions.ContainsKey(id))
                return OperationResult.Ok();
        }

        BluetoothLEDevice? leDevice = null;
        try
        {
            leDevice = await BluetoothLEDevice.FromIdAsync(id).AsTask().ConfigureAwait(false);
            if (leDevice is null)
                return OperationResult.Fail("LE device unreachable.");

            GattSession session = await GattSession
                .FromDeviceIdAsync(leDevice.BluetoothDeviceId)
                .AsTask()
                .ConfigureAwait(false);

            session.MaintainConnection = true;

            lock (_gate)
            {
                // Lost a race with another connect — keep the first session.
                if (!_leSessions.TryAdd(id, session))
                {
                    session.Dispose();
                    return OperationResult.Ok();
                }
            }

            return OperationResult.Ok();
        }
        catch (Exception e)
        {
            return OperationResult.Fail(e.Message);
        }
        finally
        {
            leDevice?.Dispose();
        }
    }

    private void CloseLeSession(string id)
    {
        GattSession? session;
        lock (_gate)
        {
            if (!_leSessions.Remove(id, out session))
                return;
        }

        try
        {
            session.MaintainConnection = false;
            session.Dispose();
        }
        catch (Exception)
        {
            // The session may already be torn down by the stack.
        }
    }

    public void Dispose()
    {
        List<GattSession> sessions;
        lock (_gate)
        {
            sessions = [.. _leSessions.Values];
            _leSessions.Clear();
        }

        foreach (GattSession session in sessions)
        {
            try
            {
                session.MaintainConnection = false;
                session.Dispose();
            }
            catch (Exception)
            {
                // Shutdown path; nothing to recover.
            }
        }
    }
}

/// <summary>Outcome of a connect/disconnect attempt.</summary>
public readonly record struct OperationResult(bool Success, string? Message)
{
    public static OperationResult Ok() => new(true, null);

    public static OperationResult Fail(string message) => new(false, message);
}

/// <summary>Service UUIDs to enable when we have no record of a device's profiles.</summary>
internal static class BluetoothProfiles
{
    private static Guid Sig(ushort id) => new($"0000{id:x4}-0000-1000-8000-00805f9b34fb");

    private static readonly Guid Headset = Sig(0x1108);
    private static readonly Guid AudioSink = Sig(0x110B);
    private static readonly Guid AvRemoteControlTarget = Sig(0x110C);
    private static readonly Guid Handsfree = Sig(0x111E);
    private static readonly Guid HumanInterfaceDevice = Sig(0x1124);

    private static readonly Guid[] Audio = [Headset, Handsfree, AudioSink, AvRemoteControlTarget];
    private static readonly Guid[] Hid = [HumanInterfaceDevice];
    private static readonly Guid[] Everything = [.. Audio, .. Hid];

    public static Guid[] ForKind(DeviceKind kind) => kind switch
    {
        DeviceKind.Headset or DeviceKind.Speaker => Audio,
        DeviceKind.Keyboard or DeviceKind.Mouse or DeviceKind.Gamepad => Hid,
        _ => Everything,
    };
}
