using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Engine.Tests.NestingEngines;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

/// <summary>
/// Slice 6 task 6.4: regressions for the independent Slice 3b reviews of last-resort clamp
/// removal (D8/D9, C10-C13). Geometry: a 23"-long, 3"-wide sheet with 1.5 x 0.625 clamps
/// centred at X = 6 and X = 21 (clamp-1 spans X [5.25, 6.75], clamp-2 [20.25, 21.75]); a
/// 3"-tall part always crosses the clamp band, a 14"-wide part fits only with one clamp
/// removed, a 22"-wide part only with both removed.
/// </summary>
public class ClampRemovalRepairTests
{
    private static ClampZone[] Zones() => new[]
    {
        new ClampZone("clamp-1", new Box(5.25, 0, 1.5, 0.625)),
        new ClampZone("clamp-2", new Box(20.25, 0, 1.5, 0.625)),
    };

    private static readonly ClampRail Rail = new(1.5, 0.625, new[] { 6.0, 21.0 });

    /// <summary>Stock resolved from the machine rail (as NestStockBuilder builds it).</summary>
    private static NestPlateStock Stock(int? quantity = null) =>
        new("sheet", new Size(3, 23), quantity, 0, default, 1, null, Zones()) { ClampRail = Rail };

    /// <summary>The same zones given explicitly, with no rail behind them.</summary>
    private static NestPlateStock ExplicitStock() =>
        JobBuilder.Stock("sheet", width: 3, length: 23, clampZones: Zones());

    private static NestItem Item(string name, double w, double h, int quantity) =>
        new() { Drawing = new Drawing(name, TestDrawingFactory.Rectangle(w, h)), Quantity = quantity };

    private static NestPipelineRequest Request(NestPlateStock stock, params NestItem[] items) =>
        new("Rectangles", items, new[] { stock });

    /// <summary>Delegates the ordinary solve to Rectangles, then fails every later call.</summary>
    private sealed class ProbeThrowEngine : INestingEngine
    {
        public int Calls;

        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null,
            CancellationToken token = default)
        {
            if (++Calls > 1)
                throw new InvalidOperationException("feasibility probe failed");
            return new RectanglesNestingEngine().Solve(job, progress, token);
        }
    }

    /// <summary>Solves with Rectangles and cancels the run on its second call.</summary>
    private sealed class CancelOnSecondCallEngine(CancellationTokenSource source) : INestingEngine
    {
        private int calls;

        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null,
            CancellationToken token = default)
        {
            var result = new RectanglesNestingEngine().Solve(job, progress, token);
            if (++calls == 2)
                source.Cancel();
            return result;
        }
    }

    // PM 1db4333f: an optional recovery probe failure must not discard the ordinary result.
    [Fact]
    public void FailingRecoveryProbeKeepsTheOrdinaryResult()
    {
        var engine = new ProbeThrowEngine();
        var request = Request(Stock(), Item("wide", 14, 3, 1), Item("small", 2, 1, 1));

        var result = NestPipeline.Run(engine, "probe-throw", request);

        Assert.True(engine.Calls > 1, "recovery never probed");
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        // Items map to part ids in request order: part-0 = wide (blocked), part-1 = small.
        Assert.Equal(1, Assert.Single(result.Raw.Fulfillment, f => f.PartId == "part-0").Unplaced);
        Assert.Equal(0, Assert.Single(result.Raw.Fulfillment, f => f.PartId == "part-1").Unplaced);
        Assert.All(result.Job.Plates, s => Assert.DoesNotContain("removed", s.Id));
    }

    [Fact]
    public void CancellationDuringRecoveryStillCancelsTheRun()
    {
        using var source = new CancellationTokenSource();
        var request = Request(Stock(), Item("wide", 14, 3, 1), Item("small", 2, 1, 1));

        Assert.ThrowsAny<OperationCanceledException>(() =>
            NestPipeline.Run(new CancelOnSecondCallEngine(source), "cancel", request, token: source.Token));
    }
    // PM 346d46ed: explicit zones have no rail to name a removed clamp or its time.
    [Fact]
    public void ExplicitZonesWithoutARailAreNeverRemoved()
    {
        var result = NestPipeline.Run(Request(ExplicitStock(), Item("wide", 14, 3, 1)));

        Assert.NotEqual(NestJobStatus.Complete, result.Status);
        Assert.All(result.Job.Plates, s => Assert.DoesNotContain("removed", s.Id));
    }

    [Fact]
    public void RailStockRemovalIsNamedOnACommittedPlateWithoutARail()
    {
        // The stock comes from a rail-following template; the commit target is an empty
        // sheet holding the same zones explicitly. The removal must still be recorded.
        var drawing = new Drawing("wide", TestDrawingFactory.Rectangle(14, 3));
        var template = new Plate(new Size(3, 23)) { EdgeSpacing = new Spacing(0, 0, 0, 0) };
        template.SetClampRail(Rail);
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        nest.PlateDefaults.Size = new Size(3, 23);
        nest.PlateDefaults.EdgeSpacing = new Spacing(0, 0, 0, 0);
        var target = nest.CreatePlate();
        foreach (var zone in Zones())
            target.ClampZones.Add(zone);
        Assert.Null(target.ClampRail);
        using var manager = new PlateManager(nest);

        var result = NestPipeline.Run(new NestPipelineRequest("Rectangles",
            new[] { new NestItem { Drawing = drawing, Quantity = 1 } }, NestStockBuilder.SinglePlate(template)));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        var plate = Assert.Single(NestPipelineCommit.ApplyToEmptyPlates(result, manager));

        Assert.Equal(new[] { "clamp-1" }, plate.RemovedClamps);
        Assert.NotNull(plate.ClampRail);
        Assert.Equal(ClampRail.DefaultRemovalMinutes, plate.ClampRemovalMinutes);
    }
    // PM 6ded8ac4 (d): the nearest-origin tie-break follows distance along the edge, not
    // the order the zones happen to be listed in.
    [Fact]
    public void TieBreakPrefersTheClampNearestTheOriginWhateverTheZoneOrder()
    {
        var reversed = new NestPlateStock("sheet", new Size(3, 23), null, 0, default, 1, null,
            Zones().Reverse().ToArray())
        { ClampRail = Rail };

        var result = NestPipeline.Run(Request(reversed, Item("wide", 14, 3, 1)));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        var sheet = Assert.Single(result.Raw.Plates);
        Assert.DoesNotContain(sheet.Stock.ClampZones, z => z.Id == "clamp-1");
        Assert.Contains(sheet.Stock.ClampZones, z => z.Id == "clamp-2");
    }
    // PM c5984c6e (C10): one lane's failure to place is not proof the part cannot fit. The
    // fixed-rotation L below fits at the origin with its notch over the clamp (an explicit
    // witness passes the layout check), but Rectangles packs its 10 x 10 bounding box and
    // cannot place it; no clamp may be removed for it.
    [Fact]
    public void PartThatFitsClampedInSomeLaneNeverRemovesAClamp()
    {
        var program = new OpenNest.CNC.Program();
        program.MoveTo(0, 0);
        program.LineTo(2, 0);
        program.LineTo(2, 8);
        program.LineTo(10, 8);
        program.LineTo(10, 10);
        program.LineTo(0, 10);
        program.LineTo(0, 0);
        var rail = new ClampRail(1.5, 0.625, new[] { 5.75 }); // clamp-1 spans X [5, 6.5]
        var size = new Size(10, 10);
        var stock = new NestPlateStock("sheet", size, null, 0, default, 1, null, rail.ResolveFor(size, 1))
        { ClampRail = rail };
        var drawing = new Drawing("notched", program);

        var witnessJob = new NestJob(new[] { DrawingJobMapper.FromItem("p", new NestItem { Drawing = drawing, Quantity = 1 }) },
            new[] { stock });
        var witness = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, new[] { new NestJobPlacement("p", 0, 0, 0, 0) }) },
            Array.Empty<PartFulfillment>(), Array.Empty<StockUsage>());
        Assert.Empty(NestLayoutCheck.Violations(witnessJob, witness)); // fits with the clamp in place

        var result = NestPipeline.Run(new NestPipelineRequest("Rectangles",
            new[] { new NestItem { Drawing = drawing, Quantity = 1 } },
            new[] { stock }));

        Assert.All(result.Job.Plates, s => Assert.DoesNotContain("removed", s.Id));
        Assert.All(result.Raw.Plates, sheet => Assert.Single(sheet.Stock.ClampZones));
    }
}
