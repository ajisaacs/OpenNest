using OpenNest.Engine.Jobs;
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

    private static NestPlateStock Stock(int? quantity = null) =>
        JobBuilder.Stock("sheet", width: 3, length: 23, quantity: quantity, clampZones: Zones());

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
}
