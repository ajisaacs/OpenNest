using System;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Properties;
using Velopack;
using Velopack.Sources;

namespace OpenNest
{
    /// <summary>
    /// Periodically checks a Velopack release feed for updates and downloads
    /// them in the background. Never restarts the app on its own -- an open,
    /// unsaved nest must not be discarded out from under the user -- so
    /// callers must observe <see cref="UpdateReady"/> and call
    /// <see cref="ApplyAndRestart"/> once the user agrees to restart.
    /// </summary>
    /// <remarks>
    /// The feed URL is never hard-coded: it is read from the per-machine
    /// <see cref="Settings.UpdateFeedUrl"/> setting (Options dialog), which is
    /// user-scoped and lives outside source control. A blank URL disables
    /// update checks entirely, which is the default for every install.
    /// </remarks>
    public sealed class UpdaterService : IDisposable
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);

        private readonly UpdateManager _manager;
        private Timer _timer;
        private UpdateInfo _pendingUpdate;

        /// <summary>Raised on the thread pool once an update has finished downloading.</summary>
        public event Action<string> UpdateReady;

        public UpdaterService()
            : this(Settings.Default.UpdateFeedUrl) { }

        public UpdaterService(string feedUrl)
        {
            if (string.IsNullOrWhiteSpace(feedUrl))
                return;

            _manager = new UpdateManager(new SimpleWebSource(feedUrl));
        }

        /// <summary>True once a downloaded update is waiting for <see cref="ApplyAndRestart"/>.</summary>
        public bool HasPendingUpdate => _pendingUpdate != null;

        public void Start()
        {
            if (_manager is null || !_manager.IsInstalled)
                return;

            _timer = new Timer(_ => _ = TickAsync(), null, CheckInterval, CheckInterval);
        }

        /// <summary>Applies the downloaded update and restarts the process. Call only after the user agrees.</summary>
        public void ApplyAndRestart()
        {
            if (_manager != null && _pendingUpdate != null)
                _manager.ApplyUpdatesAndRestart(_pendingUpdate);
        }

        public void Dispose() => _timer?.Dispose();

        private async Task TickAsync()
        {
            if (_manager is null || _pendingUpdate != null)
                return;

            try
            {
                var newVersion = await _manager.CheckForUpdatesAsync();
                if (newVersion == null)
                    return;

                await _manager.DownloadUpdatesAsync(newVersion);
                _pendingUpdate = newVersion;
                UpdateReady?.Invoke(newVersion.TargetFullRelease.Version.ToString());
            }
            catch
            {
                // Network hiccup or feed unreachable -- retry next tick.
            }
        }
    }
}
