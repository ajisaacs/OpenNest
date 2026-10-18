#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.CirclePacking;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;

namespace OpenNest.Engine.NestingEngines.Circles;

/// <summary>
/// Round-part nesting engine: every part is packed as its minimum enclosing circle
/// (<see cref="Item"/>), so blanks, washers, flanges and gears by their tip circle pack as discs
/// with exact tangent placement. Rings already nested inside rings by the cutout pre-pass arrive as
/// their outer frame and pack the same way.
///
/// Bids only on round jobs: when any readable part fills less than
/// <see cref="MinFillRatio"/> of its enclosing circle, it returns everything unplaced at once, so
/// Default discards it at almost no cost and keeps routing such jobs to Irregular and Rectangles.
///
/// Sheet by sheet (<see cref="SheetByStock"/>), each available stock is packed bottom-left (rows)
/// and left-bottom (columns), plain and seeded with a hexagonal lattice of the type with the most
/// remaining area, and bottom-left over a square lattice of it; the candidate with the lowest
/// estimated whole-job cost wins. The whole job runs once with that free choice and once
/// preferring each offered stock, and the best result by <see cref="CandidateScore"/> is kept;
/// its last sheet, and its last two sheets together, are then re-packed onto each single stock
/// and replaced when that lowers the whole job's cost.
/// Deterministic: no clocks or randomness; the only stop besides completion is the host token.
/// </summary>
public sealed class CirclesNestingEngine : INestingEngine
{
    /// <summary>Parts below this outline-to-enclosing-circle area ratio waste too much as discs;
    /// the same threshold the cutout circle packer uses.</summary>
    public const double MinFillRatio = CutoutCirclePacker.MinFillRatio;

    private static readonly (DiscOrder Order, DiscSeed Seed)[] Variants =
    {
        (DiscOrder.BottomLeft, DiscSeed.None), (DiscOrder.LeftBottom, DiscSeed.None),
        (DiscOrder.BottomLeft, DiscSeed.Hexagonal), (DiscOrder.LeftBottom, DiscSeed.Hexagonal),
        (DiscOrder.BottomLeft, DiscSeed.Square),
    };

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();

        var types = new List<DiscType>(job.Parts.Count);
        var remaining = new int[job.Parts.Count];
        for (var i = 0; i < job.Parts.Count; i++)
        {
            var part = job.Parts[i];
            var geometry = JobPartGeometry.TryRead(part.Geometry);
            var rotations = part.Rotation.EnumerateAngles(maxSamples: 1);
            var item = geometry == null ? null : Item.Create(geometry);
            if (item != null && item.FillRatio < MinFillRatio)
                return new NestJobResultBuilder(job, progress).Build(NestJobStopReason.NoPlacementFound);
            // Unreadable parts keep a placeholder so type indices match requirement order.
            types.Add(new DiscType(i, part, item ?? new Item(), rotations.Count > 0 ? rotations[0] : 0,
                geometry?.MaterialArea ?? 0));
            if (item != null && rotations.Count > 0 && job.Plates.Any(s => DiscPacker.Fits(s, types[i])))
                remaining[i] = part.Quantity;
        }

        var demand = types.Select(t => new SheetDemand(t.Index, t.Id, t.Priority, t.MaterialArea)).ToArray();
        // Look-ahead: sheet-by-sheet cost estimates can take a large sheet first and then need a
        // second sheet anyway, so the whole job also runs preferring each offered stock in turn.
        var forward = progress == null ? null : new CandidateProgress(progress);
        CandidateScore? best = null;
        var runs = new NestPlateStock?[] { null }.Concat(job.Plates.Count > 1 ? job.Plates : Array.Empty<NestPlateStock>());
        var run = 0;
        foreach (var preferred in runs)
        {
            token.ThrowIfCancellationRequested();
            var result = SheetByStock.Run(job, demand, remaining.ToArray(), Pack, forward, token,
                preferred: preferred);
            var scored = CandidateScore.Of(job, result, run++);
            if (best == null || scored.IsBetterThan(best))
                best = scored;
        }
        best = ReplanTail(best!);
        CandidateProgress.ReportCommits(best.Result, progress);
        return best.Result;

        // The last sheet is packed from whatever demand is left, often on a larger sheet than
        // it needs: re-pack the last sheet, then the last two together, onto each single stock.
        CandidateScore ReplanTail(CandidateScore chosen)
        {
            var index = types.ToDictionary(t => t.Id, t => t.Index, StringComparer.Ordinal);
            for (var tail = 1; tail <= System.Math.Min(2, chosen.Result.Plates.Count); tail++)
            {
                var sheets = chosen.Result.Plates;
                var keep = sheets.Take(sheets.Count - tail).ToList();
                var replaced = sheets.Skip(keep.Count).ToList();
                var left = new int[types.Count];
                foreach (var pose in replaced.SelectMany(p => p.Placements))
                    left[index[pose.PartId]]++;
                var count = left.Sum();
                var replacedCost = replaced.Sum(p => NestJobCost.NetSheetCost(job, p));
                foreach (var stock in job.Plates)
                {
                    token.ThrowIfCancellationRequested();
                    if (stock.Quantity is int available && keep.Count(p => p.StockId == stock.Id) >= available)
                        continue;
                    foreach (var layout in Pack(stock, left))
                    {
                        // Only a complete, cheaper tail can lower the whole job's cost.
                        if (layout.Poses.Count < count || layout.Envelope is not { } envelope
                            || NestJobCost.NetSheetCost(job.Options, stock, envelope) >= replacedCost)
                            continue;
                        var builder = new NestJobResultBuilder(job);
                        foreach (var sheet in keep)
                            builder.AddSheet(job.Plates.Single(s => s.Id == sheet.StockId),
                                sheet.Placements.Select(p => (p.PartId, p.X, p.Y, p.Rotation)));
                        builder.AddSheet(stock, layout.Poses);
                        var scored = CandidateScore.Of(job, builder.Build(chosen.Result.StopReason), run++);
                        if (scored.IsBetterThan(chosen))
                            chosen = scored;
                    }
                }
            }
            return chosen;
        }

        IEnumerable<SheetLayout> Pack(NestPlateStock stock, IReadOnlyList<int> left)
        {
            if (!types.Any(t => left[t.Index] > 0 && DiscPacker.Fits(stock, t)))
                yield break;
            foreach (var (order, seed) in Variants)
                yield return DiscPacker.Pack(types, left, stock, order, seed, token);
        }
    }
}
