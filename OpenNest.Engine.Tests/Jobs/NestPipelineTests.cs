using OpenNest.CNC;
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

        var result = NestPipeline.Run(Request("Fill", item));

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        Assert.True(result.CanKeep);
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
        var item = Item("bracket", 2);
        var before = PartGeometrySnapshot.FromProgram(item.Drawing.Program).Motions;
        var engine = new StubEngine(job =>
            OnePlate(
                job,
                new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0),
                new NestJobPlacement(job.Parts[0].Id, 1, 1, 1, 0)
            )
        );

        var result = NestPipeline.Run(engine, "Overlapper", Request("Overlapper", item));

        Assert.False(result.IsValid);
        Assert.True(result.CanKeep);
        Assert.Contains(result.Violations, v => v.Contains("bracket") && v.Contains("spacing"));
        Assert.Equal(2, result.Plates.Single().Parts.Count);
        Assert.All(result.Plates.Single().Parts, part => Assert.Same(item.Drawing, part.BaseDrawing));
        Assert.Equal(before, PartGeometrySnapshot.FromProgram(item.Drawing.Program).Motions);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }

    [Theory]
    [InlineData("ghost")]
    [InlineData(null)]
    public void UnknownOrNullRequirementMakesTheEntireProposalNonKeepable(string? partId)
    {
        var item = Item("bracket", 1);
        var before = PartGeometrySnapshot.FromProgram(item.Drawing.Program).Motions;
        var engine = new StubEngine(job => new NestJobResult(
            NestJobStatus.Complete,
            NestJobStopReason.Completed,
            new[]
            {
                new NestJobPlateResult(0, job.Plates[0], new[] { new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0) }),
                new NestJobPlateResult(1, job.Plates[0], new[] { new NestJobPlacement(partId!, 0, 30, 1, 0) }),
            },
            Array.Empty<PartFulfillment>(),
            Array.Empty<StockUsage>()
        ));

        var result = NestPipeline.Run(engine, "Ghost", Request("Ghost", item));

        Assert.False(result.IsValid);
        Assert.False(result.CanKeep);
        Assert.Contains(result.Violations, v => v.Contains(partId ?? "null") && v.Contains("Plate 1"));
        Assert.Empty(result.Plates);
        Assert.Equal(2, result.Raw.Plates.Count);
        Assert.Equal(before, PartGeometrySnapshot.FromProgram(item.Drawing.Program).Motions);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }

    [Theory]
    [InlineData(double.NaN, 1, 0, "X")]
    [InlineData(double.PositiveInfinity, 1, 0, "X")]
    [InlineData(double.NegativeInfinity, 1, 0, "X")]
    [InlineData(1, double.NaN, 0, "Y")]
    [InlineData(1, double.PositiveInfinity, 0, "Y")]
    [InlineData(1, double.NegativeInfinity, 0, "Y")]
    [InlineData(1, 1, double.NaN, "Rotation")]
    [InlineData(1, 1, double.PositiveInfinity, "Rotation")]
    [InlineData(1, 1, double.NegativeInfinity, "Rotation")]
    public void NonfinitePoseIsReportedAndNeverProposed(double x, double y, double rotation, string field)
    {
        var item = Item("bracket", 2);
        var before = PartGeometrySnapshot.FromProgram(item.Drawing.Program).Motions;
        var engine = new StubEngine(job => OnePlate(job,
            new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0),
            new NestJobPlacement(job.Parts[0].Id, 1, x, y, rotation)));

        var result = NestPipeline.Run(engine, "Nonfinite", Request("Nonfinite", item));

        Assert.False(result.IsValid);
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains(field) && v.Contains("finite") && v.Contains("bracket"));
        Assert.Equal(before, PartGeometrySnapshot.FromProgram(item.Drawing.Program).Motions);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }

    [Fact]
    public void ReportsEveryStructuralViolationWithoutMaterializingAnyGeometry()
    {
        var engine = new StubEngine(job => OnePlate(job,
            new NestJobPlacement("ghost", 0, double.NaN, double.PositiveInfinity, double.NegativeInfinity),
            new NestJobPlacement(null!, 0, 1, 1, 0),
            new NestJobPlacement(job.Parts[0].Id, 0, double.NegativeInfinity, 1, 0)));

        var result = NestPipeline.Run(engine, "Malformed", Request("Malformed", Item("bracket", 1)));

        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Equal(6, result.Violations.Count);
        Assert.Contains(result.Violations, v => v.Contains("ghost") && v.Contains("not part of this job"));
        Assert.Contains(result.Violations, v => v.Contains("null") && v.Contains("not part of this job"));
        Assert.Equal(2, result.Violations.Count(v => v.Contains("nonfinite X")));
        Assert.Contains(result.Violations, v => v.Contains("nonfinite Y"));
        Assert.Contains(result.Violations, v => v.Contains("nonfinite Rotation"));
    }

    [Fact]
    public void ExceedingMaxPlatesIsInvalidButRemainsFullyKeepable()
    {
        var item = Item("bracket", 2);
        var request = Request("TooManySheets", item) with { Options = new NestJobOptions(maxPlates: 1) };
        var engine = new StubEngine(job => new NestJobResult(
            NestJobStatus.Complete,
            NestJobStopReason.Completed,
            Enumerable.Range(0, 2).Select(i => new NestJobPlateResult(i, job.Plates[0],
                new[] { new NestJobPlacement(job.Parts[0].Id, i, 1, 1, 0) })),
            new[] { new PartFulfillment(job.Parts[0].Id, 2, 2, 0) },
            new[] { new StockUsage(job.Plates[0].Id, 2, null) }
        ));

        var result = NestPipeline.Run(engine, "TooManySheets", request);

        Assert.False(result.IsValid);
        Assert.True(result.CanKeep);
        Assert.Contains(result.Violations, v => v.Contains("MaxPlates") && v.Contains("2") && v.Contains("1"));
        Assert.Equal(2, result.Plates.Count);
        Assert.All(result.Plates, plate => Assert.Same(item.Drawing, Assert.Single(plate.Parts).BaseDrawing));
        Assert.Equal(2, item.Quantity);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }

    [Fact]
    public void InvalidStockIsRejectedBeforeCallingAnArbitraryEngine()
    {
        var called = false;
        var engine = new StubEngine(job =>
        {
            called = true;
            return OnePlate(job);
        });
        var request = Request("Unchecked", Item("bracket", 1)) with
        {
            Stock = new[] { new NestPlateStock("bad", new Size(48, 96), partSpacing: double.NaN) },
        };

        var error = Assert.Throws<ArgumentException>(() => NestPipeline.Run(engine, "Unchecked", request));

        Assert.Contains("Invalid stock", error.Message);
        Assert.False(called);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidGeometryIsRejectedBeforeCallingAnArbitraryEngine(bool nonfinite)
    {
        var item = Item("bracket", 1);
        if (nonfinite)
            ((Motion)item.Drawing.Program.Codes[1]).EndPoint = new Vector(double.NaN, 0);
        else
            item.Drawing.Program.Codes.Clear();
        var called = false;
        var engine = new StubEngine(job =>
        {
            called = true;
            return OnePlate(job);
        });

        var error = Assert.Throws<ArgumentException>(() =>
            NestPipeline.Run(engine, "Unchecked", Request("Unchecked", item)));

        Assert.Contains("Geometry must contain finite motions", error.Message);
        Assert.False(called);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(0, item.Drawing.Quantity.Nested);
    }

    [Fact]
    public void UnknownEngineNameListsRegisteredEngines()
    {
        var error = Assert.Throws<NotSupportedException>(() =>
            NestPipeline.Run(Request("Mystery Engine", Item("bracket", 1)))
        );

        Assert.Contains("Fill", error.Message);
    }

    [Fact]
    public void CancellationDuringSolveDiscardsEvenAnEngineThatReturnsNormally()
    {
        using var cts = new CancellationTokenSource();
        var engine = new StubEngine(job =>
        {
            cts.Cancel();
            return OnePlate(job, new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0));
        });

        Assert.ThrowsAny<OperationCanceledException>(() =>
            NestPipeline.Run(engine, "IgnoresStop", Request("IgnoresStop", Item("bracket", 1)), null, cts.Token)
        );
    }

    [Fact]
    public void DrawingChangedDuringSolveCannotBindGeometryOtherThanTheValidatedSnapshot()
    {
        var item = Item("bracket", 1);
        var result = NestPipeline.Run(new StubEngine(job =>
        {
            item.Drawing.Program = TestDrawingFactory.Rectangle(100, 100);
            return OnePlate(job, new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0));
        }), "Mutator", Request("Mutator", item));
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("changed") && v.Contains("bracket"));
    }

    private sealed class TransientCloneMove(double x, double y) : LinearMove(x, y)
    {
        public override ICode Clone()
        {
            var before = EndPoint;
            EndPoint = new Vector(100, 100);
            try { return base.Clone(); }
            finally { EndPoint = before; }
        }
    }

    [Fact]
    public void BindingTransientDrawingEditCannotReturnOrCommitUncheckedGeometry()
    {
        var item = Item("bracket", 1);
        item.Drawing.Program.Codes[^1] = new TransientCloneMove(0, 0);
        var result = NestPipeline.Run(new StubEngine(job =>
            OnePlate(job, new NestJobPlacement(job.Parts[0].Id, 0, 1, 1, 0))),
            "Transient", Request("Transient", item));
        Assert.False(result.CanKeep);
        Assert.Empty(result.Plates);
        Assert.Contains(result.Violations, v => v.Contains("Bound part") && v.Contains("bracket"));
    }

    [Fact]
    public void CancellationPropagatesWithoutAResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            NestPipeline.Run(Request("Fill", Item("bracket", 1)), null, cts.Token)
        );
    }
}
