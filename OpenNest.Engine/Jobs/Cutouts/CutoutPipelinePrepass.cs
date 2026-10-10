#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Internal-only frame bundles. No normal pipeline caller enables this.</summary>
internal sealed class CutoutPipelinePrepass
{
    // A job with thousands of repeats still offers the residual as ordinary demand;
    // do not multiply expensive Clipper/Fill preparation by every physical instance.
    private const int MaxBundlesPerJob = 32;
    private sealed record Bundle(string FrameId, IReadOnlyList<NestJobPlacement> LocalInserts);
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
        var remaining = original.Parts.ToDictionary(p => p.Id, p => p.Quantity);
        var geometry = original.Parts.ToDictionary(p => p.Id, p => JobPartGeometry.TryRead(p.Geometry));
        var bundles = new Dictionary<string, Bundle>(StringComparer.Ordinal);
        var byFrame = new Dictionary<string, List<NestJobPart>>(StringComparer.Ordinal);
        foreach (var frame in original.Parts)
        {
            token.ThrowIfCancellationRequested();
            if (bundles.Count >= MaxBundlesPerJob)
                break;
            var profile = geometry[frame.Id];
            if (profile == null || profile.Cutouts.Count == 0)
                continue;
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
                    if (!FitsOfferedStock(frame, angle, original.Plates))
                        continue;
                    var local = new List<NestJobPlacement>();
                    var available = new Dictionary<string, int>(remaining);
                    for (var hole = 0; hole < profile.Cutouts.Count; hole++)
                    {
                        token.ThrowIfCancellationRequested();
                        // Flat bundles: frames cannot simultaneously be inserts in another bundle.
                        var candidates = original.Parts
                            .Where(p => p.Id != frame.Id && available[p.Id] > 0
                                && p.Priority == frame.Priority
                                && geometry[p.Id] is { Cutouts.Count: 0 })
                            .Select(p => new NestJobPart(p.Id, p.Geometry, available[p.Id],
                                p.Priority, LocalRotation(p.Rotation, angle)))
                            .ToArray();
                        if (candidates.Length == 0)
                            break;
                        foreach (var pose in CutoutRouter.Fill(frame, hole, candidates, spacing, token))
                        {
                            token.ThrowIfCancellationRequested();
                            var source = geometry[pose.PartId]!;
                            var policy = original.Parts.Single(p => p.Id == pose.PartId).Rotation;
                            if (!policy.Allows(angle + pose.Rotation)
                                || local.Any(other => !NestLayoutCheck.Clears(
                                    geometry[other.PartId]!, other, source, pose, spacing)))
                                continue;
                            local.Add(pose);
                            available[pose.PartId]--;
                        }
                    }
                    if (local.Count <= (chosen?.Count ?? 0))
                        continue;
                    chosen = local.ToArray();
                    frameAngle = angle;
                }
                if (chosen == null || chosen.Count == 0)
                    break;
                var proxyId = $"__cutout-proxy-{frame.Id}-{instance}";
                if (remaining.ContainsKey(proxyId) || bundles.ContainsKey(proxyId))
                    throw new InvalidOperationException("Cutout proxy ID collides with a requirement.");
                bundles.Add(proxyId, new Bundle(frame.Id, chosen));
                proxies.Add(new NestJobPart(proxyId, frame.Geometry, 1, frame.Priority,
                    RotationPolicy.Fixed(frameAngle)));
                remaining[frame.Id]--;
                foreach (var pose in chosen)
                    remaining[pose.PartId]--;
            }
        }
        if (bundles.Count == 0)
            return null;
        var transformed = new List<NestJobPart>();
        foreach (var part in original.Parts)
        {
            if (byFrame.TryGetValue(part.Id, out var proxies))
                transformed.AddRange(proxies);
            if (remaining[part.Id] > 0)
                transformed.Add(new NestJobPart(part.Id, part.Geometry, remaining[part.Id],
                    part.Priority, part.Rotation));
        }
        return new CutoutPipelinePrepass(new NestJob(transformed, original.Plates,
            original.Options), bundles);
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

    private static bool FitsOfferedStock(NestJobPart frame, double angle,
        IReadOnlyList<NestPlateStock> stock)
    {
        var part = new Part(DrawingJobMapper.CreateDrawing(frame));
        part.Rotate(angle);
        var bounds = NestLayoutCheck.MaterialBounds(part);
        return stock.Any(s => s.Quantity != 0 && s.Fits(bounds.Length, bounds.Width,
            NestTolerances.WorkAreaSlack));
    }

    /// <summary>Reject untrusted accounting before expansion. A failed trial is never bindable,
    /// including with allowInvalid. Only actually placed proxies consume reserved inserts.</summary>
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
                poses.Add((bundle.FrameId, pose.X, pose.Y, pose.Rotation));
                var sine = System.Math.Sin(pose.Rotation);
                var cosine = System.Math.Cos(pose.Rotation);
                foreach (var local in bundle.LocalInserts)
                    poses.Add((local.PartId,
                        pose.X + local.X * cosine - local.Y * sine,
                        pose.Y + local.X * sine + local.Y * cosine,
                        pose.Rotation + local.Rotation));
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
}
