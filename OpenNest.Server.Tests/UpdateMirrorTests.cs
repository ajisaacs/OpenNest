using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace OpenNest.Server.Tests;

/// <summary>
/// The desktop update feed the server mirrors from GitHub (<c>/updates/{channel}/</c>): which
/// release each channel serves, verified downloads, cleanup, restarts and the HTTP contract.
/// GitHub is a synthetic handler; nothing here reaches the network.
/// </summary>
public sealed class UpdateMirrorTests
{
    private const string ReleasesUrl = "https://api.github.com/repos/ajisaacs/OpenNest/releases?per_page=10";

    [Fact]
    public async Task Refresh_MirrorsNothingUntilAClientAsksForAChannel()
    {
        var github = new FakeGitHub().WithRelease("v0.6.1", "0.6.1", prerelease: false);
        using var directory = new TempDirectory();
        var mirror = new UpdateMirror(directory.Path, new HttpClient(github));

        await mirror.RefreshAsync();

        Assert.Empty(github.Requests);
        Assert.NotNull(mirror.GetStatus().LastChecked);
    }

    [Fact]
    public async Task Api_ServesTheStableFeedAndPackageAfterTheFirstRequestWakesARefresh()
    {
        var github = new FakeGitHub()
            .WithRelease("dev-build", "0.7.0-dev.2020", prerelease: true)
            .WithRelease("v0.6.1", "0.6.1", prerelease: false);
        using var factory = new ServerFactory(updateHandler: github);
        using var client = factory.CreateClient();
        var mirror = factory.Services.GetRequiredService<UpdateMirror>();
        var woken = 0;
        mirror.RefreshRequested += () => woken++;

        // Velopack appends its own query; the route ignores it.
        var before = await client.GetAsync("/updates/stable/releases.win.json?arch=x64&localVersion=0.6.0");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, before.StatusCode);
        Assert.Equal(1, woken);

        await mirror.RefreshAsync();

        var feed = await client.GetFromJsonAsync<JsonElement>("/updates/stable/releases.win.json");
        var asset = Assert.Single(feed.GetProperty("Assets").EnumerateArray().ToList());
        Assert.Equal("0.6.1", asset.GetProperty("Version").GetString());
        Assert.Equal("OpenNest-0.6.1-full.nupkg", asset.GetProperty("FileName").GetString());
        Assert.Equal(FakeGitHub.Hash("0.6.1"), asset.GetProperty("SHA256").GetString());
        Assert.Equal(FakeGitHub.Package("0.6.1"), await client.GetByteArrayAsync("/updates/stable/OpenNest-0.6.1-full.nupkg"));

        // The development build is never fetched for a stable-only shop.
        Assert.DoesNotContain(github.Requests, r => r.Contains("dev-build"));
        Assert.Equal(1, github.Requests.Count(r => r.StartsWith("https://api.github.com/", StringComparison.Ordinal)));

        var status = await client.GetFromJsonAsync<JsonElement>("/api/updates");
        Assert.Equal("0.6.1", status.GetProperty("stable").GetString());
        Assert.Equal(JsonValueKind.Null, status.GetProperty("dev").ValueKind);
        Assert.Equal(JsonValueKind.Null, status.GetProperty("lastError").ValueKind);
    }

    [Fact]
    public async Task Api_RefusesUnknownChannelsAndFilesTheFeedDoesNotName()
    {
        var github = new FakeGitHub().WithRelease("v0.6.1", "0.6.1", prerelease: false);
        using var factory = new ServerFactory(updateHandler: github);
        using var client = factory.CreateClient();
        var mirror = factory.Services.GetRequiredService<UpdateMirror>();
        mirror.GetFeed(UpdateMirror.Stable);
        await mirror.RefreshAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/updates/beta/releases.win.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/updates/dev/OpenNest-0.6.1-full.nupkg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/updates/stable/OpenNest-0.6.0-full.nupkg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/updates/stable/..%2F..%2Fnests.db")).StatusCode);
    }

    [Fact]
    public async Task Dev_ServesTheHigherOfTheDevelopmentBuildAndTheNewestRelease()
    {
        var github = new FakeGitHub()
            .WithRelease("dev-build", "0.7.0-dev.2020", prerelease: true)
            .WithRelease("v0.6.1", "0.6.1", prerelease: false)
            .WithRelease("v0.5.1", null, prerelease: true); // Older pre-release with no feed.
        using var directory = new TempDirectory();
        var mirror = new UpdateMirror(directory.Path, new HttpClient(github));
        mirror.GetFeed(UpdateMirror.Dev);

        await mirror.RefreshAsync();
        Assert.Equal("0.7.0-dev.2020", mirror.GetStatus().Dev);

        // A full release published before the next development build outranks it.
        github.Clear()
            .WithRelease("v0.7.0", "0.7.0", prerelease: false)
            .WithRelease("dev-build", "0.7.0-dev.2020", prerelease: true);
        await mirror.RefreshAsync();

        Assert.Equal("0.7.0", mirror.GetStatus().Dev);
        Assert.Null(mirror.GetStatus().Stable);
        Assert.Equal(new[] { "OpenNest-0.7.0-full.nupkg" }, PackageFiles(directory.Path));
    }

    [Fact]
    public async Task Refresh_DownloadsEachPackageOnceAndDeletesSupersededOnes()
    {
        var github = new FakeGitHub().WithRelease("v0.6.1", "0.6.1", prerelease: false);
        using var directory = new TempDirectory();
        var mirror = new UpdateMirror(directory.Path, new HttpClient(github));
        mirror.GetFeed(UpdateMirror.Stable);

        await mirror.RefreshAsync();
        await mirror.RefreshAsync();
        Assert.Equal(1, github.Requests.Count(r => r.EndsWith(".nupkg", StringComparison.Ordinal)));

        github.Clear().WithRelease("v0.6.2", "0.6.2", prerelease: false).WithRelease("v0.6.1", "0.6.1", prerelease: false);
        await mirror.RefreshAsync();

        Assert.Equal("0.6.2", mirror.GetStatus().Stable);
        Assert.Equal(new[] { "OpenNest-0.6.2-full.nupkg" }, PackageFiles(directory.Path));
        Assert.Null(mirror.GetPackagePath(UpdateMirror.Stable, "OpenNest-0.6.1-full.nupkg"));
    }

    [Fact]
    public async Task Refresh_RejectsAPackageThatDoesNotMatchItsFeedAndKeepsTheCurrentOne()
    {
        var github = new FakeGitHub().WithRelease("v0.6.1", "0.6.1", prerelease: false);
        using var directory = new TempDirectory();
        var mirror = new UpdateMirror(directory.Path, new HttpClient(github));
        mirror.GetFeed(UpdateMirror.Stable);
        await mirror.RefreshAsync();

        github.Clear().WithRelease("v0.6.2", "0.6.2", prerelease: false, corrupt: true);
        await mirror.RefreshAsync();

        var status = mirror.GetStatus();
        Assert.Equal("0.6.1", status.Stable);
        Assert.Contains("SHA-256", status.LastError);
        Assert.Equal(new[] { "OpenNest-0.6.1-full.nupkg" }, PackageFiles(directory.Path));
    }

    [Fact]
    public async Task Refresh_KeepsServingWhenGitHubIsUnreachable()
    {
        var github = new FakeGitHub().WithRelease("v0.6.1", "0.6.1", prerelease: false);
        using var directory = new TempDirectory();
        var mirror = new UpdateMirror(directory.Path, new HttpClient(github));
        mirror.GetFeed(UpdateMirror.Stable);
        await mirror.RefreshAsync();

        github.Clear();
        await mirror.RefreshAsync();

        Assert.NotNull(mirror.GetStatus().LastError);
        Assert.NotNull(mirror.GetFeed(UpdateMirror.Stable));
    }

    [Fact]
    public async Task Restart_ServesTheSavedFeedAndKeepsRefreshingIt()
    {
        var github = new FakeGitHub().WithRelease("v0.6.1", "0.6.1", prerelease: false);
        using var directory = new TempDirectory();
        var first = new UpdateMirror(directory.Path, new HttpClient(github));
        first.GetFeed(UpdateMirror.Stable);
        await first.RefreshAsync();

        github.Clear().WithRelease("v0.6.2", "0.6.2", prerelease: false);
        var restarted = new UpdateMirror(directory.Path, new HttpClient(github));
        Assert.Equal("0.6.1", restarted.GetStatus().Stable);
        Assert.NotNull(restarted.GetPackagePath(UpdateMirror.Stable, "OpenNest-0.6.1-full.nupkg"));

        await restarted.RefreshAsync();
        Assert.Equal("0.6.2", restarted.GetStatus().Stable);
    }

    [Fact]
    public async Task Refresh_StopsMirroringAChannelNobodyHasAskedForInAWeek()
    {
        var github = new FakeGitHub().WithRelease("v0.6.1", "0.6.1", prerelease: false);
        using var directory = new TempDirectory();
        var now = DateTimeOffset.Parse("2026-10-28T12:00:00Z");
        var mirror = new UpdateMirror(directory.Path, new HttpClient(github), clock: () => now);
        mirror.GetFeed(UpdateMirror.Stable);
        await mirror.RefreshAsync();
        var requests = github.Requests.Count;

        now += UpdateMirror.WantedFor + TimeSpan.FromMinutes(1);
        await mirror.RefreshAsync();

        Assert.Equal(requests, github.Requests.Count);
    }

    [Theory]
    [InlineData("0.7.0", "0.6.1", 1)]
    [InlineData("0.7.0-dev.2020", "0.6.1", 1)]
    [InlineData("0.7.0", "0.7.0-dev.2020", 1)]
    [InlineData("0.7.0-dev.10", "0.7.0-dev.9", 1)]
    [InlineData("0.7.0-dev.9", "0.7.0-dev", 1)]
    [InlineData("0.7.0-beta", "0.7.0-alpha", 1)]
    [InlineData("0.7.0-alpha", "0.7.0-1", 1)]
    [InlineData("0.7.0+abc", "0.7.0", 0)]
    [InlineData("0.10.0", "0.9.0", 1)]
    [InlineData("0.6.1", "not-a-version", 1)]
    public void SemanticVersion_FollowsSemVerPrecedence(string higher, string lower, int expected)
    {
        Assert.Equal(expected, SemanticVersion.Compare(higher, lower));
        Assert.Equal(-expected, SemanticVersion.Compare(lower, higher));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("on", true)]
    [InlineData("off", false)]
    [InlineData(" OFF ", false)]
    public void Options_UpdatesAreOnUnlessTurnedOff(string? value, bool expected) =>
        Assert.Equal(expected, ServerOptions.UpdatesEnabled(value));

    private static string[] PackageFiles(string directory) =>
        Directory.GetFiles(Path.Combine(directory, "packages")).Select(Path.GetFileName).OfType<string>().ToArray();

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "opennest-updates-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    /// <summary>The GitHub release list, feeds and packages for synthetic versions, newest release first.</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        private readonly List<object> _releases = new();
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public List<string> Requests { get; } = new();

        public static byte[] Package(string version) => Encoding.UTF8.GetBytes($"package {version}");

        public static string Hash(string version) => Convert.ToHexString(SHA256.HashData(Package(version)));

        public FakeGitHub Clear()
        {
            _releases.Clear();
            _files.Clear();
            return this;
        }

        /// <param name="version">The feed's version, or null for a release without a Velopack feed.</param>
        public FakeGitHub WithRelease(string tag, string? version, bool prerelease, bool corrupt = false)
        {
            var assets = new List<object>();
            if (version is not null)
            {
                var package = $"OpenNest-{version}-full.nupkg";
                var feed = JsonSerializer.Serialize(new
                {
                    Assets = new[]
                    {
                        new { PackageId = "OpenNest", Version = version, Type = "Full", FileName = package, SHA1 = "00", SHA256 = Hash(version), Size = (long)Package(version).Length },
                    },
                });
                assets.Add(Asset(tag, "releases.win.json", Encoding.UTF8.GetBytes(feed)));
                var bytes = Package(version);
                assets.Add(Asset(tag, package, corrupt ? bytes.Select(b => (byte)(b ^ 1)).ToArray() : bytes));
            }

            assets.Add(Asset(tag, $"OpenNest.{tag}.win-x64.zip", new byte[] { 1 }));
            _releases.Add(new { tag_name = tag, draft = false, prerelease, assets });
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            if (url == ReleasesUrl)
            {
                Assert.Contains("OpenNest-Server", request.Headers.UserAgent.ToString());
                if (_releases.Count == 0)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(_releases) });
            }

            return Task.FromResult(_files.TryGetValue(url, out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private object Asset(string tag, string name, byte[] contents)
        {
            var url = $"https://github.com/ajisaacs/OpenNest/releases/download/{tag}/{name}";
            _files[url] = contents;
            return new { name, browser_download_url = url };
        }
    }
}
