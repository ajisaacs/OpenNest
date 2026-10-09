#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>Internal-only single-frame composite trial. No normal pipeline caller enables this.</summary>
internal sealed class CutoutPipelinePrepass
{
    private readonly string proxyId;
    private readonly string frameId;
    private readonly IReadOnlyList<NestJobPlacement> localInserts;

    private CutoutPipelinePrepass(NestJob engineJob, string proxyId, string frameId,
        IReadOnlyList<NestJobPlacement> localInserts)
    {
        EngineJob = engineJob;
        this.proxyId = proxyId;
        this.frameId = frameId;
        this.localInserts = localInserts;
    }

    internal NestJob EngineJob { get; }

    internal static CutoutPipelinePrepass? Prepare(NestJob original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (original.Plates.Count == 0)
            return null;
        // A single fixed-zero proxy is safe even when a symmetric outer perimeter causes
        // an engine to deduplicate rotations. General orientation intersections are deferred.
        var spacing = original.Plates.Max(p => p.PartSpacing);
        foreach (var frame in original.Parts.Where(p => p.Quantity == 1 && p.Rotation.Allows(0)))
        {
            var geometry = JobPartGeometry.TryRead(frame.Geometry);
            if (geometry == null || geometry.Cutouts.Count == 0)
                continue;
            var candidates = original.Parts.Where(p => p.Id != frame.Id).ToArray();
            if (candidates.Length == 0)
                continue;
            for (var hole = 0; hole < geometry.Cutouts.Count; hole++)
            {
                token.ThrowIfCancellationRequested();
                var poses = CutoutRouter.Fill(frame, hole, candidates, spacing, token);
                if (poses.Count == 0)
                    continue;
                var reserved = poses.GroupBy(p => p.PartId).ToDictionary(g => g.Key, g => g.Count());
                var proxyId = "__cutout-proxy-" + frame.Id;
                if (original.Parts.Any(p => p.Id == proxyId))
                    throw new InvalidOperationException("Cutout proxy ID collides with a requirement.");
                var transformed = new List<NestJobPart>();
                foreach (var part in original.Parts)
                {
                    if (part.Id == frame.Id)
                        transformed.Add(new NestJobPart(proxyId, part.Geometry, 1, part.Priority,
                            RotationPolicy.Fixed(0)));
                    else
                    {
                        var remaining = part.Quantity - reserved.GetValueOrDefault(part.Id);
                        if (remaining > 0)
                            transformed.Add(new NestJobPart(part.Id, part.Geometry, remaining,
                                part.Priority, part.Rotation));
                    }
                }
                return new CutoutPipelinePrepass(new NestJob(transformed, original.Plates,
                    original.Options), proxyId, frame.Id, poses);
            }
        }
        return null;
    }

    /// <summary>Reject untrusted accounting before expansion. A failed trial is never bindable,
    /// including with allowInvalid. Only actually placed proxies consume reserved inserts.</summary>
    internal bool TryExpand(NestJob original, NestJobResult result, out NestJobResult? expanded,
        out string failure)
    {
        expanded = null;
        failure = "Invalid cutout engine accounting";
        var parts = EngineJob.Parts.ToDictionary(p => p.Id);
        var counts = parts.ToDictionary(p => p.Key, _ => 0);
        var indices = parts.ToDictionary(p => p.Key, _ => new HashSet<int>());
        var used = EngineJob.Plates.ToDictionary(s => s.Id, _ => 0);
        var expectedIndex = 0;
        foreach (var sheet in result.Plates)
        {
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
        if (indices.Any(row => row.Value.Any(index => index >= counts[row.Key])))
            return false;
        if (result.Fulfillment.Count != parts.Count || result.StockUsage.Count != used.Count)
            return false;
        foreach (var row in result.Fulfillment)
            if (row.PartId == null || !parts.TryGetValue(row.PartId, out var part)
                || row.Requested != part.Quantity || row.Placed != counts[row.PartId]
                || row.Unplaced != part.Quantity - counts[row.PartId]
                || result.Fulfillment.Count(r => r.PartId == row.PartId) != 1)
                return false;
        foreach (var row in result.StockUsage)
        {
            var stock = EngineJob.Plates.FirstOrDefault(s => s.Id == row.StockId);
            if (stock == null || row.Used != used[row.StockId]
                || row.Remaining != stock.Quantity - row.Used
                || result.StockUsage.Count(r => r.StockId == row.StockId) != 1)
                return false;
        }
        var complete = parts.Values.All(p => counts[p.Id] == p.Quantity);
        if (result.Status != (complete ? NestJobStatus.Complete : NestJobStatus.Incomplete)
            || (complete && result.StopReason != NestJobStopReason.Completed)
            || (!complete && result.StopReason == NestJobStopReason.Completed))
            return false;

        // An incomplete solve cannot silently reserve inserts or reach the commit boundary.
        if (!complete)
        {
            failure = "Cutout engine did not complete the transformed job";
            return false;
        }
        var builder = new NestJobResultBuilder(original);
        foreach (var sheet in result.Plates)
        {
            var poses = new List<(string PartId, double X, double Y, double Rotation)>();
            foreach (var pose in sheet.Placements)
            {
                if (pose.PartId != proxyId)
                {
                    poses.Add((pose.PartId, pose.X, pose.Y, pose.Rotation));
                    continue;
                }
                poses.Add((frameId, pose.X, pose.Y, pose.Rotation));
                var sine = System.Math.Sin(pose.Rotation);
                var cosine = System.Math.Cos(pose.Rotation);
                foreach (var local in localInserts)
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
        expanded = builder.Build(result.StopReason);
        if (expanded.Status != NestJobStatus.Complete)
        {
            expanded = null;
            return false;
        }
        return true;
    }
}
