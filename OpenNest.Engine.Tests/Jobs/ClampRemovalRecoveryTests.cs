using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Engine.Tests.NestingEngines;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// D8/D9 last-resort clamp removal in the whole-job pipeline: a part blocked by clamps on
/// every offered stock gets the fewest clamps removed that let it fit (C10/C11), and the
/// residual solve is merged back into one result; everything else is untouched (C10's
/// bit-identical guarantee for an unblocked job).
/// </summary>
public class ClampRemovalRecoveryTests
{
    // Two clamps 15" apart on a 23"-long, 3"-wide sheet (no edge/part spacing, so the math
    // below is exact): clamp-1 spans X [5.25, 6.75], clamp-2 spans X [20.25, 21.75]. A 3"-tall
    // part exactly fills the sheet's Y extent, so it always sits at Y = 0 - through the clamp
    // band - for whatever X it lands at.
    private static ClampZone Clamp1() => new("clamp-1", new Box(5.25, 0, 1.5, 0.625));
    private static ClampZone Clamp2() => new("clamp-2", new Box(20.25, 0, 1.5, 0.625));

    // The machine rail those zones resolve from: only rail-backed clamps can be removed.
    private static readonly ClampRail Rail = new(1.5, 0.625, new[] { 6.0, 21.0 });

    private static NestPlateStock ClampedSheet(params ClampZone[] zones) =>
        RailSheet(null, zones);

    private static NestPlateStock RailSheet(int? quantity, params ClampZone[] zones) =>
        new("sheet", new Size(3, 23), quantity, 0, default, 1, null, zones) { ClampRail = Rail };

    private static readonly RectanglesNestingEngine Engine = new();

    [Fact]
    public void BlockedPartRemovesOnlyTheNearestOriginCoveredClamp()
    {
        // A 14"-wide part: with both clamps up, no X position in [0, 9] clears both (the gap
        // between them is 13.5" < 14"), so it is genuinely blocked (C10), not merely unlucky.
        var part = JobBuilder.Rectangle("p", w: 14, h: 3, count: 1);
        var stock = ClampedSheet(Clamp1(), Clamp2());
        var job = JobBuilder.Job(new[] { part }, new[] { stock });

        var raw = Engine.Solve(job);
        Assert.Equal(NestJobStatus.Incomplete, raw.Status); // sanity: today's solve fails

        var outcome = ClampRemovalRecovery.TryRecover(job, raw, Engine, default);

        Assert.NotNull(outcome);
        Assert.Equal(NestJobStatus.Complete, outcome!.Result.Status);
        var sheet = Assert.Single(outcome.Result.Plates);
        Assert.Single(sheet.Placements);
        // Removing either clamp alone would free enough room; "clamp-1" is nearest the origin.
        Assert.DoesNotContain(sheet.Stock.ClampZones, z => z.Id == "clamp-1");
        Assert.Contains(sheet.Stock.ClampZones, z => z.Id == "clamp-2");
        var fulfillment = Assert.Single(outcome.Result.Fulfillment);
        Assert.Equal(0, fulfillment.Unplaced);
        // The expanded job the result validates against offers the original stock plus
        // exactly the reduced-clamp variant actually used.
        Assert.Equal(2, outcome.Job.Plates.Count);
    }

    [Fact]
    public void RemovalFreesSpaceForOtherResidualDemandToo()
    {
        // C12: a sheet nested with a clamp removed may also take other residual parts.
        var blocked = JobBuilder.Rectangle("wide", w: 14, h: 3, count: 1);
        var filler = JobBuilder.Rectangle("filler", w: 2, h: 1, count: 1);
        var stock = ClampedSheet(Clamp1(), Clamp2());
        var job = JobBuilder.Job(new[] { blocked, filler }, new[] { stock });

        var raw = Engine.Solve(job);
        var outcome = ClampRemovalRecovery.TryRecover(job, raw, Engine, default);

        Assert.NotNull(outcome);
        Assert.Equal(NestJobStatus.Complete, outcome!.Result.Status);
        Assert.All(outcome.Result.Fulfillment, f => Assert.Equal(0, f.Unplaced));
    }

    [Fact]
    public void AlreadyCompleteResultIsUntouched()
    {
        // C10: no blocked part exists, so recovery is a strict no-op (bit-identical to Slice 3).
        var part = JobBuilder.Rectangle("p", w: 1, h: 1, count: 1);
        var stock = ClampedSheet(Clamp1(), Clamp2());
        var job = JobBuilder.Job(new[] { part }, new[] { stock });
        var raw = Engine.Solve(job);
        Assert.Equal(NestJobStatus.Complete, raw.Status);

        Assert.Null(ClampRemovalRecovery.TryRecover(job, raw, Engine, default));
    }

    [Fact]
    public void NoClampsOfferedNeverRecovers()
    {
        var part = JobBuilder.Rectangle("p", w: 14, h: 3, count: 1);
        var stock = JobBuilder.Stock("sheet", width: 3, length: 10); // too short, no clamps
        var job = JobBuilder.Job(new[] { part }, new[] { stock });
        var raw = Engine.Solve(job);
        Assert.NotEqual(NestJobStatus.Complete, raw.Status);

        Assert.Null(ClampRemovalRecovery.TryRecover(job, raw, Engine, default));
    }

    [Fact]
    public void PartBlockedOnlyByStockExhaustionIsNeverTreatedAsClampBlocked()
    {
        // The part fits a fresh clamped sheet on its own; only a single-sheet cap stops the
        // second copy. That is a capacity limit, not a clamp block, so nothing is removed.
        var part = JobBuilder.Rectangle("p", w: 12, h: 3, count: 2);
        var stock = RailSheet(1, Clamp1(), Clamp2());
        var job = JobBuilder.Job(new[] { part }, new[] { stock });

        var raw = Engine.Solve(job);
        Assert.Equal(NestJobStatus.Incomplete, raw.Status);
        Assert.Equal(NestJobStopReason.StockExhausted, raw.StopReason);

        Assert.Null(ClampRemovalRecovery.TryRecover(job, raw, Engine, default));
    }

    [Fact]
    public void UnfitEvenWithoutAnyClampStaysUnplaced()
    {
        // Wider than the whole sheet: removing every clamp still cannot make it fit.
        var part = JobBuilder.Rectangle("p", w: 30, h: 3, count: 1);
        var stock = ClampedSheet(Clamp1(), Clamp2());
        var job = JobBuilder.Job(new[] { part }, new[] { stock });
        var raw = Engine.Solve(job);
        Assert.Equal(NestJobStatus.Incomplete, raw.Status);

        Assert.Null(ClampRemovalRecovery.TryRecover(job, raw, Engine, default));
    }
}
