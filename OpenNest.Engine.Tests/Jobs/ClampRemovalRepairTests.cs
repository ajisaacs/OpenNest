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
    private static int RemovedCount(NestJobPlateResult sheet) => 2 - sheet.Stock.ClampZones.Count;

    // PM f91e054a (C11): minimality holds per sheet. part-0 (22 wide) needs both clamps off,
    // part-1 (14 wide) only clamp-1; the sheet holding only part-1 must keep clamp-2.
    [Fact]
    public void EachSheetRemovesOnlyTheClampsItsOwnPartsNeed()
    {
        var result = NestPipeline.Run(Request(Stock(), Item("widest", 22, 3, 1), Item("wide", 14, 3, 1)));

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, result.Raw.Plates.Count);
        foreach (var sheet in result.Raw.Plates)
        {
            var needsBoth = sheet.Placements.Any(p => p.PartId == "part-0");
            Assert.Equal(needsBoth ? 2 : 1, RemovedCount(sheet));
            if (!needsBoth)
                Assert.Contains(sheet.Stock.ClampZones, z => z.Id == "clamp-2");
        }
    }

    // PM 245a555f (C12): reduced-clamp sheets come out of the same finite pool.
    [Theory]
    [InlineData(3, false)] // three blocked parts, one physical sheet
    [InlineData(1, true)]  // the ordinary part already used the only sheet
    public void RemovalNeverInventsSheetsBeyondTheFiniteStock(int wide, bool small)
    {
        var items = new List<NestItem> { Item("wide", 14, 3, wide) };
        if (small)
            items.Add(Item("small", 2, 1, 1));

        var result = NestPipeline.Run(Request(Stock(quantity: 1), items.ToArray()));

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Single(result.Raw.Plates);
        Assert.Equal(1, result.Raw.StockUsage.Sum(u => u.Used));
        Assert.Equal(1, result.Job.Plates.Sum(s => s.Quantity ?? int.MaxValue));
        Assert.All(result.Raw.StockUsage, u => Assert.True(u.Remaining is null or >= 0));
    }
    /// <summary>Never places anything; counts how often recovery asks it to.</summary>
    private sealed class CountingNoFitEngine : INestingEngine
    {
        public int Calls;

        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress> progress = null,
            CancellationToken token = default)
        {
            Calls++;
            return new NestJobResult(NestJobStatus.Incomplete, NestJobStopReason.NoPlacementFound,
                Array.Empty<NestJobPlateResult>(),
                job.Parts.Select(p => new PartFulfillment(p.Id, p.Quantity, 0, p.Quantity)).ToList(),
                job.Plates.Select(s => new StockUsage(s.Id, 0, s.Quantity)).ToList());
        }
    }

    private static NestPlateStock WideRailStock(string id, ClampRail rail)
    {
        var size = new Size(3, 23);
        return new NestPlateStock(id, size, null, 0, default, 1, null, rail.ResolveFor(size, 1)) { ClampRail = rail };
    }

    // PM 0e655fdb: parts that cannot fit even with every clamp removed are dismissed by one
    // all-clamps-removed probe per stock instead of a search over every clamp run.
    [Fact]
    public void PartsThatNeverFitAreDismissedWithoutSearchingClampRuns()
    {
        var rail = new ClampRail(0.5, 0.625, Enumerable.Range(0, 20).Select(i => 0.5 + i * 1.1));
        var stocks = Enumerable.Range(0, 4).Select(i => WideRailStock($"option-{i}", rail)).ToArray();
        var parts = Enumerable.Range(0, 25).Select(i => JobBuilder.Rectangle($"p{i}", w: 30, h: 3, count: 1)).ToArray();
        var job = new NestJob(parts, stocks);
        var engine = new CountingNoFitEngine();
        var raw = engine.Solve(job);
        engine.Calls = 0;

        var outcome = ClampRemovalRecovery.TryRecover(job, raw, engine, default);

        Assert.Null(outcome);
        // One feasibility probe per part plus one all-clamps-removed probe per part and stock.
        Assert.True(engine.Calls <= 25 + 25 * 4, $"recovery made {engine.Calls} solves");
    }

    // PM 0e655fdb: the search has a fixed budget; running out keeps the ordinary result.
    [Fact]
    public void RecoveryStopsAtItsProbeBudget()
    {
        // 23-wide parts fit only with all 20 clamps off: every smaller run is probed first.
        var rail = new ClampRail(0.5, 0.625, Enumerable.Range(0, 20).Select(i => 0.5 + i * 1.1));
        var stocks = Enumerable.Range(0, 4).Select(i => WideRailStock($"option-{i}", rail)).ToArray();
        var parts = Enumerable.Range(0, 25).Select(i => JobBuilder.Rectangle($"p{i}", w: 23, h: 3, count: 1)).ToArray();
        var job = new NestJob(parts, stocks);
        var engine = new CountingNoFitEngine();
        var raw = engine.Solve(job);
        engine.Calls = 0;

        var outcome = ClampRemovalRecovery.TryRecover(job, raw, engine, default);

        Assert.Null(outcome);
        Assert.True(engine.Calls <= ClampRemovalRecovery.MaxProbeSolves, $"recovery made {engine.Calls} solves");
    }
    // PM 60f7a673: the removal is recorded whatever the empty target sheet already holds.
    [Theory]
    [InlineData("reduced-explicit")] // no rail, already exactly the solved reduced zones
    [InlineData("other-rail")]       // follows a different rail that cannot recreate them
    public void CommittedRemovalIsRecordedOnAnyEmptyTarget(string target)
    {
        var drawing = new Drawing("wide", TestDrawingFactory.Rectangle(14, 3));
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        nest.PlateDefaults.Size = new Size(3, 23);
        nest.PlateDefaults.EdgeSpacing = new Spacing(0, 0, 0, 0);
        var plate = nest.CreatePlate();
        if (target == "reduced-explicit")
            plate.ClampZones.Add(Zones()[1]);
        else
            plate.SetClampRail(new ClampRail(2, 0.625, new[] { 6.0, 21.0 }));
        using var manager = new PlateManager(nest);

        var result = NestPipeline.Run(Request(Stock(), Item("wide", 14, 3, 1)));
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        var applied = Assert.Single(NestPipelineCommit.ApplyToEmptyPlates(result, manager));

        Assert.Same(plate, applied);
        Assert.Same(Rail, applied.ClampRail);
        Assert.Equal(new[] { "clamp-1" }, applied.RemovedClamps);
        Assert.Equal(new[] { "clamp-2" }, applied.ClampZones.Select(z => z.Id));
        Assert.Equal(ClampRail.DefaultRemovalMinutes, applied.ClampRemovalMinutes);
    }
    // PM bf4438ac: when one offered stock runs out, a blocked part may still use another
    // offered stock that has sheets left (each with its own minimal removal).
    [Theory]
    [InlineData(2, false)] // two blocked copies: one on A, one on B
    [InlineData(1, true)]  // the ordinary part took A; the blocked part goes to B
    public void RecoveryMovesOnToAnotherStockWithSheetsLeft(int wide, bool small)
    {
        NestPlateStock Sheet(string id, double length)
        {
            var size = new Size(3, length);
            return new NestPlateStock(id, size, 1, 0, default, 1, null, Rail.ResolveFor(size, 1)) { ClampRail = Rail };
        }
        var items = new List<NestItem> { Item("wide", 14, 3, wide) };
        if (small)
            items.Add(Item("small", 2, 1, 1));
        var request = new NestPipelineRequest("Rectangles", items, new[] { Sheet("A", 23), Sheet("B", 24) },
            new NestJobOptions(maxPlates: 2));

        var result = NestPipeline.Run(request);

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, result.Raw.Plates.Count);
        Assert.All(result.Raw.Plates.Where(p => p.StockId.Contains("removed")), p => Assert.Single(p.Stock.ClampZones));
        Assert.Equal(2, result.Raw.StockUsage.Sum(u => u.Used));
    }
    // Delta review policy gate 3: identical offered stock (four 48x120 sheets with the same
    // eight-clamp rail) must not multiply the probe count, or a moderate job of full-sheet
    // parts runs out of budget although every part fits with its clamps removed.
    [Fact]
    public void IdenticalStockOptionsShareTheirProbes()
    {
        var rail = new ClampRail(1.5, 0.625, new[] { 6.0, 21.0, 36.0, 51.0, 66.0, 81.0, 96.0, 111.0 });
        var size = new Size(48, 120);
        var stocks = Enumerable.Range(0, 4)
            .Select(i => new NestPlateStock($"s{i}", size, null, 0, default, 1, null, rail.ResolveFor(size, 1)) { ClampRail = rail })
            .ToArray();
        var parts = Enumerable.Range(0, 8)
            .Select(i => JobBuilder.Rectangle($"full-{i}", w: 120, h: 48, count: 1, x: i * 1e-3))
            .ToArray();
        var job = new NestJob(parts, stocks);
        var engine = new OpenNest.Engine.NestingEngines.Default.DefaultNestingEngine();
        var raw = engine.Solve(job);
        Assert.Empty(raw.Plates);

        var outcome = ClampRemovalRecovery.TryRecover(job, raw, engine, default);

        Assert.NotNull(outcome);
        Assert.Equal(NestJobStatus.Complete, outcome!.Result.Status);
        Assert.Equal(8, outcome.Result.Plates.Count);
        Assert.All(outcome.Result.Plates, sheet => Assert.Empty(sheet.Stock.ClampZones));
    }
}
