using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Nc2.BluetoothTaskbarApp.Bluetooth;
using Nc2.BluetoothTaskbarApp.Interop;

namespace Nc2.BluetoothTaskbarApp.Views;

/// <summary>One line of the low-battery popup.</summary>
public sealed record LowBatteryEntry(string Name, DeviceKind Kind, int Percent);

/// <summary>
/// Short-lived popup in the corner above the tray. It is a plain window rather
/// than a Windows notification, so it never lands in the notification centre.
/// </summary>
public partial class BatteryAlertWindow : Window
{
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(15);

    private readonly DispatcherTimer _hideTimer;

    public BatteryAlertWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer { Interval = VisibleFor };
        _hideTimer.Tick += (_, _) => HideAlert();

        MouseLeftButtonUp += (_, _) => HideAlert();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // ShowActivated alone still lets a click steal focus and keeps the window in Alt+Tab.
        IntPtr handle = new WindowInteropHelper(this).Handle;
        long style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
    }

    public void ShowAlert(IReadOnlyList<LowBatteryEntry> entries)
    {
        DeviceList.ItemsSource = entries;

        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        UpdateLayout();

        Rect work = SystemParameters.WorkArea;
        Left = Math.Round(work.Right - ActualWidth);
        Top = Math.Round(work.Bottom - ActualHeight);

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void HideAlert()
    {
        _hideTimer.Stop();

        if (!IsVisible)
            return;

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            // A new alert may have reopened the window while this one faded out.
            if (!_hideTimer.IsEnabled)
                Hide();
        };

        BeginAnimation(OpacityProperty, fade);
    }
}
