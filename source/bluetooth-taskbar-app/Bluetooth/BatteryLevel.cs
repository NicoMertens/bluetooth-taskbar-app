using System.Windows.Media;

namespace Nc2.BluetoothTaskbarApp.Bluetooth;

/// <summary>Charge bands that share one colour wherever a battery reading is shown.</summary>
public enum BatteryLevel
{
    Normal,
    Low,
    VeryLow,
    Critical,
}

/// <summary>
/// The battery colour profile: thresholds and colours in one place, used by the
/// flyout list, the low-battery popup and the tray icon alike.
/// </summary>
public static class BatteryPalette
{
    public static BatteryLevel LevelOf(int percent) => percent switch
    {
        <= 10 => BatteryLevel.Critical,
        <= 20 => BatteryLevel.VeryLow,
        <= 30 => BatteryLevel.Low,
        _ => BatteryLevel.Normal,
    };

    /// <summary>
    /// Colour for a band, or null for <see cref="BatteryLevel.Normal"/>, which keeps
    /// the surface's own text colour (white in the flyout). A light background gets
    /// darker shades, because the plain yellow vanishes on a light taskbar.
    /// </summary>
    public static Color? ColorOf(BatteryLevel level, bool lightBackground = false) => level switch
    {
        BatteryLevel.Low => lightBackground ? Color.FromRgb(0xB0, 0x86, 0x00) : Color.FromRgb(0xFD, 0xD8, 0x35),
        BatteryLevel.VeryLow => lightBackground ? Color.FromRgb(0xC8, 0x5A, 0x00) : Color.FromRgb(0xF6, 0x74, 0x00),
        BatteryLevel.Critical => lightBackground ? Color.FromRgb(0xC0, 0x22, 0x32) : Color.FromRgb(0xDA, 0x44, 0x53),
        _ => null,
    };
}
