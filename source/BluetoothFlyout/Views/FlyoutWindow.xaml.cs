using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using BluetoothFlyout.Interop;
using BluetoothFlyout.ViewModels;

namespace BluetoothFlyout.Views;

/// <summary>
/// The Plasma-style popup. It parks itself next to the tray, closes on focus
/// loss, and is hidden rather than destroyed so reopening stays instant.
/// </summary>
public partial class FlyoutWindow : Window
{
    /// <summary>
    /// Clicking the tray icon while the flyout is open deactivates it first. Inside
    /// this window we treat the click as "close", not "close then reopen".
    /// </summary>
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private readonly FlyoutViewModel _viewModel;
    private DateTime _hiddenAt = DateTime.MinValue;

    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>Opens the flyout, or closes it if a click just dismissed it.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            HideFlyout();
            return;
        }

        if (DateTime.UtcNow - _hiddenAt < ReopenGuard)
            return;

        ShowFlyout();
    }

    public void ShowFlyout()
    {
        _viewModel.RefreshCommand.Execute(null);

        // Lay out first so ActualHeight is real before we place the window.
        Opacity = 0;
        Show();
        UpdateLayout();
        PlaceNearTray();

        Activate();
        var helper = new WindowInteropHelper(this);
        NativeMethods.SetForegroundWindow(helper.Handle);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    public void HideFlyout()
    {
        if (!IsVisible)
            return;

        _hiddenAt = DateTime.UtcNow;

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(90));
        fade.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Hide();
            Opacity = 0;
        };

        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Anchors the flyout to the screen edge the taskbar occupies, horizontally
    /// aligned with the cursor and clamped into the work area.
    /// </summary>
    private void PlaceNearTray()
    {
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
            return;

        IntPtr monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new MONITORINFO_SIZED();
        if (!NativeMethods.GetMonitorInfo(monitor, ref info.Value))
            return;

        double scale = GetScale(monitor);

        NativeMethods.RECT work = info.Value.rcWork;
        NativeMethods.RECT screen = info.Value.rcMonitor;

        double workLeft = work.Left / scale;
        double workTop = work.Top / scale;
        double workRight = work.Right / scale;
        double workBottom = work.Bottom / scale;
        double cursorX = cursor.X / scale;

        // Horizontally centred on the click, kept inside the work area.
        double left = Math.Clamp(cursorX - (ActualWidth / 2), workLeft, workRight - ActualWidth);

        // Vertically: sit above a bottom taskbar, below a top one.
        bool taskbarOnTop = work.Top > screen.Top;
        double top = taskbarOnTop ? workTop : workBottom - ActualHeight;

        Left = Math.Round(left);
        Top = Math.Round(Math.Clamp(top, workTop, Math.Max(workTop, workBottom - ActualHeight)));
    }

    private static double GetScale(IntPtr monitor)
    {
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0
            && dpiX > 0)
        {
            return dpiX / 96.0;
        }

        return 1.0;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        HideFlyout();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideFlyout();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Only the tray icon's Exit tears the app down; everything else just hides.
        if (!Application.Current.Properties.Contains("ShuttingDown"))
        {
            e.Cancel = true;
            HideFlyout();
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>Wrapper that pre-fills cbSize, which GetMonitorInfo requires.</summary>
    private struct MONITORINFO_SIZED
    {
        public NativeMethods.MONITORINFO Value = new()
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>(),
        };

        public MONITORINFO_SIZED()
        {
        }
    }
}
