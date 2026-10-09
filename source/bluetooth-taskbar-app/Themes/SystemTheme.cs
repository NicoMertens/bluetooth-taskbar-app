using System.Windows;
using Microsoft.Win32;

namespace Nc2.BluetoothTaskbarApp.Themes;

/// <summary>
/// Follows the Windows mode (light or dark), which the taskbar, the tray glyph
/// and the shell's own flyouts all go by.
/// </summary>
internal static class SystemTheme
{
    private const string PersonalizeKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Uri DarkPalette = new("Themes/BreezeDark.xaml", UriKind.Relative);
    private static readonly Uri LightPalette = new("Themes/BreezeLight.xaml", UriKind.Relative);

    /// <summary>
    /// True when Windows runs in light mode. Note this is <c>SystemUsesLightTheme</c>,
    /// not <c>AppsUseLightTheme</c> — the two are set independently, and the
    /// taskbar follows the system one.
    /// </summary>
    public static bool IsLight()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        }
        catch (Exception)
        {
            // Key missing or unreadable — assume the far more common dark mode.
            return false;
        }
    }

    /// <summary>Swaps the palette dictionary; every DynamicResource brush repaints with it.</summary>
    public static void Apply(Application app)
    {
        Uri wanted = IsLight() ? LightPalette : DarkPalette;
        IList<ResourceDictionary> merged = app.Resources.MergedDictionaries;

        ResourceDictionary? current = merged.FirstOrDefault(d => d.Source == DarkPalette || d.Source == LightPalette);
        if (current?.Source == wanted)
            return;

        if (current is not null)
            merged.Remove(current);

        merged.Insert(0, new ResourceDictionary { Source = wanted });
    }
}
