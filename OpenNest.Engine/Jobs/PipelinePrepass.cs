#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Jobs;

/// <summary>
/// A pipeline pre-pass that offers the engine a transformed job (compound proxies standing in
/// for several parts) and expands the engine's answer back to the original job's parts, which
/// are then validated again. Cutout bundles and common-line pairs are the two kinds.
/// </summary>
internal interface IPipelinePrepass
{
    /// <summary>The job the engine solves.</summary>
    NestJob EngineJob { get; }

    /// <summary>The violation reported when the engine's answer cannot be expanded.</summary>
    string ReconciliationFailure { get; }

    /// <summary>Expands an answer to <see cref="EngineJob"/> into one for <paramref name="original"/>.</summary>
    bool TryExpand(NestJob original, NestJobResult result, CancellationToken token,
        out NestJobResult? expanded, out string failure);

    /// <summary>Copies per original requirement that an unplaced proxy left unplaced as a frame's insert.</summary>
    IReadOnlyDictionary<string, int> ReleasedInserts(NestJobResult engineResult);
}

/// <summary>The accounting every pre-pass checks before trusting an engine's answer.</summary>
internal static class PipelinePrepassAccounting
{
    /// <summary>
    /// True when <paramref name="result"/> is self-consistent for <paramref name="engineJob"/>:
    /// consecutive sheet indices, known stock and parts, unique instance indices within quantity,
    /// and fulfillment, stock usage and status rows that match the placements.
    /// </summary>
    internal static bool Matches(NestJob engineJob, NestJobResult result, CancellationToken token)
    {
        var parts = engineJob.Parts.ToDictionary(p => p.Id);
        var counts = parts.ToDictionary(p => p.Key, _ => 0);
        var indices = parts.ToDictionary(p => p.Key, _ => new HashSet<int>());
        var stockById = engineJob.Plates.ToDictionary(s => s.Id, StringComparer.Ordinal);
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
        return result.Status == (complete ? NestJobStatus.Complete : NestJobStatus.Incomplete)
            && (!complete || result.StopReason == NestJobStopReason.Completed)
            && (complete || result.StopReason != NestJobStopReason.Completed);
    }
}
