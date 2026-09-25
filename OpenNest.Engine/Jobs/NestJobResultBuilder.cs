using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Jobs;

/// <summary>Assembles committed sheets and quantity accounting without choosing or validating poses.</summary>
public sealed class NestJobResultBuilder
{
    private readonly NestJob job;
    private readonly IProgress<NestJobProgress> progress;
    private readonly Dictionary<string, NestJobPart> parts;
    private readonly Dictionary<string, NestPlateStock> stocks;
    private readonly Dictionary<string, int> placed;
    private readonly Dictionary<string, int> used;
    private readonly List<NestJobPlateResult> sheets = new();
    private int committedParts;

    /// <summary>Creates an empty result builder for the job and optional progress reporter.</summary>
    public NestJobResultBuilder(NestJob job, IProgress<NestJobProgress> progress = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        this.job = job;
        this.progress = progress;
        parts = job.Parts.ToDictionary(p => p.Id, StringComparer.Ordinal);
        stocks = job.Plates.ToDictionary(s => s.Id, StringComparer.Ordinal);
        placed = job.Parts.ToDictionary(p => p.Id, _ => 0, StringComparer.Ordinal);
        used = job.Plates.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
    }

    /// <summary>
    /// Commits a sheet in call order, assigning zero-based instance indices per part across sheets.
    /// Reports PlateCommitted and returns the new zero-based PlateIndex. Stock must belong to the job.
    /// Unknown parts, overproduction, and exhausted stock are rejected before accounting changes.
    /// </summary>
    public int AddSheet(
        NestPlateStock stock,
        IEnumerable<(string PartId, double X, double Y, double Rotation)> placements
    )
    {
        ArgumentNullException.ThrowIfNull(placements);
        RequireStock(stock);
        if (stock.Quantity.HasValue && used[stock.Id] >= stock.Quantity.Value)
            throw new InvalidOperationException("Stock is exhausted.");

        var counts = new Dictionary<string, int>(placed, StringComparer.Ordinal);
        var poses = new List<NestJobPlacement>();
        foreach (var pose in placements)
        {
            if (pose.PartId == null || !parts.TryGetValue(pose.PartId, out var part))
                throw new ArgumentException("Unknown part ID.", nameof(placements));
            var index = counts[pose.PartId];
            if (index >= part.Quantity)
                throw new InvalidOperationException("Placement exceeds the requested quantity.");
            poses.Add(new NestJobPlacement(pose.PartId, index, pose.X, pose.Y, pose.Rotation));
            counts[pose.PartId]++;
        }

        var plateIndex = sheets.Count;
        sheets.Add(new NestJobPlateResult(plateIndex, stock, poses));
        foreach (var count in counts)
            placed[count.Key] = count.Value;
        used[stock.Id]++;
        committedParts += poses.Count;
        progress?.Report(new NestJobProgress(
            NestJobStage.PlateCommitted, stock.Id, plateIndex, sheets.Count, committedParts));
        return plateIndex;
    }

    /// <summary>Number of physical sheets committed from this job's stock.</summary>
    public int SheetsUsed(NestPlateStock stock)
    {
        RequireStock(stock);
        return used[stock.Id];
    }

    /// <summary>Number of committed instances of the requirement ID.</summary>
    public int Placed(string partId) => placed[partId];

    /// <summary>True when every requested instance has been committed.</summary>
    public bool IsComplete => job.Parts.All(p => placed[p.Id] == p.Quantity);

    /// <summary>
    /// Returns a detached accounting snapshot in commit/input order. Complete jobs use Completed;
    /// otherwise the result is Incomplete and uses the supplied stop reason.
    /// </summary>
    public NestJobResult Build(NestJobStopReason stopReason)
    {
        var complete = IsComplete;
        return new NestJobResult(
            complete ? NestJobStatus.Complete : NestJobStatus.Incomplete,
            complete ? NestJobStopReason.Completed : stopReason,
            sheets,
            job.Parts.Select(p => new PartFulfillment(
                p.Id, p.Quantity, placed[p.Id], p.Quantity - placed[p.Id])),
            job.Plates.Select(s => new StockUsage(s.Id, used[s.Id], s.Quantity - used[s.Id]))
        );
    }

    private void RequireStock(NestPlateStock stock)
    {
        ArgumentNullException.ThrowIfNull(stock);
        if (!stocks.TryGetValue(stock.Id, out var owned) || !ReferenceEquals(stock, owned))
            throw new ArgumentException("Stock must belong to the job.", nameof(stock));
    }
}
