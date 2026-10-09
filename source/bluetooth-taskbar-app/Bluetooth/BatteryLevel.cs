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
/// Thresholds of the battery colour profile. The colours themselves live in the
/// theme palettes (Themes/BreezeDark.xaml, Themes/BreezeLight.xaml).
/// </summary>
public static class BatteryLevels
{
    public static BatteryLevel FromPercent(int percent) => percent switch
    {
        <= 10 => BatteryLevel.Critical,
        <= 20 => BatteryLevel.VeryLow,
        <= 30 => BatteryLevel.Low,
        _ => BatteryLevel.Normal,
    };

    public static string BrushKey(BatteryLevel level) => level switch
    {
        BatteryLevel.Low => "BatteryLowBrush",
        BatteryLevel.VeryLow => "BatteryVeryLowBrush",
        BatteryLevel.Critical => "BatteryCriticalBrush",
        _ => "TextBrush",
    };
}
