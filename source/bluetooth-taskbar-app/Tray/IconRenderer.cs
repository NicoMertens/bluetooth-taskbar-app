using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nc2.BluetoothTaskbarApp.Bluetooth;
using Nc2.BluetoothTaskbarApp.Interop;

namespace Nc2.BluetoothTaskbarApp.Tray;

/// <summary>
/// Renders the tray glyph with WPF and converts it into an HICON, so the app
/// ships no .ico variants and follows the current DPI and connection state.
/// </summary>
internal static class IconRenderer
{
    private const string BluetoothPath =
        "M17.71 7.71L12 2h-1v7.59L6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 11 14.41V22h1l5.71-5.71-4.3-4.29 4.3-4.29z"
        + "M13 5.83l1.88 1.88L13 9.59V5.83zm1.88 10.46L13 18.17v-3.76l1.88 1.88z";

    /// <summary>Tray icons are 16px at 100% scaling; supersample so the badge digit stays legible.</summary>
    private const int Supersample = 4;

    /// <summary>Glyph colours for a dark and a light taskbar respectively.</summary>
    private static readonly Color GlyphOnDark = Color.FromRgb(0xE2, 0xE5, 0xE8);
    private static readonly Color GlyphOnLight = Color.FromRgb(0x1F, 0x1F, 0x1F);

    /// <summary>Count colours — Breeze accent on dark, darkened so it still reads on light.</summary>
    private static readonly Color CountOnDark = Color.FromRgb(0x3D, 0xAE, 0xE9);
    private static readonly Color CountOnLight = Color.FromRgb(0x1A, 0x72, 0xA8);

    private static readonly Geometry Glyph = BuildGlyph();

    private static readonly Typeface BadgeTypeface = new(
        new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static Geometry BuildGlyph()
    {
        Geometry geometry = Geometry.Parse(BluetoothPath);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Draws the Bluetooth glyph, tinted by the weakest connected battery, plus a
    /// badge with the number of connected devices when there is at least one.
    /// Caller owns the handle (DestroyIcon).
    /// </summary>
    public static IntPtr CreateTrayIcon(int connectedCount, BatteryLevel battery)
    {
        int size = Math.Max(16, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON));
        return CreateHIcon(RenderBitmap(size, connectedCount, battery, SystemTheme.IsLightTaskbar()), size);
    }

    /// <summary>The drawing half, kept separate from the HICON plumbing so it can be inspected.</summary>
    internal static BitmapSource RenderBitmap(int size, int connectedCount, BatteryLevel battery, bool lightTaskbar)
    {
        int large = size * Supersample;

        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
            Draw(dc, large, connectedCount, battery, lightTaskbar);

        var hiRes = new RenderTargetBitmap(large, large, 96, 96, PixelFormats.Pbgra32);
        hiRes.Render(visual);
        hiRes.Freeze();

        return Downsample(hiRes, size);
    }

    private static void Draw(DrawingContext dc, double size, int connectedCount, BatteryLevel battery, bool lightTaskbar)
    {
        Color glyphColor = BatteryPalette.ColorOf(battery, lightTaskbar) ?? (lightTaskbar ? GlyphOnLight : GlyphOnDark);
        var glyphBrush = new SolidColorBrush(glyphColor);
        Rect glyphInk = Glyph.Bounds;

        if (connectedCount <= 0)
        {
            double height = size * 0.92;
            double width = height * (glyphInk.Width / glyphInk.Height);
            dc.DrawGeometry(glyphBrush, null,
                Fit(Glyph, glyphInk, (size - width) / 2, (size - height) / 2, height));
            return;
        }

        string label = connectedCount > 9 ? "9+" : connectedCount.ToString(CultureInfo.InvariantCulture);

        // Measure the digits at an arbitrary reference size; only the shape matters,
        // the real scale comes out of the layout below.
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            BadgeTypeface,
            100,
            Brushes.Black,
            pixelsPerDip: 1.0);

        Geometry digits = text.BuildGeometry(new Point(0, 0));
        Rect digitInk = digits.Bounds;

        // Glyph and digits share one height, side by side. Ink bounds (not font
        // metrics) are matched, so the digits are literally as tall as the glyph.
        double gap = size * 0.07;
        double glyphAspect = glyphInk.Width / glyphInk.Height;
        double digitAspect = digitInk.Width / digitInk.Height;

        // Take the largest height that still fits the icon's width.
        double common = Math.Min(size * 0.90, (size - gap) / (glyphAspect + digitAspect));

        double glyphWidth = glyphAspect * common;
        double digitWidth = digitAspect * common;
        double left = (size - (glyphWidth + gap + digitWidth)) / 2;
        double top = (size - common) / 2;

        dc.DrawGeometry(glyphBrush, null, Fit(Glyph, glyphInk, left, top, common));

        var digitBrush = new SolidColorBrush(lightTaskbar ? CountOnLight : CountOnDark);
        dc.DrawGeometry(digitBrush, null,
            Fit(digits, digitInk, left + glyphWidth + gap, top, common));
    }

    /// <summary>
    /// Returns <paramref name="source"/> scaled so its ink bounds are
    /// <paramref name="height"/> tall, with its top-left corner at (x, y).
    /// </summary>
    private static Geometry Fit(Geometry source, Rect ink, double x, double y, double height)
    {
        double scale = height / ink.Height;

        var group = new TransformGroup();
        group.Children.Add(new ScaleTransform(scale, scale));
        group.Children.Add(new TranslateTransform(x - (ink.X * scale), y - (ink.Y * scale)));

        Geometry clone = source.Clone();
        clone.Transform = group;
        return clone;
    }

    private static BitmapSource Downsample(BitmapSource source, int size)
    {
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

        using (DrawingContext dc = visual.RenderOpen())
            dc.DrawImage(source, new Rect(0, 0, size, size));

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();

        return target;
    }

    private static IntPtr CreateHIcon(BitmapSource source, int size)
    {
        int stride = size * 4;
        var pixels = new byte[stride * size];
        source.CopyPixels(pixels, stride, 0);

        var info = new NativeMethods.BITMAPINFO
        {
            bmiHeader = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size, // negative: top-down, matching WPF's row order
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            },
        };

        IntPtr colorBitmap = NativeMethods.CreateDIBSection(
            IntPtr.Zero, ref info, NativeMethods.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);

        if (colorBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            return IntPtr.Zero;

        // The 1bpp mask is ignored for 32bpp icons but must still be supplied.
        IntPtr maskBitmap = NativeMethods.CreateBitmap(size, size, 1, 1, IntPtr.Zero);

        try
        {
            Marshal.Copy(pixels, 0, bits, pixels.Length);

            var iconInfo = new NativeMethods.ICONINFO
            {
                fIcon = true,
                hbmColor = colorBitmap,
                hbmMask = maskBitmap,
            };

            return NativeMethods.CreateIconIndirect(ref iconInfo);
        }
        finally
        {
            // CreateIconIndirect copies the bitmaps, so we own these either way.
            if (colorBitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(colorBitmap);
            if (maskBitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(maskBitmap);
        }
    }
}
