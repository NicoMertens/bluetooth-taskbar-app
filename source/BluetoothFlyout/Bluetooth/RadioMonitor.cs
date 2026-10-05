using Windows.Devices.Radios;

namespace BluetoothFlyout.Bluetooth;

/// <summary>
/// Tracks whether the Bluetooth radio is switched on.
/// <para>
/// This cannot be inferred from the device list: with the radio off the watcher
/// still reports every paired device, and their <c>IsConnected</c> property keeps
/// the value it had before, so everything would look connected. The radio state
/// has to be read separately.
/// </para>
/// </summary>
public sealed class RadioMonitor : IDisposable
{
    private readonly Lock _gate = new();

    private Radio? _radio;
    private bool _disposed;

    /// <summary>
    /// Whether Bluetooth is on. Starts out true so the list is not hidden during
    /// the moment before the first read completes.
    /// </summary>
    public bool IsOn { get; private set; } = true;

    /// <summary>Raised off the UI thread when the radio is switched on or off.</summary>
    public event Action? Changed;

    /// <summary>
    /// Finds the Bluetooth radio and subscribes to its state. Also re-run on every
    /// refresh, which picks up an adapter that was swapped or plugged in later.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (_disposed)
            return;

        Radio? found = null;

        try
        {
            // Access is only gated for *changing* a radio; reading works regardless,
            // but asking first keeps us on the documented path.
            await Radio.RequestAccessAsync().AsTask().ConfigureAwait(false);

            IReadOnlyList<Radio> radios = await Radio.GetRadiosAsync().AsTask().ConfigureAwait(false);
            found = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        }
        catch (Exception)
        {
            // No radio service, or access denied. Treated as "no adapter" below.
        }

        bool previous;
        bool current;

        lock (_gate)
        {
            if (_disposed)
                return;

            if (!ReferenceEquals(found, _radio))
            {
                if (_radio is not null)
                    _radio.StateChanged -= OnStateChanged;

                _radio = found;

                if (_radio is not null)
                    _radio.StateChanged += OnStateChanged;
            }

            previous = IsOn;

            // A missing radio counts as off: there is nothing to connect through.
            current = found?.State == RadioState.On;
            IsOn = current;
        }

        if (previous != current)
            Changed?.Invoke();
    }

    private void OnStateChanged(Radio sender, object args)
    {
        bool previous;
        bool current;

        lock (_gate)
        {
            if (_disposed)
                return;

            previous = IsOn;
            current = sender.State == RadioState.On;
            IsOn = current;
        }

        if (previous != current)
            Changed?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;

            if (_radio is not null)
            {
                _radio.StateChanged -= OnStateChanged;
                _radio = null;
            }
        }
    }
}
