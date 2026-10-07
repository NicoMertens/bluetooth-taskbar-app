using Microsoft.Win32;

namespace Nc2.BluetoothTaskbarApp.Tray;

/// <summary>
/// Whether the taskbar is light or dark. The tray glyph has to invert with it,
/// otherwise a light glyph disappears on a light taskbar.
/// </summary>
internal static class SystemTheme
{
    private const string PersonalizeKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>
    /// True when Windows paints the taskbar light. Note this is
    /// <c>SystemUsesLightTheme</c>, not <c>AppsUseLightTheme</c> — the two are set
    /// independently, and the taskbar follows the system one.
    /// </summary>
    public static bool IsLightTaskbar()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            // Key missing or unreadable — assume the far more common dark taskbar.
            return false;
        }
    }
}
