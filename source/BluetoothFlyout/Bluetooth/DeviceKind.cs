namespace BluetoothFlyout.Bluetooth;

/// <summary>Icon bucket for a device, derived from its Bluetooth Class of Device.</summary>
public enum DeviceKind
{
    Generic,
    Headset,
    Speaker,
    Keyboard,
    Mouse,
    Gamepad,
    Phone,
    Computer,
    Wearable,
    Printer,
}

public static class DeviceClassifier
{
    // Class of Device, major device class (bits 8-12 of the CoD).
    private const uint MajorComputer = 1;
    private const uint MajorPhone = 2;
    private const uint MajorAudioVideo = 4;
    private const uint MajorPeripheral = 5;
    private const uint MajorImaging = 6;
    private const uint MajorWearable = 7;

    /// <summary>
    /// Maps the major/minor device class onto an icon bucket, falling back to a
    /// name heuristic for devices that report a useless CoD (plenty of BLE ones do).
    /// </summary>
    public static DeviceKind Classify(uint? major, uint? minor, string name)
    {
        DeviceKind fromCod = major switch
        {
            MajorComputer => DeviceKind.Computer,
            MajorPhone => DeviceKind.Phone,
            MajorAudioVideo => ClassifyAudio(minor),
            MajorPeripheral => ClassifyPeripheral(minor),
            MajorImaging => DeviceKind.Printer,
            MajorWearable => DeviceKind.Wearable,
            _ => DeviceKind.Generic,
        };

        return fromCod != DeviceKind.Generic ? fromCod : GuessFromName(name);
    }

    private static DeviceKind ClassifyAudio(uint? minor) => minor switch
    {
        1 or 2 or 6 => DeviceKind.Headset,        // headset, hands-free, headphones
        5 or 7 or 8 or 0x0A => DeviceKind.Speaker, // loudspeaker, portable/car audio, hi-fi
        _ => DeviceKind.Headset,
    };

    private static DeviceKind ClassifyPeripheral(uint? minor)
    {
        if (minor is null)
            return DeviceKind.Generic;

        // Bits 6-7 of the CoD flag keyboard / pointing device; in the 6-bit minor
        // value handed to us those land at 0x10 and 0x20.
        uint flags = minor.Value & 0x30;
        if (flags == 0x10)
            return DeviceKind.Keyboard;
        if (flags is 0x20 or 0x30)
            return DeviceKind.Mouse;

        // Remaining low bits describe the device type; 2 = joystick, 3 = gamepad.
        return (minor.Value & 0x0F) switch
        {
            2 or 3 => DeviceKind.Gamepad,
            _ => DeviceKind.Generic,
        };
    }

    private static DeviceKind GuessFromName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return DeviceKind.Generic;

        string n = name.ToLowerInvariant();

        if (Contains(n, "headphone", "headset", "buds", "airpod", "wh-", "wf-", "earphone", "momentum"))
            return DeviceKind.Headset;
        if (Contains(n, "speaker", "soundbar", "boom", "flip", "charge", "soundcore"))
            return DeviceKind.Speaker;
        if (Contains(n, "keyboard", "keys", "keychron"))
            return DeviceKind.Keyboard;
        if (Contains(n, "mouse", "mx master", "mx anywhere", "trackpad", "magic trackpad"))
            return DeviceKind.Mouse;
        if (Contains(n, "controller", "gamepad", "dualsense", "dualshock", "xbox", "joy-con"))
            return DeviceKind.Gamepad;
        if (Contains(n, "watch", "band", "fit"))
            return DeviceKind.Wearable;
        if (Contains(n, "phone", "iphone", "pixel", "galaxy"))
            return DeviceKind.Phone;

        return DeviceKind.Generic;
    }

    private static bool Contains(string haystack, params string[] needles)
    {
        foreach (string needle in needles)
        {
            if (haystack.Contains(needle, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
