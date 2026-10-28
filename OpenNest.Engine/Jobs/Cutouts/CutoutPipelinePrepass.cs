#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.CirclePacking;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Frame bundles prepared by <see cref="NestPipeline"/> before every whole-job solve:
/// engines place a frame proxy and never see its reserved inserts. Only actually placed
/// proxies expand, and the expanded physical layout is validated again.</summary>
/// <remarks>Bundles may nest: smaller frames are bundled first, so a larger round cutout can
/// take a ring part or an earlier bundle (a ring holding its own inserts) as one round insert.
/// Those go only through <see cref="CutoutCirclePacker"/>, which keeps every disc clear of the
/// others' whole enclosing circles; lattice and NFP placement could seat a part in a ring's
/// empty bore, or over a nested bundle's reserved inserts.</remarks>
internal sealed class CutoutPipelinePrepass
{
    // A job with thousands of repeats still offers the residual as ordinary demand;
    // do not multiply expensive Clipper/Fill preparation by every physical instance.
    private const int MaxBundlesPerJob = 32;

    /// <summary>Most parts on one frame-in-frame chain: flange, washer, smaller washer, slug.</summary>
    internal const int MaxLevels = 4;

    // Levels: parts on the deepest chain through the frame; Parts: every physical part.
    private sealed record Bundle(string FrameId, IReadOnlyList<NestJobPlacement> LocalInserts,
        int Levels, int Parts);
    private readonly IReadOnlyDictionary<string, Bundle> bundles;

    private CutoutPipelinePrepass(NestJob engineJob, IReadOnlyDictionary<string, Bundle> bundles)
    {
        EngineJob = engineJob;
        this.bundles = bundles;
    }

    internal NestJob EngineJob { get; }

    internal static CutoutPipelinePrepass? Prepare(NestJob original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.Plates.Count == 0)
            return null;
        // One reservation vector works for every offered stock. Current jobs cannot
        // represent stock-conditional insert quantities; use the largest spacing.
        var spacing = original.Plates.Max(p => p.PartSpacing);
        var memo = new CutoutMemo();
        var remaining = original.Parts.ToDictionary(p => p.Id, p => p.Quantity);
        var geometry = original.Parts.ToDictionary(p => p.Id, p => memo.Read(p.Geometry));
        // Proxies join these as they are built, so a larger frame can take one as an insert.
        var offers = original.Parts.ToList();
        var requirements = original.Parts.ToDictionary(p => p.Id);
        var round = original.Parts.ToDictionary(p => p.Id, p => geometry[p.Id] is { } g
            && memo.ItemOf(g).FillRatio >= CutoutCirclePacker.MinFillRatio);
        // An insert's outline cannot fit a hole of smaller area; bolt holes skip the searches.
        var outline = original.Parts.ToDictionary(p => p.Id, p => geometry[p.Id] is { } g
            ? System.Math.Abs(g.Perimeter.Area()) : double.PositiveInfinity);
        // Lattice and NFP placement read a part's holes as free space, so they take as inserts
        // only parts whose holes no part of the job fits by area: no later insert can land in
        // one. Proxies are never solid; their frames' holes hold reserved inserts.
        var smallestOutline = outline.Values.DefaultIfEmpty(double.PositiveInfinity).Min();
        var solid = original.Parts.ToDictionary(p => p.Id, p => geometry[p.Id] is { } g
            && g.Cutouts.All(c => System.Math.Abs(c.Area()) < smallestOutline));
        var bundles = new Dictionary<string, Bundle>(StringComparer.Ordinal);
        var byFrame = new Dictionary<string, List<NestJobPart>>(StringComparer.Ordinal);
        // A proposal depends on the frame, the hole and the offered inserts with their local
        // rotation policies, not on the frame angle itself: automatic-rotation inserts ask the
        // same question at every angle, and so does every instance of the same frame while the
        // remaining demand is unchanged. Replay those answers.
        var proposals = new Dictionary<string, IReadOnlyList<NestJobPlacement>>(StringComparer.Ordinal);
        // Smallest first: a frame can only hold smaller parts, so its candidate inner bundles
        // already exist, and small holes take small inserts before large holes are tried.
        var frames = original.Parts.Where(p => geometry[p.Id] is { Cutouts.Count: > 0 })
            .OrderBy(p => System.Math.Abs(geometry[p.Id]!.Perimeter.Area()))
            .ToArray();
        foreach (var frame in frames)
        {
            token.ThrowIfCancellationRequested();
            if (bundles.Count >= MaxBundlesPerJob)
                break;
            var profile = geometry[frame.Id]!;
            var proxies = new List<NestJobPart>();
            byFrame[frame.Id] = proxies;
            for (var instance = 0; instance < frame.Quantity; instance++)
            {
                token.ThrowIfCancellationRequested();
                if (bundles.Count >= MaxBundlesPerJob)
                    break;
                IReadOnlyList<NestJobPlacement>? chosen = null;
                double frameAngle = 0;
                // Outer-perimeter symmetry cannot deduplicate asymmetric cutouts.
                foreach (var angle in frame.Rotation.EnumerateAngles(maxSamples: 16))
                {
                    token.ThrowIfCancellationRequested();
                    if (!NestUnplacedParts.FitsOfferedStock(frame, angle, original.Plates))
                        continue;
                    var local = new List<NestJobPlacement>();
                    var available = new Dictionary<string, int>(remaining);
                    for (var hole = 0; hole < profile.Cutouts.Count; hole++)
                    {
                        token.ThrowIfCancellationRequested();
                        var holeArea = System.Math.Abs(profile.Cutouts[hole].Area());
                        var eligible = offers
                            .Where(p => p.Id != frame.Id && available[p.Id] > 0
                                && p.Priority == frame.Priority && outline[p.Id] <= holeArea
                                && (!bundles.TryGetValue(p.Id, out var b)
                                    || (b.FrameId != frame.Id && b.Levels < MaxLevels)))
                            .Select(p => new NestJobPart(p.Id, p.Geometry, available[p.Id],
                                p.Priority, LocalRotation(p.Rotation, angle)))
                            .ToArray();
                        var flat = eligible.Where(p => solid.GetValueOrDefault(p.Id)).ToArray();
                        var nested = CutoutCirclePacker.RoundHole(profile.Cutouts[hole]) == null
                            ? Array.Empty<NestJobPart>()
                            : eligible.Where(p => round[p.Id] && !solid.GetValueOrDefault(p.Id)).ToArray();
                        if (flat.Length == 0 && nested.Length == 0)
                            continue;
                        foreach (var pose in Propose(frame, hole, flat, nested))
                        {
                            token.ThrowIfCancellationRequested();
                            var source = geometry[pose.PartId]!;
                            if (!requirements[pose.PartId].Rotation.Allows(angle + pose.Rotation)
                                || local.Any(other => !memo.Clears(
                                    geometry[other.PartId]!, other, source, pose, spacing)))
                                continue;
                            local.Add(pose);
                            available[pose.PartId]--;
                        }
                    }
                    if (Weight(local) <= Weight(chosen ?? Array.Empty<NestJobPlacement>()))
                        continue;
                    chosen = local.ToArray();
                    frameAngle = angle;
                }
                if (chosen == null || chosen.Count == 0)
                    break;
                var proxyId = $"__cutout-proxy-{frame.Id}-{instance}";
                if (requirements.ContainsKey(proxyId))
                    throw new InvalidOperationException("Cutout proxy ID collides with a requirement.");
                var levels = 1 + chosen.Max(p => bundles.TryGetValue(p.PartId, out var b) ? b.Levels : 1);
                bundles.Add(proxyId, new Bundle(frame.Id, chosen, levels, 1 + Weight(chosen)));
                var proxy = new NestJobPart(proxyId, frame.Geometry, 1, frame.Priority,
                    RotationPolicy.Fixed(frameAngle));
                proxies.Add(proxy);
                remaining[frame.Id]--;
                foreach (var pose in chosen)
                    remaining[pose.PartId]--;
                offers.Add(proxy);
                requirements.Add(proxyId, proxy);
                geometry.Add(proxyId, profile);
                outline.Add(proxyId, outline[frame.Id]);
                round.Add(proxyId, round[frame.Id]);
                remaining.Add(proxyId, 1);
            }
        }
        if (bundles.Count == 0)
            return null;
        var transformed = new List<NestJobPart>();
        foreach (var part in original.Parts)
        {
            // A proxy another frame took as an insert is reserved inside that frame's bundle.
            if (byFrame.TryGetValue(part.Id, out var proxies))
                transformed.AddRange(proxies.Where(p => remaining[p.Id] > 0));
            if (remaining[part.Id] > 0)
                transformed.Add(new NestJobPart(part.Id, part.Geometry, remaining[part.Id],
                    part.Priority, part.Rotation));
        }
        return new CutoutPipelinePrepass(new NestJob(transformed, original.Plates,
            original.Options), bundles);

        // Ties keep the flat router's proposal, which predates nesting.
        IReadOnlyList<NestJobPlacement> Propose(NestJobPart frame, int hole,
            NestJobPart[] flat, NestJobPart[] nested)
        {
            var key = ProposalKey(frame, hole, flat, nested);
            if (proposals.TryGetValue(key, out var replay))
                return replay;
            var proposed = ProposeCore(frame, hole, flat, nested);
            proposals[key] = proposed;
            return proposed;
        }

        IReadOnlyList<NestJobPlacement> ProposeCore(NestJobPart frame, int hole,
            NestJobPart[] flat, NestJobPart[] nested)
        {
            IReadOnlyList<NestJobPlacement> poses = Array.Empty<NestJobPlacement>();
            if (nested.Length > 0)
                poses = CutoutCirclePacker.Fill(frame, hole,
                    flat.Where(p => round[p.Id]).Concat(nested).ToArray(), spacing, memo, token);
            // Seating every flat copy as a disc leaves the slower proposals nothing to beat.
            var flatIds = flat.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            if (flat.Length == 0
                || poses.Count(p => flatIds.Contains(p.PartId)) == flat.Sum(p => (long)p.Quantity))
                return poses;
            var router = CutoutRouter.Fill(frame, hole, flat, spacing, memo, token);
            return Weight(router) >= Weight(poses) ? router : poses;
        }

        // Physical parts a proposal places: a nested bundle counts its frame and contents.
        int Weight(IReadOnlyList<NestJobPlacement> poses) =>
            poses.Sum(p => bundles.TryGetValue(p.PartId, out var b) ? b.Parts : 1);
    }

    private static string ProposalKey(NestJobPart frame, int hole, NestJobPart[] flat, NestJobPart[] nested)
    {
        var key = new System.Text.StringBuilder();
        key.Append(frame.Id).Append('|').Append(hole);
        foreach (var part in flat)
            Append(part, 'f');
        foreach (var part in nested)
            Append(part, 'n');
        return key.ToString();

        void Append(NestJobPart part, char kind)
        {
            var policy = part.Rotation;
            key.Append('|').Append(kind).Append(part.Id).Append(':').Append(part.Quantity).Append(':')
                .Append((int)policy.Kind).Append(':').Append(policy.Start.ToString("R")).Append(':')
                .Append(policy.End.ToString("R")).Append(':').Append(policy.Step.ToString("R")).Append(':')
                .Append(policy.Allow180Equivalent ? 1 : 0);
        }
    }

    private static RotationPolicy LocalRotation(RotationPolicy original, double frameAngle) =>
        original.Kind switch
        {
            RotationPolicyKind.Automatic => RotationPolicy.Automatic,
            RotationPolicyKind.Fixed => RotationPolicy.Fixed(original.Start - frameAngle,
                original.Allow180Equivalent),
            _ => RotationPolicy.BoundedSweep(original.Start - frameAngle,
                original.End - frameAngle, original.Step, original.Allow180Equivalent),
        };

    /// <summary>Reserved insert copies per original requirement whose frame proxy the engine
    /// did not place; call only on engine output that <see cref="TryExpand"/> accepted.</summary>
    internal IReadOnlyDictionary<string, int> ReleasedInserts(NestJobResult engineResult)
    {
        var placed = engineResult.Plates.SelectMany(p => p.Placements)
            .Select(p => p.PartId).ToHashSet(StringComparer.Ordinal);
        var released = new Dictionary<string, int>(StringComparer.Ordinal);
        // Nested proxies are never offered to the engine; their outermost frame releases them.
        foreach (var part in EngineJob.Parts)
            if (bundles.TryGetValue(part.Id, out var bundle) && !placed.Contains(part.Id))
                Release(bundle);
        return released;

        void Release(Bundle bundle)
        {
            foreach (var insert in bundle.LocalInserts)
            {
                if (bundles.TryGetValue(insert.PartId, out var nested))
                {
                    released[nested.FrameId] = released.GetValueOrDefault(nested.FrameId) + 1;
                    Release(nested);
                }
                else
                    released[insert.PartId] = released.GetValueOrDefault(insert.PartId) + 1;
            }
        }
    }

    /// <summary>Reject untrusted accounting before expansion. A failed trial is never bindable,
    /// including with allowInvalid. Only actually placed proxies consume reserved inserts;
    /// an unplaced proxy's inserts stay unplaced demand of their own requirements.</summary>
    internal bool TryExpand(NestJob original, NestJobResult result, CancellationToken token,
        out NestJobResult? expanded,
        out string failure)
    {
        expanded = null;
        failure = "Invalid cutout engine accounting";
        var parts = EngineJob.Parts.ToDictionary(p => p.Id);
        var counts = parts.ToDictionary(p => p.Key, _ => 0);
        var indices = parts.ToDictionary(p => p.Key, _ => new HashSet<int>());
        var stockById = EngineJob.Plates.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var used = stockById.ToDictionary(s => s.Key, _ => 0, StringComparer.Ordinal);
        var expectedIndex = 0;
        foreach (var sheet in result.Plates)
        {
            token.ThrowIfCancellationRequested();
            if (sheet.PlateIndex != expectedIndex++ || sheet.Placements.Count == 0
                || !used.ContainsKey(sheet.StockId))
                return false;
            used[sheet.StockId]++;
            foreach (var pose in sheet.Placements)
            {
                if (pose.PartId == null || !parts.ContainsKey(pose.PartId)
                    || pose.InstanceIndex < 0 || !indices[pose.PartId].Add(pose.InstanceIndex)
                    || ++counts[pose.PartId] > parts[pose.PartId].Quantity)
                    return false;
            }
        }
        foreach (var row in indices)
        {
            token.ThrowIfCancellationRequested();
            if (row.Value.Any(index => index >= counts[row.Key]))
                return false;
        }
        if (result.Fulfillment.Count != parts.Count || result.StockUsage.Count != used.Count)
            return false;
        var seenParts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in result.Fulfillment)
        {
            token.ThrowIfCancellationRequested();
            if (row.PartId == null || !parts.TryGetValue(row.PartId, out var part)
                || !seenParts.Add(row.PartId)
                || row.Requested != part.Quantity || row.Placed != counts[row.PartId]
                || row.Unplaced != part.Quantity - counts[row.PartId])
                return false;
        }
        var seenStock = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in result.StockUsage)
        {
            token.ThrowIfCancellationRequested();
            if (row.StockId == null || !stockById.TryGetValue(row.StockId, out var stock)
                || !seenStock.Add(row.StockId)
                || row.Used != used[row.StockId]
                || row.Remaining != stock.Quantity - row.Used)
                return false;
        }
        var complete = parts.Values.All(p => counts[p.Id] == p.Quantity);
        if (result.Status != (complete ? NestJobStatus.Complete : NestJobStatus.Incomplete)
            || (complete && result.StopReason != NestJobStopReason.Completed)
            || (!complete && result.StopReason == NestJobStopReason.Completed))
            return false;

        var builder = new NestJobResultBuilder(original);
        foreach (var sheet in result.Plates)
        {
            token.ThrowIfCancellationRequested();
            var poses = new List<(string PartId, double X, double Y, double Rotation)>();
            foreach (var pose in sheet.Placements)
            {
                if (!bundles.TryGetValue(pose.PartId, out var bundle))
                {
                    poses.Add((pose.PartId, pose.X, pose.Y, pose.Rotation));
                    continue;
                }
                Expand(bundle, pose.X, pose.Y, pose.Rotation, poses);
            }
            // Canonical stock reference, not an engine-supplied object with altered settings.
            var stock = original.Plates.Single(s => s.Id == sheet.StockId);
            try
            {
                builder.AddSheet(stock, poses);
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

    // Inserts precede their frame at every level: cutting a frame's cutout releases the slug
    // each insert is cut from, so placed order (innermost first) is the safe cutting order.
    private void Expand(Bundle bundle, double x, double y, double rotation,
        List<(string PartId, double X, double Y, double Rotation)> poses)
    {
        var sine = System.Math.Sin(rotation);
        var cosine = System.Math.Cos(rotation);
        foreach (var local in bundle.LocalInserts)
        {
            var insertX = x + local.X * cosine - local.Y * sine;
            var insertY = y + local.X * sine + local.Y * cosine;
            if (bundles.TryGetValue(local.PartId, out var nested))
                Expand(nested, insertX, insertY, rotation + local.Rotation, poses);
            else
                poses.Add((local.PartId, insertX, insertY, rotation + local.Rotation));
        }
        poses.Add((bundle.FrameId, x, y, rotation));
    }
}
