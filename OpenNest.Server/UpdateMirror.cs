using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenNest.Server;

/// <summary>
/// A caching mirror of the desktop app's Velopack feed, so the workstations of one shop check
/// for updates against their nest server instead of each calling GitHub. One refresh makes a
/// single GitHub API request (the release list), reads the feeds attached to the chosen releases
/// and downloads each channel's newest full package once, verified against the feed's size and
/// SHA-256, into <see cref="DirectoryPath"/>.
/// </summary>
/// <remarks>
/// Channels: <c>stable</c> serves the newest full release; <c>dev</c> serves whichever is higher
/// of that and the newest pre-release with a feed (the rolling development build). A channel is
/// mirrored only while clients ask for it (within <see cref="WantedFor"/>), so a server whose
/// workstations never take development builds never downloads one. Feeds are served with the
/// package file names Velopack wrote, unchanged: clients download <c>{feed URL}/{FileName}</c>.
/// </remarks>
public sealed class UpdateMirror
{
    public const string Stable = "stable";
    public const string Dev = "dev";
    public const string FeedFileName = "releases.win.json";
    public const string DefaultRepository = "ajisaacs/OpenNest";

    /// <summary>How long a channel stays mirrored after the last client asked for it.</summary>
    public static readonly TimeSpan WantedFor = TimeSpan.FromDays(7);

    private static readonly string[] Channels = { Stable, Dev };
    private static readonly JsonSerializerOptions FeedOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly string _repository;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<string, ChannelFeed> _feeds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _wanted = new(StringComparer.Ordinal);
    private DateTimeOffset? _lastChecked;
    private string? _lastError;

    public UpdateMirror(string directoryPath, HttpClient http, string repository = DefaultRepository, Func<DateTimeOffset>? clock = null)
    {
        DirectoryPath = directoryPath;
        _http = http;
        _repository = repository;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        Directory.CreateDirectory(PackagesPath);
        LoadSavedFeeds();
    }

    /// <summary>Raised when a client asks for a channel that holds no feed yet, so the next refresh need not wait.</summary>
    public event Action? RefreshRequested;

    public string DirectoryPath { get; }

    private string PackagesPath => Path.Combine(DirectoryPath, "packages");

    public static bool IsChannel(string channel) => Array.IndexOf(Channels, channel) >= 0;

    /// <summary>
    /// The channel's feed for a client, or null while nothing is mirrored for it (the route answers
    /// 503 and the client falls back to GitHub). Every call marks the channel as wanted.
    /// </summary>
    public byte[]? GetFeed(string channel)
    {
        bool missing;
        byte[]? feed;
        lock (_stateLock)
        {
            _wanted[channel] = _clock();
            feed = _feeds.TryGetValue(channel, out var current) ? current.Json : null;
            missing = feed is null;
        }

        if (missing)
            RefreshRequested?.Invoke();
        return feed;
    }

    /// <summary>The path of a package a channel's feed currently names, or null for any other file name.</summary>
    public string? GetPackagePath(string channel, string fileName)
    {
        lock (_stateLock)
        {
            if (!_feeds.TryGetValue(channel, out var feed) || feed.FileName != fileName)
                return null;
        }

        var path = Path.Combine(PackagesPath, fileName);
        return File.Exists(path) ? path : null;
    }

    public UpdateMirrorStatus GetStatus()
    {
        lock (_stateLock)
        {
            return new UpdateMirrorStatus
            {
                Stable = _feeds.TryGetValue(Stable, out var stable) ? stable.Version : null,
                Dev = _feeds.TryGetValue(Dev, out var dev) ? dev.Version : null,
                LastChecked = _lastChecked,
                LastError = _lastError,
            };
        }
    }

    /// <summary>
    /// Brings every wanted channel up to date with GitHub. Failures are recorded in
    /// <see cref="GetStatus"/> and leave the feeds already mirrored in place.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            var wanted = WantedChannels();
            if (wanted.Count > 0)
                await RefreshChannelsAsync(wanted, cancellationToken);

            lock (_stateLock)
            {
                _lastChecked = _clock();
                _lastError = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            lock (_stateLock)
            {
                _lastChecked = _clock();
                _lastError = ex.Message;
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private List<string> WantedChannels()
    {
        var now = _clock();
        lock (_stateLock)
            return Channels.Where(c => _wanted.TryGetValue(c, out var at) && now - at <= WantedFor).ToList();
    }

    private async Task RefreshChannelsAsync(List<string> channels, CancellationToken cancellationToken)
    {
        var releases = await GetJsonAsync<List<GitHubRelease>>(
            $"https://api.github.com/repos/{_repository}/releases?per_page=10", cancellationToken) ?? new();
        var published = releases.Where(r => !r.Draft && FeedAsset(r) is not null).ToList();

        // The API lists the newest releases first.
        var stable = published.FirstOrDefault(r => !r.Prerelease);
        var preRelease = published.FirstOrDefault(r => r.Prerelease);
        var feeds = new Dictionary<GitHubRelease, Candidate?>();

        foreach (var channel in channels)
        {
            var sources = channel == Stable ? new[] { stable } : new[] { stable, preRelease };
            Candidate? best = null;
            foreach (var release in sources.OfType<GitHubRelease>())
            {
                if (!feeds.TryGetValue(release, out var candidate))
                    feeds[release] = candidate = await ReadCandidateAsync(release, cancellationToken);
                if (candidate is not null && (best is null || SemanticVersion.Compare(candidate.Asset.Version, best.Asset.Version) > 0))
                    best = candidate;
            }

            if (best is not null)
                await PublishAsync(channel, best, cancellationToken);
        }

        DeleteUnreferencedPackages();
    }

    private async Task<Candidate?> ReadCandidateAsync(GitHubRelease release, CancellationToken cancellationToken)
    {
        var feed = await GetJsonAsync<VelopackFeed>(FeedAsset(release)!.DownloadUrl, cancellationToken);
        var full = feed?.Assets?
            .Where(a => a.Type == "Full" && IsPlainFileName(a.FileName) && SemanticVersion.IsValid(a.Version))
            .OrderByDescending(a => a.Version, SemanticVersion.Comparer)
            .FirstOrDefault();
        var package = full is null ? null : release.Assets.FirstOrDefault(a => a.Name == full.FileName);
        return package is null ? null : new Candidate(full!, package.DownloadUrl);
    }

    private async Task PublishAsync(string channel, Candidate candidate, CancellationToken cancellationToken)
    {
        var asset = candidate.Asset;
        var packagePath = Path.Combine(PackagesPath, asset.FileName);
        if (!File.Exists(packagePath))
            await DownloadPackageAsync(candidate, packagePath, cancellationToken);

        // Serve only the chosen package, as the feed Velopack wrote describes it.
        var json = JsonSerializer.SerializeToUtf8Bytes(new { Assets = new[] { asset } });
        var feedPath = Path.Combine(DirectoryPath, channel, FeedFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(feedPath)!);
        WriteAtomically(feedPath, json);

        lock (_stateLock)
            _feeds[channel] = new ChannelFeed(json, asset.FileName, asset.Version);
    }

    private async Task DownloadPackageAsync(Candidate candidate, string packagePath, CancellationToken cancellationToken)
    {
        var partial = packagePath + ".partial";
        try
        {
            using (var response = await _http.GetAsync(candidate.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var target = File.Create(partial);
                await source.CopyToAsync(target, cancellationToken);
            }

            var asset = candidate.Asset;
            var length = new FileInfo(partial).Length;
            if (asset.Size > 0 && length != asset.Size)
                throw new InvalidDataException($"{asset.FileName} is {length} bytes; its feed says {asset.Size}.");
            if (!string.IsNullOrEmpty(asset.SHA256))
            {
                await using var stream = File.OpenRead(partial);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                if (!hash.Equals(asset.SHA256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{asset.FileName} does not match its feed's SHA-256.");
            }

            File.Move(partial, packagePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial);
        }
    }

    private void DeleteUnreferencedPackages()
    {
        HashSet<string> keep;
        lock (_stateLock)
            keep = _feeds.Values.Select(f => f.FileName).ToHashSet(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(PackagesPath))
        {
            if (keep.Contains(Path.GetFileName(path)))
                continue;
            try { File.Delete(path); }
            catch (IOException) { } // Still being served; the next refresh retries.
            catch (UnauthorizedAccessException) { }
        }
    }

    private void LoadSavedFeeds()
    {
        foreach (var channel in Channels)
        {
            var path = Path.Combine(DirectoryPath, channel, FeedFileName);
            if (!File.Exists(path))
                continue;
            try
            {
                var json = File.ReadAllBytes(path);
                var asset = JsonSerializer.Deserialize<VelopackFeed>(json, FeedOptions)?.Assets?.SingleOrDefault();
                if (asset is null || !IsPlainFileName(asset.FileName) || !File.Exists(Path.Combine(PackagesPath, asset.FileName)))
                    continue;
                _feeds[channel] = new ChannelFeed(json, asset.FileName, asset.Version);
                // A restart keeps serving what clients were already taking.
                _wanted[channel] = _clock();
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
            {
                // An unreadable feed is rebuilt by the next refresh.
            }
        }
    }

    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("OpenNest-Server");
        if (url.StartsWith("https://api.github.com/", StringComparison.Ordinal))
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, FeedOptions, cancellationToken);
    }

    private static GitHubAsset? FeedAsset(GitHubRelease release) =>
        release.Assets.FirstOrDefault(a => a.Name == FeedFileName);

    // Feed file names become paths under the packages directory and URL segments.
    private static bool IsPlainFileName(string? name) =>
        !string.IsNullOrEmpty(name) && name == Path.GetFileName(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

    private static void WriteAtomically(string path, byte[] contents)
    {
        var temporary = path + ".tmp";
        File.WriteAllBytes(temporary, contents);
        File.Move(temporary, path, overwrite: true);
    }

    private sealed record ChannelFeed(byte[] Json, string FileName, string Version);

    private sealed record Candidate(VelopackFeedAsset Asset, string DownloadUrl);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    }

    private sealed class VelopackFeed
    {
        public List<VelopackFeedAsset>? Assets { get; set; }
    }
}

/// <summary>One entry of a Velopack <c>releases.{channel}.json</c>; properties keep Velopack's names.</summary>
public sealed class VelopackFeedAsset
{
    public string PackageId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Type { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? SHA1 { get; set; }
    public string? SHA256 { get; set; }
    public long Size { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NotesMarkdown { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? NotesHTML { get; set; }
}

/// <summary><c>GET /api/updates</c>: the versions each channel serves and the last refresh's outcome.</summary>
public sealed class UpdateMirrorStatus
{
    public string? Stable { get; init; }
    public string? Dev { get; init; }
    public DateTimeOffset? LastChecked { get; init; }
    public string? LastError { get; init; }
}

/// <summary>SemVer 2.0 precedence for feed versions such as <c>0.7.0-dev.2020</c>; build metadata is ignored.</summary>
public static class SemanticVersion
{
    public static readonly IComparer<string> Comparer = Comparer<string>.Create(Compare);

    public static bool IsValid(string? version) => version is not null && TryParse(version, out _, out _);

    public static int Compare(string? left, string? right)
    {
        if (!TryParse(left, out var leftCore, out var leftPre))
            return TryParse(right, out _, out _) ? -1 : 0;
        if (!TryParse(right, out var rightCore, out var rightPre))
            return 1;

        for (var i = 0; i < 3; i++)
        {
            var core = leftCore[i].CompareTo(rightCore[i]);
            if (core != 0)
                return core;
        }

        // A release outranks its pre-releases.
        if (leftPre.Length == 0 || rightPre.Length == 0)
            return (leftPre.Length == 0).CompareTo(rightPre.Length == 0);

        for (var i = 0; i < System.Math.Min(leftPre.Length, rightPre.Length); i++)
        {
            var leftNumeric = long.TryParse(leftPre[i], out var leftNumber);
            var rightNumeric = long.TryParse(rightPre[i], out var rightNumber);
            var part = (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftPre[i], rightPre[i]),
            };
            if (part != 0)
                return part;
        }

        return leftPre.Length.CompareTo(rightPre.Length);
    }

    private static bool TryParse(string? version, out long[] core, out string[] preRelease)
    {
        core = Array.Empty<long>();
        preRelease = Array.Empty<string>();
        if (string.IsNullOrEmpty(version))
            return false;

        var withoutMetadata = version.Split('+')[0];
        var dash = withoutMetadata.IndexOf('-');
        var numbers = (dash < 0 ? withoutMetadata : withoutMetadata[..dash]).Split('.');
        if (numbers.Length != 3)
            return false;

        core = new long[3];
        for (var i = 0; i < 3; i++)
        {
            if (!long.TryParse(numbers[i], out core[i]) || core[i] < 0)
                return false;
        }

        preRelease = dash < 0 ? Array.Empty<string>() : withoutMetadata[(dash + 1)..].Split('.');
        return preRelease.All(p => p.Length > 0);
    }
}
