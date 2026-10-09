using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Nc2.BluetoothTaskbarApp.Bluetooth;

namespace Nc2.BluetoothTaskbarApp.ViewModels;

/// <summary>Backs the flyout: the device list plus its refresh/settings actions.</summary>
public sealed class FlyoutViewModel : ObservableObject, IDisposable
{
    /// <summary>The watcher fires in bursts; coalesce them into one rebuild.</summary>
    private static readonly TimeSpan RebuildDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a failure message stays on a row.</summary>
    private static readonly TimeSpan ErrorLinger = TimeSpan.FromSeconds(5);

    private readonly BluetoothMonitor _monitor;
    private readonly RadioMonitor _radio;
    private readonly ConnectionController _controller;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _rebuildTimer;

    private readonly CancellationTokenSource _detailsCts = new();
    private bool _isRefreshingDetails;
    private bool _refreshRequestedAgain;
    private bool _disposed;

    public FlyoutViewModel(
        BluetoothMonitor monitor,
        RadioMonitor radio,
        ConnectionController controller,
        Dispatcher dispatcher)
    {
        _monitor = monitor;
        _radio = radio;
        _controller = controller;
        _dispatcher = dispatcher;

        _rebuildTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = RebuildDelay };
        _rebuildTimer.Tick += (_, _) =>
        {
            _rebuildTimer.Stop();
            Rebuild();
        };

        RefreshCommand = new RelayCommand(() =>
        {
            Rebuild();
            _ = RefreshDetailsAsync();
        });

        OpenSettingsCommand = new RelayCommand(OpenBluetoothSettings);

        _monitor.Changed += OnMonitorChanged;
        _radio.Changed += OnMonitorChanged;
    }

    public ObservableCollection<DeviceItemViewModel> Devices { get; } = [];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public bool IsRadioOn => _radio.IsOn;

    /// <summary>The list is only shown when the radio is on and has something to list.</summary>
    public bool ShowDeviceList => IsRadioOn && Devices.Count > 0;

    public bool ShowMessage => !ShowDeviceList;

    public string VersionText { get; } = $"v{typeof(FlyoutViewModel).Assembly.GetName().Version?.ToString(3)}";

    public string MessageText => IsRadioOn ? "No paired devices" : "Bluetooth is off";

    /// <summary>True once at least one device is connected — drives the tray icon.</summary>
    public bool AnyConnected => Devices.Any(d => d.IsOn);

    public event Action? DeviceStateChanged;

    private void OnMonitorChanged()
    {
        if (_disposed)
            return;

        // Hop to the UI thread and let the timer coalesce the burst.
        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
                _rebuildTimer.Start();
        }, DispatcherPriority.Background);
    }

    /// <summary>Rebuilds the list in place so rows keep their busy/error state.</summary>
    private void Rebuild()
    {
        // With the radio off the watcher still lists every paired device, and their
        // IsConnected value is whatever it was before — so the list would claim
        // everything is connected. Show nothing instead.
        List<BluetoothDeviceModel> snapshot = _radio.IsOn ? _monitor.Snapshot() : [];

        AvailabilityProbe.Populate(snapshot);

        // Pairings whose adapter is gone cannot be connected and are usually
        // leftovers from hardware that no longer exists — leave them to Settings.
        snapshot.RemoveAll(device => !device.IsReachable);

        var existing = Devices.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

        // The snapshot is freshly built from the watcher and therefore knows nothing
        // the probes resolved. Carry that detail over, or every rebuild would blank
        // out the battery level and the held-by-Windows state until the next probe.
        foreach (BluetoothDeviceModel model in snapshot)
        {
            if (!existing.TryGetValue(model.Key, out DeviceItemViewModel? known))
                continue;

            model.BatteryPercent ??= known.BatteryPercent;
            model.IsHeldByWindows = known.IsHeldByWindows;
        }

        bool addedDevice = false;

        for (int index = 0; index < snapshot.Count; index++)
        {
            BluetoothDeviceModel model = snapshot[index];

            if (existing.TryGetValue(model.Key, out DeviceItemViewModel? item))
            {
                item.Apply(model);

                int current = Devices.IndexOf(item);
                if (current != index)
                    Devices.Move(current, index);
            }
            else
            {
                Devices.Insert(index, new DeviceItemViewModel(model, OnToggleRequestedAsync));
                addedDevice = true;
            }
        }

        // Anything past the snapshot length is gone (unpaired or radio off).
        while (Devices.Count > snapshot.Count)
            Devices.RemoveAt(Devices.Count - 1);

        RaiseListState();
        DeviceStateChanged?.Invoke();

        // A device the watcher just reported has no probed detail yet. Without this
        // the first rows would sit there with no battery level and no held state
        // until the periodic refresh came round, which takes a minute and a half.
        if (addedDevice)
            _ = RefreshDetailsAsync();
    }

    /// <summary>
    /// Re-reads battery level and HID ownership. Safe to call often: a call that
    /// arrives while one is running is not dropped but replayed at the end, so the
    /// list cannot be left showing stale detail.
    /// </summary>
    public async Task RefreshDetailsAsync()
    {
        if (_disposed)
            return;

        if (_isRefreshingDetails)
        {
            _refreshRequestedAgain = true;
            return;
        }

        _isRefreshingDetails = true;
        try
        {
            do
            {
                _refreshRequestedAgain = false;

                // Picks up an adapter that was swapped, plugged in or disabled.
                await _radio.RefreshAsync().ConfigureAwait(true);

                List<BluetoothDeviceModel> models = [.. Devices.Select(d => d.Model)];
                if (models.Count == 0)
                    return;

                await DeviceDetailsProbe.PopulateAsync(models, _detailsCts.Token).ConfigureAwait(true);

                if (_disposed)
                    return;

                foreach (DeviceItemViewModel item in Devices)
                    item.Apply(item.Model);

                DeviceStateChanged?.Invoke();
            }
            while (_refreshRequestedAgain);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            _isRefreshingDetails = false;
        }
    }

    private async Task OnToggleRequestedAsync(DeviceItemViewModel item, bool shouldConnect)
    {
        item.Error = null;
        item.IsBusy = true;

        try
        {
            OperationResult result = await _controller
                .SetConnectedAsync(item.Model, shouldConnect)
                .ConfigureAwait(true);

            if (result.Success)
            {
                // The watcher confirms the real state; this is just so the row
                // does not snap back while the link is still coming up.
                item.Model.IsConnected = shouldConnect;
            }
            else
            {
                item.RevertToggle(!shouldConnect);
                ShowTransientError(item, Shorten(result.Message));
            }
        }
        catch (Exception e)
        {
            item.RevertToggle(!shouldConnect);
            ShowTransientError(item, Shorten(e.Message));
        }
        finally
        {
            item.IsBusy = false;
            Raise(nameof(AnyConnected));
            DeviceStateChanged?.Invoke();
        }

        // Battery becomes readable once a device is actually up.
        if (shouldConnect)
            await RefreshDetailsAsync().ConfigureAwait(true);
    }

    private void RaiseListState()
    {
        Raise(nameof(IsRadioOn));
        Raise(nameof(ShowDeviceList));
        Raise(nameof(ShowMessage));
        Raise(nameof(MessageText));
        Raise(nameof(AnyConnected));
    }

    private void ShowTransientError(DeviceItemViewModel item, string message)
    {
        item.Error = message;

        _ = Task.Delay(ErrorLinger).ContinueWith(_ =>
        {
            // Only clear it if nothing newer has replaced it in the meantime.
            if (!_disposed && item.Error == message)
                item.Error = null;
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static string Shorten(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Failed";

        string trimmed = message.Trim().TrimEnd('.');
        return trimmed.Length <= 48 ? trimmed : trimmed[..47] + "…";
    }

    private static void OpenBluetoothSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Settings URI handler missing or blocked by policy — nothing to fall back to.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _monitor.Changed -= OnMonitorChanged;
        _radio.Changed -= OnMonitorChanged;
        _rebuildTimer.Stop();
        _detailsCts.Cancel();
        _detailsCts.Dispose();
    }
}
