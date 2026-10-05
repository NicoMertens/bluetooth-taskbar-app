using System.Globalization;
using System.IO;

namespace BluetoothFlyout.Bluetooth;

/// <summary>
/// Remembers which Bluetooth services a device had installed before we disconnected it.
/// <para>
/// Disconnecting a BR/EDR device means disabling its services, which also removes
/// them from the enumeration — so without this note we would not know what to
/// re-enable on the next connect. Stored as one line per device:
/// <c>ADDRESS=guid,guid,...</c>
/// </para>
/// </summary>
public sealed class ServiceCache
{
    private readonly string _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<ulong, Guid[]> _entries = [];

    public ServiceCache()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BluetoothFlyout");

        _path = Path.Combine(dir, "services.txt");
        Load();
    }

    public Guid[]? Get(ulong address)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(address, out Guid[]? services) ? services : null;
        }
    }

    public void Set(ulong address, Guid[] services)
    {
        if (services.Length == 0)
            return;

        lock (_gate)
        {
            _entries[address] = services;
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
                return;

            foreach (string line in File.ReadAllLines(_path))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                    continue;

                if (!ulong.TryParse(line.AsSpan(0, separator), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out ulong address))
                    continue;

                Guid[] services = [.. line[(separator + 1)..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(part => Guid.TryParse(part, out Guid guid) ? guid : Guid.Empty)
                    .Where(guid => guid != Guid.Empty)];

                if (services.Length > 0)
                    _entries[address] = services;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A missing or unreadable cache only costs us the fallback service list.
        }
    }

    /// <summary>Caller must hold <see cref="_gate"/>.</summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            IEnumerable<string> lines = _entries.Select(entry =>
                $"{entry.Key:X12}={string.Join(',', entry.Value)}");

            File.WriteAllLines(_path, lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Persisting is best-effort; the in-memory copy still serves this session.
        }
    }
}
