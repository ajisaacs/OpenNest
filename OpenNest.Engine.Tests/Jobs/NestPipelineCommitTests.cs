using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestPipelineCommitTests
{
    private sealed class StubEngine(bool overlap) : INestingEngine
    {
        public NestJobResult Solve(NestJob job, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => new(
                NestJobStatus.Complete, NestJobStopReason.Completed,
                new[] { new NestJobPlateResult(0, job.Plates[0], new[]
                {
                    new NestJobPlacement(job.Parts[0].Id, 0, 2, 2, 0),
                    new NestJobPlacement(job.Parts[0].Id, 1, overlap ? 2 : 20, 2, 0),
                }) },
                new[] { new PartFulfillment(job.Parts[0].Id, 2, 2, 0) },
                new[] { new StockUsage(job.Plates[0].Id, 1, null) });
    }

    private static NestPipelineResult Result(Drawing drawing, bool overlap = false) =>
        NestPipeline.Run(new StubEngine(overlap), "stub", new NestPipelineRequest("stub",
            new[] { new NestItem { Drawing = drawing, Quantity = 2 } },
            new[] { new NestPlateStock("sheet", new Size(48, 96), null, 0.25, new Spacing(1, 1), 1) }));

    [Fact]
    public void AppliesValidatedSettingsToEmptyPlateAndNeverFillsOccupiedPlate()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var occupied = nest.CreatePlate();
        occupied.Parts.Add(new Part(drawing));
        var empty = nest.CreatePlate();
        empty.Quantity = 7;
        empty.Quadrant = 3;
        empty.Size = new Size(4, 8);
        empty.EdgeSpacing = new Spacing(0, 0);
        using var manager = new PlateManager(nest);
        var result = Result(drawing);
        Assert.True(result.IsValid, string.Join("; ", result.Violations));

        var applied = NestPipelineCommit.ApplyToEmptyPlates(result, manager);

        Assert.Same(empty, Assert.Single(applied));
        Assert.Single(occupied.Parts);
        Assert.Equal(2, empty.Parts.Count);
        Assert.Equal(1, empty.Quantity);
        Assert.Equal(1, empty.Quadrant);
        Assert.Equal(result.Job.Plates[0].Size, empty.Size);
        Assert.Equal(result.Job.Plates[0].EdgeSpacing, empty.EdgeSpacing);
        Assert.Equal(0.25, empty.PartSpacing);
        Assert.All(empty.Parts, p => Assert.Same(drawing, p.BaseDrawing));
        Assert.Equal(3, drawing.Quantity.Nested);
    }

    [Fact]
    public void InvalidResultRequiresExplicitConsentAndDiscardDoesNotMutateNest()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var nest = new Nest();
        nest.Drawings.Add(drawing);
        var empty = nest.CreatePlate();
        using var manager = new PlateManager(nest);
        var result = Result(drawing, overlap: true);
        Assert.False(result.IsValid);

        Assert.Throws<InvalidOperationException>(() => NestPipelineCommit.ApplyToEmptyPlates(result, manager));
        Assert.Same(empty, Assert.Single(nest.Plates));
        Assert.Empty(empty.Parts);
        Assert.Equal(0, drawing.Quantity.Nested);

        var applied = NestPipelineCommit.ApplyToEmptyPlates(result, manager, allowInvalid: true);
        Assert.Equal(2, Assert.Single(applied).Parts.Count);
    }

    [Fact]
    public void CancelledCommitDoesNotCreateAnyPlate()
    {
        var drawing = new Drawing("part", TestDrawingFactory.Rectangle());
        var nest = new Nest();
        using var manager = new PlateManager(nest);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            NestPipelineCommit.ApplyToEmptyPlates(Result(drawing), manager, token: cts.Token));
        Assert.Empty(nest.Plates);
    }
}
