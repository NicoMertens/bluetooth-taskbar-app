namespace Nc2.BluetoothTaskbarApp.Bluetooth;

/// <summary>
/// A paired device as the watcher sees it. Dual-mode devices surface twice in
/// device enumeration (once BR/EDR, once LE); <see cref="Key"/> collapses them.
/// </summary>
public sealed class BluetoothDeviceModel
{
    /// <summary>Stable merge key: container id when available, else the BT address.</summary>
    public required string Key { get; init; }

    public required string Name { get; set; }

    /// <summary>48-bit BR/EDR address, 0 when only the LE endpoint is known.</summary>
    public ulong Address { get; set; }

    public bool IsConnected { get; set; }

    public DeviceKind Kind { get; set; } = DeviceKind.Generic;

    public int? BatteryPercent { get; set; }

    /// <summary>WinRT device id of the BR/EDR association endpoint, if paired over classic.</summary>
    public string? ClassicId { get; set; }

    /// <summary>WinRT device id of the LE association endpoint, if paired over LE.</summary>
    public string? LeId { get; set; }

    /// <summary>False when the pairing belongs to an adapter this machine no longer has.</summary>
    public bool IsReachable { get; set; } = true;

    /// <summary>
    /// True for LE devices whose link is owned by Windows' HID-over-GATT driver.
    /// Those cannot be disconnected by an app — connecting still works.
    /// </summary>
    public bool IsHeldByWindows { get; set; }

    public bool SupportsClassic => ClassicId is not null && Address != 0;

    public bool SupportsLe => LeId is not null;
}
