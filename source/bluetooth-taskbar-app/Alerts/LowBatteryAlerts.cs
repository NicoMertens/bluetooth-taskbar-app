using System.IO;
using System.Media;
using System.Windows.Threading;
using Nc2.BluetoothTaskbarApp.ViewModels;
using Nc2.BluetoothTaskbarApp.Views;

namespace Nc2.BluetoothTaskbarApp.Alerts;

/// <summary>
/// Pops up a warning with a sound while any connected device runs low, the first
/// time as soon as it is noticed and then again every few minutes.
/// </summary>
public sealed class LowBatteryAlerts : IDisposable
{
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(3);

    /// <summary>Only reads the view model, so checking often costs nothing and catches a drop early.</summary>
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    private static readonly string LowBatterySound = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Windows Battery Low.wav");

    private readonly FlyoutViewModel _viewModel;
    private readonly DispatcherTimer _checkTimer;
    private BatteryAlertWindow? _window;
    private SoundPlayer? _sound;
    private DateTime? _lastAlert;

    public LowBatteryAlerts(FlyoutViewModel viewModel, Dispatcher dispatcher)
    {
        _viewModel = viewModel;

        _checkTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = CheckInterval };
        _checkTimer.Tick += (_, _) => Check();
    }

    public void Start() => _checkTimer.Start();

    private void Check()
    {
        List<LowBatteryEntry> low =
        [
            .. _viewModel.Devices
                .Where(d => d.IsBatteryLow && !d.IsBusy)
                .Select(d => new LowBatteryEntry(d.Name, d.Kind, d.BatteryPercent!.Value)),
        ];

        if (low.Count == 0)
            return;

        DateTime now = DateTime.UtcNow;
        if (_lastAlert is { } last && now - last < RepeatInterval)
            return;

        _lastAlert = now;

        _window ??= new BatteryAlertWindow();
        _window.ShowAlert(low);
        PlaySound();
    }

    private void PlaySound()
    {
        try
        {
            if (File.Exists(LowBatterySound))
            {
                _sound ??= new SoundPlayer(LowBatterySound);
                _sound.Play();
                return;
            }
        }
        catch (Exception)
        {
            // Unreadable or broken wav — fall back to the system sound below.
        }

        SystemSounds.Exclamation.Play();
    }

    public void Dispose()
    {
        _checkTimer.Stop();
        _sound?.Dispose();
        _window?.Close();
    }
}
