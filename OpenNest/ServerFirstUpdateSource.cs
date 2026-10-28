using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace OpenNest
{
    /// <summary>
    /// Reads the update feed the nest server mirrors, falling back to another source (GitHub)
    /// when the server cannot serve one: unreachable, a version without the mirror (404), or a
    /// channel it has not mirrored yet (503). Each package downloads from the source whose feed
    /// listed it, so a fallback feed's package never goes to the server.
    /// </summary>
    internal sealed class ServerFirstUpdateSource : IUpdateSource
    {
        private readonly IUpdateSource _server;
        private readonly IUpdateSource _fallback;
        private readonly ConditionalWeakTable<VelopackAsset, IUpdateSource> _origins = new();

        public ServerFirstUpdateSource(IUpdateSource server, IUpdateSource fallback)
        {
            _server = server;
            _fallback = fallback;
        }

        /// <summary>The source that answered the last feed request.</summary>
        public IUpdateSource LastFeedSource { get; private set; }

        public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string appId, string channel,
            Guid? stagingId = null, VelopackAsset latestLocalRelease = null)
        {
            VelopackAssetFeed feed;
            IUpdateSource source = _server;
            try
            {
                feed = await _server.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);
            }
            catch (Exception ex)
            {
                logger?.Warn($"Nest server update feed unavailable ({ex.Message}); checking GitHub instead.");
                source = _fallback;
                feed = await _fallback.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);
            }

            foreach (var asset in feed?.Assets ?? Array.Empty<VelopackAsset>())
                _origins.AddOrUpdate(asset, source);
            LastFeedSource = source;
            return feed;
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
            Action<int> progress, CancellationToken cancelToken = default)
        {
            var source = _origins.TryGetValue(releaseEntry, out var origin) ? origin : LastFeedSource ?? _server;
            return source.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
        }
    }
}
