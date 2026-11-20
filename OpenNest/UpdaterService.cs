using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenNest.Desktop;
using OpenNest.Forms;
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
    /// the Options dialog points this Windows user's copies at a self-hosted feed instead;
    /// that URL is operator infrastructure and never lives in source control.
    /// In Database mode without such a URL, the nest server's mirror of the same feed
    /// (<c>{server}/updates/stable</c> or <c>/dev</c>) is read first, so a shop's workstations
    /// share one GitHub check and one download; GitHub is the fallback when the server has none.
    /// On the once-a-day and once-a-week development channels the background check waits until
    /// <see cref="DevBuildSchedule.BackgroundCheckDue"/>; <see cref="CheckNowAsync"/> never waits.
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
        private readonly UpdateChannel _channel = UpdateChannel.DevEveryBuild;
        private readonly bool _recordsRunningVersion;
        private DateTimeOffset? _runningSince;

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
            : this(Settings.Default.UpdateFeedUrl, Settings.Default.UpdateIncludeDevBuilds,
                NestStorage.Settings.IsDatabaseMode ? NestStorage.Settings.ServerUrl : null)
        {
            _channel = UpdateChannelOption.Read(
                Settings.Default.UpdateIncludeDevBuilds, Settings.Default.UpdateDevBuildInterval);
            _recordsRunningVersion = true;
        }

        public UpdaterService(string feedUrl, bool includeDevBuilds, string serverUrl = null)
        {
            // Only a process started through VelopackApp (MainApp) has a locator;
            // test hosts and other embedders get no update checks.
            if (!VelopackLocator.IsCurrentSet)
                return;

            _manager = new UpdateManager(CreateSource(feedUrl, includeDevBuilds, serverUrl));
        }

        /// <summary>
        /// A hand-set feed URL wins; otherwise a nest server (Database mode) is read first with
        /// GitHub as its fallback; otherwise GitHub alone.
        /// </summary>
        internal static IUpdateSource CreateSource(string feedUrl, bool includeDevBuilds, string serverUrl)
        {
            if (!string.IsNullOrWhiteSpace(feedUrl))
                return new SimpleWebSource(feedUrl);

            var github = new GithubSource(GitHubRepositoryUrl, null, includeDevBuilds);
            if (string.IsNullOrWhiteSpace(serverUrl))
                return github;

            var channel = includeDevBuilds ? "dev" : "stable";
            return new ServerFirstUpdateSource(new SimpleWebSource($"{serverUrl.TrimEnd('/')}/updates/{channel}"), github);
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

            if (_recordsRunningVersion)
                RecordRunningVersion();

            _timer = new Timer(_ => _ = TickAsync(), null, FirstCheckDelay, CheckInterval);
        }

        /// <summary>
        /// Starts the once-a-day/once-a-week clock the first time this copy runs a version,
        /// however that version arrived (background update, Check for Updates, an installer).
        /// </summary>
        private void RecordRunningVersion()
        {
            try
            {
                var storedVersion = Settings.Default.UpdateRunningVersion;
                var storedSince = DevBuildSchedule.ParseSince(Settings.Default.UpdateRunningSince);
                var (version, since) = DevBuildSchedule.Observe(
                    storedVersion, storedSince, CurrentVersion, DateTimeOffset.Now);
                _runningSince = since;
                if (version == storedVersion && since == storedSince)
                    return;

                Settings.Default.UpdateRunningVersion = version;
                Settings.Default.UpdateRunningSince = DevBuildSchedule.FormatSince(since);
                Settings.Default.Save();
            }
            catch
            {
                // An unwritable settings file must not stop updates; the clock restarts next run.
            }
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
            if (!DevBuildSchedule.BackgroundCheckDue(_channel, _runningSince, DateTimeOffset.Now))
                return;

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
