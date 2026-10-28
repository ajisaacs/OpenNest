using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace OpenNest.WinForms.Tests;

public class ServerFirstUpdateSourceTests
{
    [Fact]
    public void FeedUrlWinsOverTheNestServer()
    {
        var source = Assert.IsType<SimpleWebSource>(
            UpdaterService.CreateSource("http://feed.example:8080", includeDevBuilds: true, serverUrl: "http://nests.example:8090"));

        Assert.Equal("http://feed.example:8080/", source.BaseUri.ToString());
    }

    [Fact]
    public void WithoutANestServerUpdatesComeFromGitHub()
    {
        Assert.IsType<GithubSource>(UpdaterService.CreateSource(null, includeDevBuilds: false, serverUrl: null));
        Assert.IsType<GithubSource>(UpdaterService.CreateSource("", includeDevBuilds: false, serverUrl: " "));
    }

    [Theory]
    [InlineData(false, "http://nests.example:8090/updates/stable")]
    [InlineData(true, "http://nests.example:8090/updates/dev")]
    public void DatabaseModeReadsTheServersChannelFirst(bool includeDevBuilds, string expected)
    {
        Assert.IsType<ServerFirstUpdateSource>(
            UpdaterService.CreateSource(null, includeDevBuilds, "http://nests.example:8090/"));

        Assert.Equal(expected, ServerChannelUrl(includeDevBuilds, "http://nests.example:8090/"));
    }

    [Fact]
    public async Task AServerFeedIsUsedAndItsPackagesDownloadFromTheServer()
    {
        var server = new FakeSource("0.6.1");
        var github = new FakeSource("0.6.1");
        var source = new ServerFirstUpdateSource(server, github);

        var feed = await source.GetReleaseFeed(NullVelopackLogger.Instance, "OpenNest", "win");
        await source.DownloadReleaseEntry(NullVelopackLogger.Instance, feed.Assets[0], "package", _ => { });

        Assert.Same(server, source.LastFeedSource);
        Assert.Equal((1, 1), (server.FeedRequests, server.Downloads));
        Assert.Equal((0, 0), (github.FeedRequests, github.Downloads));
    }

    [Fact]
    public async Task WhenTheServerHasNoFeedGitHubAnswersAndServesItsOwnPackage()
    {
        var server = new FakeSource(null);
        var github = new FakeSource("0.6.1");
        var source = new ServerFirstUpdateSource(server, github);

        var feed = await source.GetReleaseFeed(NullVelopackLogger.Instance, "OpenNest", "win");
        await source.DownloadReleaseEntry(NullVelopackLogger.Instance, feed.Assets[0], "package", _ => { });

        Assert.Same(github, source.LastFeedSource);
        Assert.Equal((1, 0), (server.FeedRequests, server.Downloads));
        Assert.Equal((1, 1), (github.FeedRequests, github.Downloads));
    }

    private static string ServerChannelUrl(bool includeDevBuilds, string serverUrl)
    {
        var source = (ServerFirstUpdateSource)UpdaterService.CreateSource(null, includeDevBuilds, serverUrl);
        var field = typeof(ServerFirstUpdateSource).GetField("_server", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return ((SimpleWebSource)field.GetValue(source)!).BaseUri.ToString().TrimEnd('/');
    }

    /// <summary>A feed with one full package, or (version null) a source that fails like an unreachable server.</summary>
    private sealed class FakeSource : IUpdateSource
    {
        private readonly string? _version;

        public FakeSource(string? version) => _version = version;

        public int FeedRequests { get; private set; }

        public int Downloads { get; private set; }

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string appId, string channel,
            Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            FeedRequests++;
            if (_version is null)
                throw new HttpRequestException("Response status code does not indicate success: 503 (Service Unavailable).");

            return Task.FromResult(new VelopackAssetFeed
            {
                Assets = new[]
                {
                    new VelopackAsset { PackageId = appId, Version = SemanticVersion.Parse(_version), Type = VelopackAssetType.Full, FileName = $"OpenNest-{_version}-full.nupkg" },
                },
            });
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
            Action<int> progress, CancellationToken cancelToken = default)
        {
            Downloads++;
            return Task.CompletedTask;
        }
    }
}
