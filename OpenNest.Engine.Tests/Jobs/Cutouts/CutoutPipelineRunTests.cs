using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

/// <summary>Ordinary whole-job runs bundle parts into other parts' cutouts first, and fall back
/// to the ordinary solve whenever that bundled trial is refused or places less.</summary>
[Collection("Cutout pipeline registry")]
public class CutoutPipelineRunTests
{
    private static NestPipelineRequest Request(string engine, int sheets = 1) =>
        new(engine,
            new[] { new NestItem { Drawing = new Drawing("frame", TestShapes.Ring(20, 10)), Quantity = 1 },
                new NestItem { Drawing = new Drawing("insert", TestShapes.Rectangle(6, 6)), Quantity = 1 } },
            new[] { new NestPlateStock("sheet", new Size(22, 22), sheets, 0.25) },
            new NestJobOptions(maxPlates: sheets));

    private sealed class Stub(Func<NestJob, NestJobResult> solve) : INestingEngine
    {
        public List<NestJob> Jobs { get; } = new();

        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            Jobs.Add(job);
            return solve(job);
        }
    }

    private static bool IsTrial(NestJob job) => job.Parts.Any(p => p.Id.StartsWith("__cutout-proxy-"));

    private static NestJobResult Place(NestJob job, params (string Id, double X, double Y)[] poses)
    {
        var builder = new NestJobResultBuilder(job);
        if (poses.Length > 0)
            builder.AddSheet(job.Plates[0], poses.Select(p => (p.Id, p.X, p.Y, 0.0)));
        var complete = job.Parts.All(p => poses.Count(x => x.Id == p.Id) == p.Quantity);
        return builder.Build(complete ? NestJobStopReason.Completed : NestJobStopReason.NoPlacementFound);
    }

    [Theory]
    [InlineData("Irregular")]
    [InlineData("Default")]
    public void OrdinaryRunPlacesAnInsertInsideItsFrameThatTheOrdinarySolveCannotFit(string engine)
    {
        var request = Request(engine);
        var baseline = NestPipeline.RunWithoutCutouts(request);
        Assert.Equal(NestJobStatus.Incomplete, baseline.Status);

        var result = NestPipeline.Run(request);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Empty(result.RefusedCutoutTrial);
        var plate = Assert.Single(result.Plates);
        Assert.Equal(new[] { request.Items[1].Drawing, request.Items[0].Drawing },
            plate.Parts.Select(p => p.BaseDrawing));
    }

    [Fact]
    public void RefusedTrialFallsBackToTheOrdinarySolveAndSaysWhy()
    {
        var request = Request("stub");
        var engine = new Stub(job => IsTrial(job)
            // Forged: the proxy is placed with a quarter turn its fixed rotation forbids.
            ? new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
                new[] { new NestJobPlateResult(0, job.Plates[0], new[] {
                    new NestJobPlacement(job.Parts[0].Id, 0, 11, 11, System.Math.PI / 2) }) },
                job.Parts.Select(p => new PartFulfillment(p.Id, 1, 1, 0)),
                new[] { new StockUsage("sheet", 1, 0) })
            : Place(job, ("part-0", 11, 11)));
        var result = NestPipeline.Run(engine, "stub", request);
        Assert.Equal(2, engine.Jobs.Count);
        Assert.True(IsTrial(engine.Jobs[0]));
        Assert.False(IsTrial(engine.Jobs[1]));
        Assert.True(result.CanKeep);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Contains(result.RefusedCutoutTrial, v => v.Contains("rotation constraint"));
        Assert.Equal(new[] { "part-0" }, Assert.Single(result.Raw.Plates).Placements.Select(p => p.PartId));
    }

    [Fact]
    public void IncompleteTrialYieldsToAnOrdinarySolveThatPlacesMore()
    {
        var request = Request("stub");
        request.Items[1].Quantity = 2;
        request = request with { Stock = new[] { new NestPlateStock("sheet", new Size(22, 40), 1, 0.25) } };
        // The trial leaves its proxy (and the reserved insert) unplaced; the ordinary solve does not.
        var engine = new Stub(job => IsTrial(job)
            ? Place(job, ("part-1", 30, 1))
            : Place(job, ("part-0", 11, 11), ("part-1", 23, 1), ("part-1", 30, 1)));
        var result = NestPipeline.Run(engine, "stub", request);
        Assert.Equal(2, engine.Jobs.Count);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Empty(result.RefusedCutoutTrial);
        Assert.Equal(3, Assert.Single(result.Plates).Parts.Count);
    }

    [Fact]
    public void IncompleteTrialThatPlacesAtLeastAsMuchIsKept()
    {
        var request = Request("stub");
        request.Items[0].Quantity = 2;
        request.Items[1].Quantity = 2;
        request = request with { Stock = new[] { new NestPlateStock("sheet", new Size(22, 22), 1, 0.25) } };
        var engine = new Stub(job => IsTrial(job)
            ? Place(job, (job.Parts.First(IsProxy).Id, 11, 11))
            : Place(job, ("part-0", 11, 11)));
        var result = NestPipeline.Run(engine, "stub", request);
        Assert.Equal(2, engine.Jobs.Count);
        Assert.True(result.CanKeep);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(new[] { "part-1", "part-0" },
            Assert.Single(result.Raw.Plates).Placements.Select(p => p.PartId));
        Assert.All(result.Raw.Fulfillment, f => Assert.Equal((2, 1, 1), (f.Requested, f.Placed, f.Unplaced)));

        static bool IsProxy(NestJobPart part) => part.Id.StartsWith("__cutout-proxy-");
    }

    [Fact]
    public void StopDuringTheTrialNeverStartsTheOrdinarySolve()
    {
        using var cts = new CancellationTokenSource();
        var engine = new Stub(job =>
        {
            cts.Cancel();
            return Place(job);
        });
        Assert.Throws<OperationCanceledException>(() =>
            NestPipeline.Run(engine, "stub", Request("stub"), token: cts.Token));
        Assert.True(IsTrial(Assert.Single(engine.Jobs)));
    }
}
