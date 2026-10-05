using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BluetoothFlyout.Bluetooth;

/// <summary>
/// Watches paired Bluetooth devices over both transports and keeps a merged,
/// de-duplicated view of them. Raises <see cref="Changed"/> off the UI thread.
/// </summary>
public sealed class BluetoothMonitor : IDisposable
{
    private const string PropIsConnected = "System.Devices.Aep.IsConnected";
    private const string PropAddress = "System.Devices.Aep.DeviceAddress";
    private const string PropContainerId = "System.Devices.Aep.ContainerId";
    private const string PropCodMajor = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string PropCodMinor = "System.Devices.Aep.Bluetooth.Cod.Minor";

    private static readonly string[] RequestedProperties =
    [
        PropIsConnected, PropAddress, PropContainerId, PropCodMajor, PropCodMinor,
    ];

    private readonly Lock _gate = new();

    /// <summary>Raw endpoints keyed by WinRT device id, before dual-mode merging.</summary>
    private readonly Dictionary<string, Endpoint> _endpoints = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<DeviceWatcher> _watchers = [];
    private bool _disposed;

    /// <summary>Fired whenever the merged device list may have changed.</summary>
    public event Action? Changed;

    public void Start()
    {
        AddWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true), isLe: false);
        AddWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), isLe: true);
    }

    private void AddWatcher(string selector, bool isLe)
    {
        DeviceWatcher watcher = DeviceInformation.CreateWatcher(
            selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);

        watcher.Added += (_, info) => Upsert(info.Id, info.Name, info.Properties, isLe);
        watcher.Updated += (_, update) => Update(update.Id, update.Properties);
        watcher.Removed += (_, update) => Remove(update.Id);
        watcher.EnumerationCompleted += (_, _) => Changed?.Invoke();

        _watchers.Add(watcher);
        watcher.Start();
    }

    private void Upsert(string id, string name, IReadOnlyDictionary<string, object?> props, bool isLe)
    {
        lock (_gate)
        {
            _endpoints[id] = new Endpoint
            {
                Id = id,
                Name = name,
                IsLe = isLe,
                IsConnected = GetBool(props, PropIsConnected) ?? false,
                Address = ParseAddress(GetString(props, PropAddress)),
                ContainerId = GetString(props, PropContainerId),
                CodMajor = GetUInt(props, PropCodMajor),
                CodMinor = GetUInt(props, PropCodMinor),
            };
        }

        Changed?.Invoke();
    }

    private void Update(string id, IReadOnlyDictionary<string, object?> props)
    {
        lock (_gate)
        {
            if (!_endpoints.TryGetValue(id, out Endpoint? endpoint))
                return;

            // An update carries only the properties that actually changed.
            if (GetBool(props, PropIsConnected) is { } connected)
                endpoint.IsConnected = connected;
            if (GetString(props, PropAddress) is { } address)
                endpoint.Address = ParseAddress(address);
            if (GetString(props, PropContainerId) is { } container)
                endpoint.ContainerId = container;
        }

        Changed?.Invoke();
    }

    private void Remove(string id)
    {
        lock (_gate)
        {
            if (!_endpoints.Remove(id))
                return;
        }

        Changed?.Invoke();
    }

    /// <summary>Merged snapshot, sorted connected-first then alphabetically.</summary>
    public List<BluetoothDeviceModel> Snapshot()
    {
        List<Endpoint> endpoints;
        lock (_gate)
        {
            endpoints = [.. _endpoints.Values];
        }

        var merged = new Dictionary<string, BluetoothDeviceModel>(StringComparer.OrdinalIgnoreCase);

        foreach (Endpoint endpoint in endpoints)
        {
            string key = MergeKey(endpoint);

            if (!merged.TryGetValue(key, out BluetoothDeviceModel? device))
            {
                device = new BluetoothDeviceModel { Key = key, Name = endpoint.Name };
                merged[key] = device;
            }

            // Either transport being up means the device is connected.
            device.IsConnected |= endpoint.IsConnected;

            if (endpoint.Address != 0)
                device.Address = endpoint.Address;

            if (endpoint.IsLe)
                device.LeId = endpoint.Id;
            else
                device.ClassicId = endpoint.Id;

            // The BR/EDR endpoint carries the friendlier name and a real CoD.
            if (!endpoint.IsLe && !string.IsNullOrWhiteSpace(endpoint.Name))
                device.Name = endpoint.Name;
            else if (string.IsNullOrWhiteSpace(device.Name))
                device.Name = endpoint.Name;

            if (endpoint.CodMajor is not null)
                device.Kind = DeviceClassifier.Classify(endpoint.CodMajor, endpoint.CodMinor, device.Name);
        }

        foreach (BluetoothDeviceModel device in merged.Values)
        {
            if (string.IsNullOrWhiteSpace(device.Name))
                device.Name = "Unknown device";

            if (device.Kind == DeviceKind.Generic)
                device.Kind = DeviceClassifier.Classify(null, null, device.Name);
        }

        return [.. merged.Values
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private static string MergeKey(Endpoint endpoint)
    {
        // Normalise the container id so it matches the one the battery lookup reports.
        if (!string.IsNullOrEmpty(endpoint.ContainerId))
            return NormalizeContainerId(endpoint.ContainerId);

        return endpoint.Address != 0 ? endpoint.Address.ToString("X12") : endpoint.Id;
    }

    /// <summary>Container ids arrive in both "{b}" and "d" Guid spellings; pick one.</summary>
    public static string NormalizeContainerId(string containerId)
    {
        return Guid.TryParse(containerId, out Guid guid) ? guid.ToString("D") : containerId;
    }

    /// <summary>Parses "aa:bb:cc:dd:ee:ff" into a 48-bit address; 0 when unparseable.</summary>
    private static ulong ParseAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        Span<char> hex = stackalloc char[12];
        int written = 0;

        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
                continue;
            if (written == 12)
                return 0;
            hex[written++] = c;
        }

        return written == 12
            && ulong.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ulong address)
            ? address
            : 0;
    }

    private static bool? GetBool(IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out object? value) && value is bool b ? b : null;

    private static string? GetString(IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out object? value) ? value?.ToString() : null;

    private static uint? GetUInt(IReadOnlyDictionary<string, object?> props, string key)
        => props.TryGetValue(key, out object? value) && value is not null ? Convert.ToUInt32(value) : null;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (DeviceWatcher watcher in _watchers)
        {
            try
            {
                if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
                    watcher.Stop();
            }
            catch (Exception)
            {
                // Watcher teardown races with shutdown; nothing useful to do about it.
            }
        }

        _watchers.Clear();
    }

    private sealed class Endpoint
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required bool IsLe { get; init; }
        public bool IsConnected { get; set; }
        public ulong Address { get; set; }
        public string? ContainerId { get; set; }
        public uint? CodMajor { get; init; }
        public uint? CodMinor { get; init; }
    }
}
