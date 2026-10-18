#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.RectanglePacking;

namespace OpenNest.Engine.NestingEngines.Rectangles;

/// <summary>
/// Rectangle-lane nesting engine: every part is nested as the axis-aligned box of its material at
/// its minimum-area rotations, packed with a maximal-rectangles free list (Jylänki 2010).
///
/// Built for jobs of plain and near-rectangular parts, where a part's box wastes almost nothing
/// and exact box packing beats contour-sliding engines on both speed and density. Irregular parts
/// are still placed validly, only as their bounding boxes; they are not nested into each other.
///
/// Sheet by sheet (<see cref="SheetByStock"/>), each available stock is packed under several
/// free-space scoring rules and two pick modes (best-fitting box anywhere, or largest type first),
/// and the candidate with the lowest estimated whole-job cost wins.
/// Deterministic: no clocks or randomness; the only stop besides completion is the host token.
/// </summary>
public sealed class RectanglesNestingEngine : INestingEngine
{
    private static readonly FitRule[] Rules =
    {
        FitRule.BestShortSide, FitRule.BestLongSide, FitRule.BestArea,
        FitRule.BottomLeft, FitRule.LeftBottom, FitRule.ContactPoint,
    };

    private static readonly PickMode[] Modes = { PickMode.Global, PickMode.Ordered };

    // Instance-owned diagnostics; no process-global counters or mutable solver state.
    internal Action<PickMode>? SheetPacked { get; init; }
    internal Action<RepeatBatch>? BatchSelected { get; init; }

    public NestJobResult Solve(
        NestJob job,
        IProgress<NestJobProgress>? progress = null,
        CancellationToken token = default
    )
    {
        ArgumentNullException.ThrowIfNull(job);
        token.ThrowIfCancellationRequested();

        var types = BoxCatalog.Build(job);
        var remaining = types.Select(t => t.Part.Quantity).ToArray();
        // Parts with unreadable geometry or no box that fits any offered sheet can never be placed.
        foreach (var t in types)
            if (t.Orientations.Count == 0 || !job.Plates.Any(stock => t.Orientations.Any(o => FitsStock(stock, o))))
                remaining[t.Index] = 0;

        var demand = types.Select(t => new SheetDemand(t.Index, t.Id, t.Priority, t.MaterialArea)).ToArray();
        return SheetByStock.Run(job, demand, remaining, Pack, progress, token, BatchSelected);

        IEnumerable<SheetLayout> Pack(NestPlateStock stock, IReadOnlyList<int> left)
        {
            // Both private MaxRects modes return only after their normal demand/no-fit search;
            // neither returns partial/budget-limited output. This is not a geometric impossibility proof.
            foreach (var mode in Modes)
                foreach (var rule in Rules)
                {
                    var plan = SheetPacker.Pack(types, left, stock, rule, mode, token);
                    SheetPacked?.Invoke(mode);
                    yield return new SheetLayout(stock, plan.Poses().ToArray(), plan.MaterialArea, plan.Envelope);
                }
        }
    }

    private static bool FitsStock(NestPlateStock stock, BoxOrientation o)
    {
        var work = stock.WorkArea;
        return o.Width <= work.Right - work.Left + SheetPacker.OverhangAllowance
            && o.Height <= work.Top - work.Bottom + SheetPacker.OverhangAllowance;
    }
}
