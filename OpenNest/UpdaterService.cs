using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Properties;
using Velopack;
using Velopack.Locators;
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
    /// A blank <see cref="Settings.UpdateFeedUrl"/> (the default) checks the
    /// public GitHub Releases for full versions, plus the rolling development
    /// pre-release when <see cref="Settings.UpdateIncludeDevBuilds"/> is set. A URL set in
    /// the Options dialog points this machine at a self-hosted feed instead;
    /// that URL is operator infrastructure and never lives in source control.
    /// </remarks>
    public sealed class UpdaterService : IDisposable
    {
        // Short first delay: most sessions are far shorter than the interval and
        // would otherwise never check; the delay keeps the check off startup.
        private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
        private const string GitHubRepositoryUrl = "https://github.com/ajisaacs/OpenNest";

        private readonly UpdateManager _manager;
        private readonly SemaphoreSlim _checkLock = new(1, 1);
        private Timer _timer;
        private UpdateInfo _pendingUpdate;

        /// <summary>The outcome of <see cref="CheckNowAsync"/>.</summary>
        public enum CheckResult
        {
            /// <summary>Not a Velopack installation, so this copy cannot update itself.</summary>
            NotInstalled,
            UpToDate,
            /// <summary>A newer version is downloaded (now or earlier) and waits for <see cref="ApplyAndRestart"/>.</summary>
            UpdateReady,
            Failed,
        }

        /// <summary>Raised on the thread pool once an update has finished downloading.</summary>
        public event Action<string> UpdateReady;

        public UpdaterService()
            : this(Settings.Default.UpdateFeedUrl, Settings.Default.UpdateIncludeDevBuilds) { }

        public UpdaterService(string feedUrl, bool includeDevBuilds)
        {
            // Only a process started through VelopackApp (MainApp) has a locator;
            // test hosts and other embedders get no update checks.
            if (!VelopackLocator.IsCurrentSet)
                return;

            IUpdateSource source = string.IsNullOrWhiteSpace(feedUrl)
                ? new GithubSource(GitHubRepositoryUrl, null, includeDevBuilds)
                : new SimpleWebSource(feedUrl);
            _manager = new UpdateManager(source);
        }

        /// <summary>True once a downloaded update is waiting for <see cref="ApplyAndRestart"/>.</summary>
        public bool HasPendingUpdate => _pendingUpdate != null;

        /// <summary>True when this copy can update itself (a Velopack installation).</summary>
        public bool CanUpdate => _manager != null && _manager.IsInstalled;

        /// <summary>The version of the downloaded update, or null when none is pending.</summary>
        public string PendingVersion => _pendingUpdate?.TargetFullRelease.Version.ToString();

        /// <summary>
        /// The running version: Velopack's package version when installed, otherwise
        /// the assembly's informational version without build metadata.
        /// </summary>
        public string CurrentVersion
        {
            get
            {
                if (CanUpdate && _manager.CurrentVersion != null)
                    return _manager.CurrentVersion.ToString();

                return DisplayVersion();
            }
        }

        /// <summary>The assembly's informational version without the "+commit" build metadata.</summary>
        public static string DisplayVersion()
        {
            var assembly = typeof(UpdaterService).Assembly;
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? "unknown";
            var metadata = version.IndexOf('+');
            return metadata < 0 ? version : version.Substring(0, metadata);
        }

        public void Start()
        {
            if (_manager is null || !_manager.IsInstalled)
                return;

            _timer = new Timer(_ => _ = TickAsync(), null, FirstCheckDelay, CheckInterval);
        }

        /// <summary>Applies the downloaded update and restarts the process. Call only after the user agrees.</summary>
        public void ApplyAndRestart()
        {
            if (_manager != null && _pendingUpdate != null)
                _manager.ApplyUpdatesAndRestart(_pendingUpdate);
        }

        /// <summary>
        /// Checks the update source now and downloads a newer version if there is one,
        /// raising <see cref="UpdateReady"/> as a background check would. Waits for a
        /// background check already in progress instead of running a second one.
        /// </summary>
        public async Task<CheckResult> CheckNowAsync()
        {
            if (!CanUpdate)
                return CheckResult.NotInstalled;

            try
            {
                return await CheckAndDownloadAsync() ? CheckResult.UpdateReady : CheckResult.UpToDate;
            }
            catch
            {
                return CheckResult.Failed;
            }
        }

        public void Dispose() => _timer?.Dispose();

        private async Task TickAsync()
        {
            try
            {
                await CheckAndDownloadAsync();
            }
            catch
            {
                // Network hiccup or feed unreachable -- retry next tick.
            }
        }

        /// <returns>True when an update is downloaded and pending.</returns>
        private async Task<bool> CheckAndDownloadAsync()
        {
            await _checkLock.WaitAsync();
            try
            {
                if (_pendingUpdate != null)
                    return true;

                var newVersion = await _manager.CheckForUpdatesAsync();
                if (newVersion == null)
                    return false;

                await _manager.DownloadUpdatesAsync(newVersion);
                _pendingUpdate = newVersion;
            }
            finally
            {
                _checkLock.Release();
            }

            UpdateReady?.Invoke(_pendingUpdate.TargetFullRelease.Version.ToString());
            return true;
        }
    }
}
