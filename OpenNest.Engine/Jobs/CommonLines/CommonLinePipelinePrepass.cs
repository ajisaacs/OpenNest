#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs.CommonLines;

/// <summary>
/// The common-line trial <see cref="NestPipeline"/> runs beside the ordinary solve when the
/// job's <see cref="NestJobOptions.CommonLineUsage"/> is on: every part that allows common lines
/// is offered as kerf-gap pairs (<see cref="CommonLinePair"/>) plus its odd copy, so any engine
/// places them without knowing about common lines. Placed pairs expand into their two copies and
/// one explicit <see cref="NestJobCommonLine"/> group each; the expanded layout is validated again.
/// </summary>
internal sealed class CommonLinePipelinePrepass : IPipelinePrepass
{
    /// <summary>Every pair proxy's part id starts with this; ordinary parts never do.</summary>
    internal const string ProxyPrefix = "__common-line-pair-";

    /// <summary>How many of a part's copies a trial offers as pairs.</summary>
    internal enum Share
    {
        /// <summary>As many pairs as the quantity allows.</summary>
        All,

        /// <summary>About half the copies paired, the rest single: rows that fit an odd count.</summary>
        Half,
    }

    private sealed record Proxy(string PartId, CommonLinePair Pair);

    private readonly IReadOnlyDictionary<string, Proxy> proxies;
    private readonly double kerf;

    private CommonLinePipelinePrepass(NestJob engineJob, IReadOnlyDictionary<string, Proxy> proxies, double kerf)
    {
        EngineJob = engineJob;
        this.proxies = proxies;
        this.kerf = kerf;
    }

    public NestJob EngineJob { get; }

    public string ReconciliationFailure => "Common-line engine output failed reconciliation";

    /// <summary>True for a job holding pair proxies: a common-line trial's engine job.</summary>
    internal static bool HasProxies(NestJob job) =>
        job.Parts.Any(p => p.Id.StartsWith(ProxyPrefix, StringComparison.Ordinal));

    /// <summary>
    /// The trial job, or null when the job does not use common lines or no part can pair
    /// (none allows it, has two copies, may turn half a turn and has a long enough straight edge).
    /// </summary>
    internal static CommonLinePipelinePrepass? Prepare(NestJob original, Share share, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(original);
        token.ThrowIfCancellationRequested();
        if (!original.Options.UsesCommonLines || original.Plates.Count == 0)
            return null;
        var kerf = original.Options.CommonLineKerf;
        // A pair a kerf apart only saves anything where the part spacing is wider than the kerf.
        if (original.Plates.All(stock => !(kerf < stock.PartSpacing)))
            return null;
        var ids = original.Parts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var proxies = new Dictionary<string, Proxy>(StringComparer.Ordinal);
        var parts = new List<NestJobPart>();
        foreach (var part in original.Parts)
        {
            token.ThrowIfCancellationRequested();
            var pairs = Pairs(part, share);
            var pair = pairs > 0 && CanTurnHalf(part.Rotation) ? CommonLinePair.Build(part.Geometry, kerf) : null;
            if (pair == null)
            {
                parts.Add(part);
                continue;
            }
            var proxyId = ProxyPrefix + part.Id;
            if (ids.Contains(proxyId))
                throw new InvalidOperationException("Common-line proxy ID collides with a requirement.");
            proxies.Add(proxyId, new Proxy(part.Id, pair));
            // The pair first: it is the larger piece, and engines keep input order among equals.
            parts.Add(new NestJobPart(proxyId, pair.Geometry, pairs, part.Priority, part.Rotation));
            var singles = part.Quantity - 2 * pairs;
            if (singles > 0)
                parts.Add(new NestJobPart(part.Id, part.Geometry, singles, part.Priority, part.Rotation)
                {
                    AllowCommonLine = part.AllowCommonLine,
                });
        }
        if (proxies.Count == 0)
            return null;
        var engineJob = new NestJob(parts, original.Plates, original.Options);
        try
        {
            NestJobValidator.Validate(engineJob);
        }
        catch (ArgumentException)
        {
            return null; // A trial the engine could not be asked is no trial; the ordinary solve stands.
        }
        return new CommonLinePipelinePrepass(engineJob, proxies, kerf);
    }

    private static int Pairs(NestJobPart part, Share share)
    {
        if (!part.AllowCommonLine || part.Quantity < 2)
            return 0;
        return share == Share.All ? part.Quantity / 2 : part.Quantity / 4;
    }

    // The second copy turns half a turn from the first, so the part must allow both angles.
    private static bool CanTurnHalf(RotationPolicy rotation) =>
        rotation.Kind == RotationPolicyKind.Automatic || rotation.Allow180Equivalent;

    public IReadOnlyDictionary<string, int> ReleasedInserts(NestJobResult engineResult) =>
        new Dictionary<string, int>(StringComparer.Ordinal);

    public bool TryExpand(NestJob original, NestJobResult result, CancellationToken token,
        out NestJobResult? expanded, out string failure)
    {
        expanded = null;
        failure = "Invalid common-line engine accounting";
        if (!PipelinePrepassAccounting.Matches(EngineJob, result, token))
            return false;
        var builder = new NestJobResultBuilder(original);
        foreach (var sheet in result.Plates)
        {
            token.ThrowIfCancellationRequested();
            if (sheet.CommonLines.Count > 0)
                return false; // Engines place pairs as parts; they never return groups of their own here.
            var poses = new List<(string PartId, double X, double Y, double Rotation)>();
            var groups = new List<NestJobCommonLine>();
            foreach (var pose in sheet.Placements)
            {
                if (!proxies.TryGetValue(pose.PartId, out var proxy))
                {
                    poses.Add((pose.PartId, pose.X, pose.Y, pose.Rotation));
                    continue;
                }
                var sine = System.Math.Sin(pose.Rotation);
                var cosine = System.Math.Cos(pose.Rotation);
                var pair = proxy.Pair;
                groups.Add(new NestJobCommonLine(kerf, new[] { poses.Count, poses.Count + 1 }));
                poses.Add((proxy.PartId, pose.X, pose.Y, pose.Rotation));
                poses.Add((proxy.PartId,
                    pose.X + pair.SecondX * cosine - pair.SecondY * sine,
                    pose.Y + pair.SecondX * sine + pair.SecondY * cosine,
                    pose.Rotation + CommonLinePair.SecondRotation));
            }
            var stock = original.Plates.Single(s => s.Id == sheet.StockId);
            try
            {
                builder.AddSheet(stock, poses, groups);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return false;
            }
        }
        token.ThrowIfCancellationRequested();
        expanded = builder.Build(result.StopReason);
        return true;
    }
}
