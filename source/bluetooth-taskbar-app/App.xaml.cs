using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nc2.BluetoothTaskbarApp.Alerts;
using Nc2.BluetoothTaskbarApp.Bluetooth;
using Nc2.BluetoothTaskbarApp.Interop;
using Nc2.BluetoothTaskbarApp.Tray;
using Nc2.BluetoothTaskbarApp.Updates;
using Nc2.BluetoothTaskbarApp.ViewModels;
using Nc2.BluetoothTaskbarApp.Views;

namespace Nc2.BluetoothTaskbarApp;

public partial class App : Application
{
    /// <summary>Battery levels change slowly; polling harder would wake the radio for nothing.</summary>
    private static readonly TimeSpan DetailsPollInterval = TimeSpan.FromSeconds(90);

    /// <summary>A tray app can run for weeks, so a check at startup alone would miss releases.</summary>
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private Mutex? _singleInstance;
    private BluetoothMonitor? _monitor;
    private RadioMonitor? _radio;
    private ConnectionController? _controller;
    private FlyoutViewModel? _viewModel;
    private FlyoutWindow? _window;
    private TrayIcon? _tray;
    private ContextMenu? _trayMenu;
    private DispatcherTimer? _detailsTimer;
    private UpdateService? _updates;
    private DispatcherTimer? _updateTimer;
    private MenuItem? _updateMenuItem;
    private LowBatteryAlerts? _batteryAlerts;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(initiallyOwned: true, @"Local\bluetooth-taskbar-app.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Another copy already owns the tray icon.
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        _monitor = new BluetoothMonitor();
        _radio = new RadioMonitor();
        _controller = new ConnectionController();
        _viewModel = new FlyoutViewModel(_monitor, _radio, _controller, Dispatcher);
        _window = new FlyoutWindow(_viewModel);

        _tray = new TrayIcon();
        _tray.Activated += () => _window.Toggle();
        _tray.ContextMenuRequested += ShowTrayMenu;

        _viewModel.DeviceStateChanged += UpdateTray;

        _monitor.Start();

        _detailsTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = DetailsPollInterval,
        };
        _detailsTimer.Tick += (_, _) => _ = _viewModel.RefreshDetailsAsync();
        _detailsTimer.Start();

        _ = _viewModel.RefreshDetailsAsync();

        _batteryAlerts = new LowBatteryAlerts(_viewModel, Dispatcher);
        _batteryAlerts.Start();

        _updates = new UpdateService();
        _updates.UpdateReady += _ => Dispatcher.InvokeAsync(ShowUpdateMenuItem);
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = UpdateCheckInterval,
        };
        _updateTimer.Tick += (_, _) => _ = _updates.CheckAsync();
        _updateTimer.Start();
        _ = _updates.CheckAsync();

        // --show opens the flyout straight away, so it can be seen without
        // clicking the tray icon. It still closes as soon as focus moves away.
        if (e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase))
            Dispatcher.InvokeAsync(() => _window.ShowFlyout(), DispatcherPriority.ApplicationIdle);
    }

    private void UpdateTray()
    {
        if (_tray is null || _viewModel is null)
            return;

        if (!_viewModel.IsRadioOn)
        {
            _tray.Update(0, BatteryLevel.Normal, "Bluetooth is off");
            return;
        }

        int connected = _viewModel.Devices.Count(d => d.IsOn);
        BatteryLevel weakest = _viewModel.Devices.Select(d => d.BatteryLevel).DefaultIfEmpty().Max();

        _tray.Update(connected, weakest, connected switch
        {
            0 => "Bluetooth – no devices connected",
            1 => "Bluetooth – 1 device connected",
            _ => $"Bluetooth – {connected} devices connected",
        });
    }

    private void ShowTrayMenu(NativeMethods.POINT _)
    {
        if (_viewModel is null || _tray is null)
            return;

        _window?.HideFlyout();

        _trayMenu ??= BuildTrayMenu(_viewModel);

        // Without foreground ownership the menu would not dismiss on an outside click.
        NativeMethods.SetForegroundWindow(_tray.Handle);

        _trayMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _trayMenu.IsOpen = true;
    }

    private ContextMenu BuildTrayMenu(FlyoutViewModel viewModel)
    {
        var menu = new ContextMenu { StaysOpen = false };

        var refresh = new MenuItem { Header = "Refresh" };
        refresh.Click += (_, _) => viewModel.RefreshCommand.Execute(null);

        var settings = new MenuItem { Header = "Bluetooth settings…" };
        settings.Click += (_, _) => viewModel.OpenSettingsCommand.Execute(null);

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => RequestShutdown();

        _updateMenuItem = new MenuItem { Visibility = Visibility.Collapsed };
        _updateMenuItem.Click += (_, _) =>
        {
            _updates?.ApplyOnExit(restart: true);
            RequestShutdown();
        };
        ShowUpdateMenuItem();

        menu.Items.Add(_updateMenuItem);
        menu.Items.Add(refresh);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);

        return menu;
    }

    private void ShowUpdateMenuItem()
    {
        // The menu is built lazily; if it does not exist yet, building it picks the version up.
        if (_updateMenuItem is null || _updates?.PendingVersion is not { } version)
            return;

        _updateMenuItem.Header = $"Restart to update to {version}";
        _updateMenuItem.Visibility = Visibility.Visible;
    }

    private void RequestShutdown()
    {
        // The flyout refuses to close unless this flag is set.
        Properties["ShuttingDown"] = true;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _detailsTimer?.Stop();
        _updateTimer?.Stop();
        _batteryAlerts?.Dispose();
        _updates?.ApplyOnExit(restart: false);
        _tray?.Dispose();
        _viewModel?.Dispose();
        _monitor?.Dispose();
        _radio?.Dispose();
        _controller?.Dispose();

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();

        base.OnExit(e);
    }
}
