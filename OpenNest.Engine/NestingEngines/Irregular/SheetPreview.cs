#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.NestingEngines.Irregular;

/// <summary>
/// Live preview of the sheet a packer is filling, sent as job progress detail
/// (<see cref="NestJobProgress.LegacyProgress"/>) for front ends that draw it. Reports are
/// throttled to one per <see cref="Interval"/>; the clock only gates reporting and never feeds
/// back into packing, so the solve stays deterministic.
/// </summary>
internal sealed class SheetPreview(
    IProgress<NestJobProgress> progress,
    IReadOnlyList<PartType> types,
    Func<NestJobPart, Drawing> drawingFor
)
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private TimeSpan? last;

    /// <summary>Called after each placement on a sheet trial; reports when the interval has passed.</summary>
    public void Report(NestPlateStock stock, int plateIndex, IReadOnlyList<Placed> placed)
    {
        var now = clock.Elapsed;
        if (last is TimeSpan previous && now - previous < Interval)
            return;
        last = now;

        var parts = new List<Part>(placed.Count);
        foreach (var pose in placed)
        {
            // Same pose mapping as the materialized result: rotate about the snapshot origin, then move.
            var part = new Part(drawingFor(types[pose.Orientation.TypeIndex].Part));
            part.Rotate(pose.Orientation.Rotation);
            part.Location = new Vector(pose.X, pose.Y);
            part.UpdateBounds();
            parts.Add(part);
        }

        progress.Report(new NestJobProgress(NestJobStage.EvaluatingCandidate, stock.Id, plateIndex, 0, 0,
            new NestProgress
            {
                Phase = NestPhase.Custom,
                PlateNumber = plateIndex + 1,
                BestParts = parts,
                ActiveWorkArea = stock.WorkArea,
                IsOverallBest = true,
                Description = $"Irregular: packing plate {plateIndex + 1} on stock {stock.Id} ({parts.Count} parts)",
            }));
    }
}
