using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Nc2.BluetoothTaskbarApp.Bluetooth;

namespace Nc2.BluetoothTaskbarApp.Views;

/// <summary>Maps a device category onto the matching icon geometry from the theme.</summary>
public sealed class DeviceKindToGeometryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value is DeviceKind kind
            ? kind switch
            {
                DeviceKind.Headset => "Icon.Headset",
                DeviceKind.Speaker => "Icon.Speaker",
                DeviceKind.Keyboard => "Icon.Keyboard",
                DeviceKind.Mouse => "Icon.Mouse",
                DeviceKind.Gamepad => "Icon.Gamepad",
                DeviceKind.Phone => "Icon.Phone",
                DeviceKind.Computer => "Icon.Computer",
                DeviceKind.Wearable => "Icon.Wearable",
                DeviceKind.Printer => "Icon.Printer",
                _ => "Icon.Bluetooth",
            }
            : "Icon.Bluetooth";

        return Application.Current.TryFindResource(key) as Geometry
            ?? Application.Current.TryFindResource("Icon.Bluetooth") as Geometry
            ?? Geometry.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>True becomes Visible; pass "Invert" as the parameter to flip it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;

        if (parameter as string == "Invert")
            flag = !flag;

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Picks the horizontal battery glyph from Segoe Fluent Icons for a charge level.</summary>
public sealed class BatteryPercentToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int step = value is int percent ? (int)Math.Round(Math.Clamp(percent, 0, 100) / 10.0) : 0;

        // Battery0–Battery9 are contiguous at U+E850; the full glyph Battery10 lives elsewhere.
        return step == 10 ? "" : ((char)(0xE850 + step)).ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
