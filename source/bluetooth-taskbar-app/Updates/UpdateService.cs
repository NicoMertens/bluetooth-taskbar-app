using System.Reflection;
using Velopack;
using Velopack.Sources;

namespace Nc2.BluetoothTaskbarApp.Updates;

/// <summary>
/// Pulls new releases from GitHub in the background and applies them on request
/// or, failing that, when the app exits.
/// </summary>
public sealed class UpdateService
{
    private readonly UpdateManager? _manager;
    private VelopackAsset? _pending;
    private bool _handedOver;

    public UpdateService()
    {
        // The SDK turns -p:RepositoryUrl from the release workflow into this attribute; local builds have none.
        string? repoUrl = Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryUrl")?.Value;

        if (string.IsNullOrWhiteSpace(repoUrl))
            return;

        var manager = new UpdateManager(new GithubSource(repoUrl, accessToken: null, prerelease: false));

        // Only an installed copy can update itself; dotnet run and _out builds cannot.
        if (manager.IsInstalled)
            _manager = manager;
    }

    /// <summary>Raised on the thread pool once an update has been downloaded.</summary>
    public event Action<string>? UpdateReady;

    public string? PendingVersion => _pending?.Version.ToString();

    public async Task CheckAsync()
    {
        if (_manager is null || _pending is not null)
            return;

        try
        {
            UpdateInfo? info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
                return;

            await _manager.DownloadUpdatesAsync(info).ConfigureAwait(false);
            _pending = info.TargetFullRelease;
            UpdateReady?.Invoke(_pending.Version.ToString());
        }
        catch (Exception)
        {
            // Offline or GitHub rate-limited: the next periodic check simply tries again.
        }
    }

    /// <summary>
    /// Hands a downloaded update to Velopack's updater, which swaps it in once this
    /// process has exited. The caller shuts down normally so the tray icon is removed;
    /// ApplyUpdatesAndRestart would kill the process and leave a ghost icon behind.
    /// </summary>
    public void ApplyOnExit(bool restart)
    {
        if (_manager is null || _pending is null || _handedOver)
            return;

        _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: restart);
        _handedOver = true;
    }
}
