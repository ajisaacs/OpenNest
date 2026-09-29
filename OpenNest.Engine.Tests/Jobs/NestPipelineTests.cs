using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestPipelineTests
{
    private static NestPlateStock Sheet() =>
        new("sheet", new Size(48, 96), quantity: null, partSpacing: 0.25);

    private static NestItem Item(string name, int quantity) =>
        new()
        {
            Drawing = new Drawing(name, TestDrawingFactory.Rectangle()),
            Quantity = quantity,
        };

    private static NestPipelineRequest Request(string engine, params NestItem[] items) =>
        new(engine, items, new[] { Sheet() });

    private static NestJobResult OnePlate(NestJob job, params NestJobPlacement[] placements) =>
        new(
            NestJobStatus.Complete,
            NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, job.Plates[0], placements) },
            job.Parts.Select(p => new PartFulfillment(p.Id, p.Quantity, p.Quantity, 0)),
            new[] { new StockUsage(job.Plates[0].Id, 1, null) }
        );

    private sealed class StubEngine(Func<NestJob, NestJobResult> solve) : INestingEngine
    {
        public NestJobResult Solve(
            NestJob job,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        ) => solve(job);
    }

    [Fact]
    public void RegisteredEngineResultIsValidatedAndBoundToCallerDrawings()
    {
        var item = Item("bracket", 10);
        var codes = item.Drawing.Program.Codes.Count;

        var result = NestPipeline.Run(Request("Default", item));

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.Equal(NestJobStatus.Complete, result.Status);
        var parts = result.Plates.SelectMany(p => p.Parts).ToList();
        Assert.Equal(10, parts.Count);
        Assert.All(parts, part => Assert.Same(item.Drawing, part.BaseDrawing));
        Assert.All(result.Plates, plate => Assert.Same(result.Job.Plates[0], plate.Stock));
        Assert.Equal(10, item.Quantity);
        Assert.Equal(codes, item.Drawing.Program.Codes.Count);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }

    [Fact]
    public void OverlappingEngineOutputIsReportedByDrawingNameWithoutThrowing()
    {
        var engine = new StubEngine(job =>
            OnePlate(
                job,
                new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0),
                new NestJobPlacement(job.Parts[0].Id, 1, 1, 1, 0)
            )
        );

        var result = NestPipeline.Run(engine, "Overlapper", Request("Overlapper", Item("bracket", 2)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("bracket") && v.Contains("spacing"));
        Assert.Equal(2, result.Plates.Single().Parts.Count);
    }

    [Fact]
    public void PlacementForUnknownRequirementIsAViolationNotSilentlyDropped()
    {
        var engine = new StubEngine(job =>
            OnePlate(
                job,
                new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0),
                new NestJobPlacement("ghost", 0, 30, 1, 0)
            )
        );

        var result = NestPipeline.Run(engine, "Ghost", Request("Ghost", Item("bracket", 1)));

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.Contains("ghost"));
        Assert.Single(result.Plates.Single().Parts);
    }

    [Fact]
    public void UnknownEngineNameListsRegisteredEngines()
    {
        var error = Assert.Throws<NotSupportedException>(() =>
            NestPipeline.Run(Request("Mystery Engine", Item("bracket", 1)))
        );

        Assert.Contains("Default", error.Message);
    }

    [Fact]
    public void CancellationPropagatesWithoutAResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            NestPipeline.Run(Request("Default", Item("bracket", 1)), null, cts.Token)
        );
    }
}
