using Nc2.BluetoothTaskbarApp.Bluetooth;

namespace Nc2.BluetoothTaskbarApp.ViewModels;

/// <summary>One row in the flyout: a paired device and its connect toggle.</summary>
public sealed class DeviceItemViewModel : ObservableObject
{
    private readonly Func<DeviceItemViewModel, bool, Task> _onToggleRequested;

    private bool _isOn;
    private bool _isBusy;
    private bool _isHeldByWindows;
    private string _name = string.Empty;
    private DeviceKind _kind;
    private int? _batteryPercent;
    private string? _error;

    public DeviceItemViewModel(BluetoothDeviceModel model, Func<DeviceItemViewModel, bool, Task> onToggleRequested)
    {
        _onToggleRequested = onToggleRequested;
        Key = model.Key;
        Model = model;
        Apply(model);
    }

    public string Key { get; }

    /// <summary>Latest snapshot from the watcher; the controller acts on this.</summary>
    public BluetoothDeviceModel Model { get; private set; }

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public DeviceKind Kind
    {
        get => _kind;
        private set => Set(ref _kind, value);
    }

    public int? BatteryPercent
    {
        get => _batteryPercent;
        private set
        {
            if (Set(ref _batteryPercent, value))
                RaiseRowState();
        }
    }

    /// <summary>
    /// Bound two-way to the toggle. A user flip starts the connect/disconnect;
    /// watcher updates come in through <see cref="Apply"/> instead, which does
    /// not re-trigger the operation.
    /// </summary>
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (!Set(ref _isOn, value))
                return;

            RaiseRowState();
            _ = _onToggleRequested(this, value);
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (!Set(ref _isBusy, value))
                return;

            RaiseRowState();
        }
    }

    public bool IsToggleEnabled => !IsBusy;

    /// <summary>
    /// Hidden where flipping it could not do anything: a device Windows owns can
    /// still be connected, just never disconnected, so the toggle only disappears
    /// while such a device is actually on.
    /// </summary>
    public bool IsToggleVisible => !(IsHeldByWindows && IsOn);

    public bool IsHeldByWindows
    {
        get => _isHeldByWindows;
        private set
        {
            if (Set(ref _isHeldByWindows, value))
                RaiseRowState();
        }
    }

    /// <summary>Set when the last operation failed; cleared on the next attempt.</summary>
    public string? Error
    {
        get => _error;
        set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
                RaiseRowState();
            }
        }
    }

    public bool HasError => Error is not null;

    /// <summary>Only a connected device's reading is current, so only that one is banded.</summary>
    public BatteryLevel BatteryLevel =>
        IsOn && BatteryPercent is { } percent ? BatteryLevels.FromPercent(percent) : BatteryLevel.Normal;

    public bool IsBatteryLow => BatteryLevel != BatteryLevel.Normal;

    public string StatusText
    {
        get
        {
            if (IsBusy)
                return IsOn ? "Connecting…" : "Disconnecting…";

            if (Error is { } error)
                return error;

            return IsOn ? "Connected" : "Not connected";
        }
    }

    /// <summary>The battery sits beside the status only while it describes a settled connection.</summary>
    public bool IsBatteryVisible => IsOn && !IsBusy && !HasError && BatteryPercent is not null;

    /// <summary>Pushes a fresh watcher snapshot in without firing the toggle handler.</summary>
    public void Apply(BluetoothDeviceModel model)
    {
        Model = model;
        Name = model.Name;
        Kind = model.Kind;
        BatteryPercent = model.BatteryPercent;
        IsHeldByWindows = model.IsHeldByWindows;

        // While an operation is in flight the watcher still reports the old state;
        // leave the toggle where the user put it until it settles.
        if (IsBusy || _isOn == model.IsConnected)
            return;

        _isOn = model.IsConnected;
        Raise(nameof(IsOn));
        RaiseRowState();
    }

    /// <summary>Reverts the toggle after a failed operation, without re-running it.</summary>
    public void RevertToggle(bool previous)
    {
        if (_isOn == previous)
            return;

        _isOn = previous;
        Raise(nameof(IsOn));
        RaiseRowState();
    }

    /// <summary>
    /// Re-announces everything derived from the row's state. Kept in one place so
    /// a new derived property cannot be forgotten at one of the mutation sites.
    /// </summary>
    private void RaiseRowState()
    {
        Raise(nameof(StatusText));
        Raise(nameof(BatteryLevel));
        Raise(nameof(IsBatteryLow));
        Raise(nameof(IsBatteryVisible));
        Raise(nameof(IsToggleEnabled));
        Raise(nameof(IsToggleVisible));
    }
}
