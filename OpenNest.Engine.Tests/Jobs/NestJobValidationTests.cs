using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestJobValidationTests
{
    [Fact]
    public void IncrementalContoursUseAccumulatedCoordinates()
    {
        var program = new Program(Mode.Incremental);
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        program.LineTo(0, 3);
        program.LineTo(-4, 0);
        program.LineTo(0, -3);
        var job = new NestJob(new[]
        {
            new NestJobPart("part", PartGeometrySnapshot.FromProgram(program), 1)
        }, new[] { new NestPlateStock("stock", new Size(10, 10), 1) });

        var result = Solve(job, new NestJobPlacement("part", 0, 0, 0, 0));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(new PartFulfillment("part", 1, 1, 0), Assert.Single(result.Fulfillment));
    }

    [Fact]
    public void CandidateInsideAnotherRequirementsHoleDoesNotOverlapMaterial()
    {
        var outer = new NestJobPart("outer", PartGeometrySnapshot.FromProgram(RectangleWithHole()), 1);
        var inner = new NestJobPart("inner", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(2, 2)), 1);
        var job = new NestJob(new[] { outer, inner }, new[] { new NestPlateStock("stock", new Size(20, 20), 1) });

        var result = Solve(job,
            new NestJobPlacement("outer", 0, 0, 0, 0),
            new NestJobPlacement("inner", 0, 4, 4, 0));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Single(result.Plates);
        Assert.Equal(2, result.Plates[0].Placements.Count);
    }

    [Fact]
    public void SmallCornerOverlapIsRejected()
    {
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(10, 10)), 2);
        var job = new NestJob(new[] { part }, new[] { new NestPlateStock("stock", new Size(20, 20), 1) });

        Assert.Throws<InvalidOperationException>(() => Solve(job,
            new NestJobPlacement("part", 0, 0, 0, 0),
            new NestJobPlacement("part", 1, 9, 9, 0)));
    }

    [Theory]
    [InlineData(10.0, 0.0)]
    [InlineData(10.0, 10.0)]
    public void BoundaryContactWithZeroSpacingIsAccepted(double x, double y)
    {
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(10, 10)), 2);
        var job = new NestJob(new[] { part }, new[] { new NestPlateStock("stock", new Size(20, 20), 1) });

        var result = Solve(job,
            new NestJobPlacement("part", 0, 0, 0, 0),
            new NestJobPlacement("part", 1, x, y, 0));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, Assert.Single(result.Plates).Placements.Count);
    }

    [Fact]
    public void UnknownOrOverproducingCandidateFailsBeforeCommitWithoutChangingInput()
    {
        var reports = new List<NestJobProgress>();
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(2, 2)), 1);
        var stock = new NestPlateStock("stock", new Size(20, 20), 1);
        var job = new NestJob(new[] { part }, new[] { stock });
        var runner = new NestJobRunner(_ => new CandidateNester(new[]
        {
            new NestJobPlacement("part", 0, 0, 0, 0),
            new NestJobPlacement("unknown", 0, 4, 0, 0)
        }));

        Assert.Throws<InvalidOperationException>(() => runner.Solve(job, new InlineProgress(reports.Add)));

        Assert.Equal(1, job.Parts[0].Quantity);
        Assert.Equal(1, job.Plates[0].Quantity);
        Assert.DoesNotContain(reports, report => report.Stage == NestJobStage.PlateCommitted);
    }

    [Fact]
    public void CandidateThatOverproducesIsRejectedRatherThanClamped()
    {
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(2, 2)), 1);
        var job = new NestJob(new[] { part }, new[] { new NestPlateStock("stock", new Size(20, 20), 1) });

        Assert.Throws<InvalidOperationException>(() => Solve(job,
            new NestJobPlacement("part", 0, 0, 0, 0),
            new NestJobPlacement("part", 1, 4, 0, 0)));

        Assert.Equal(1, job.Parts[0].Quantity);
        Assert.Equal(1, job.Plates[0].Quantity);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpenOrZeroLengthContoursAreRejected(bool zeroLength)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(4, 0);
        if (zeroLength) program.LineTo(4, 0);
        program.LineTo(4, 3);
        program.LineTo(0, 3);
        if (zeroLength) program.LineTo(0, 0);
        var job = new NestJob(new[] { new NestJobPart("part", PartGeometrySnapshot.FromProgram(program), 1) },
            new[] { new NestPlateStock("stock", new Size(20, 20), 1) });

        Assert.Throws<ArgumentException>(() => new NestJobRunner(_ => new CandidateNester(Array.Empty<NestJobPlacement>())).Solve(job));
    }

    private static NestJobResult Solve(NestJob job, params NestJobPlacement[] placements) =>
        new NestJobRunner(_ => new CandidateNester(placements)).Solve(job);

    private static Program RectangleWithHole()
    {
        var program = TestDrawingFactory.Rectangle(10, 10);
        program.MoveTo(3, 3);
        program.LineTo(3, 7);
        program.LineTo(7, 7);
        program.LineTo(7, 3);
        program.LineTo(3, 3);
        return program;
    }

    private sealed class CandidateNester(IEnumerable<NestJobPlacement> placements) : IPlateNester
    {
        public PlateCandidate Place(PlatePlacementRequest request, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default) => new(placements);
    }

    private sealed class InlineProgress(Action<NestJobProgress> report) : IProgress<NestJobProgress>
    {
        public void Report(NestJobProgress value) => report(value);
    }
}
