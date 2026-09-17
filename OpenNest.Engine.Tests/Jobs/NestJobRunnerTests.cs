using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.Jobs;

public class NestJobRunnerTests
{
    [Fact]
    public void EmptyJobCompletesWithoutPlatesOrPlacementWork()
    {
        var fake = new FakePlateNester();
        var factoryCalls = 0;
        var runner = new NestJobRunner(_ => { factoryCalls++; return fake; });
        var job = new NestJob(Array.Empty<NestJobPart>(), new[]
        {
            new NestPlateStock("finite", new Size(100, 200), 2),
            new NestPlateStock("unlimited", new Size(100, 200))
        });

        var result = runner.Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(NestJobStopReason.Completed, result.StopReason);
        Assert.Empty(result.Plates);
        Assert.Empty(result.Fulfillment);
        Assert.Collection(result.StockUsage,
            usage => { Assert.Equal(0, usage.Used); Assert.Equal(2, usage.Remaining); },
            usage => { Assert.Equal(0, usage.Used); Assert.Null(usage.Remaining); });
        Assert.Equal(0, factoryCalls);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public void PreCancelledEmptyJobThrows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new NestJobRunner(_ => new FakePlateNester());
        var job = new NestJob(Array.Empty<NestJobPart>(), Array.Empty<NestPlateStock>());
        Assert.Throws<OperationCanceledException>(() => runner.Solve(job, token: cancellation.Token));
    }

    [Fact]
    public void NonemptyJobIsExplicitlyUnsupportedInContractSlice()
    {
        var part = new NestJobPart("part", PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle()), 1);
        var job = new NestJob(new[] { part }, Array.Empty<NestPlateStock>());
        var runner = new NestJobRunner(_ => new FakePlateNester());
        Assert.Throws<NotSupportedException>(() => runner.Solve(job));
    }

    [Fact]
    public void JobOwnsCollectionsSettingsAndExactGeometryIncludingHoleArc()
    {
        var program = TestDrawingFactory.Rectangle();
        program.MoveTo(3.123456789, 4);
        program.ArcTo(3.123456789, 4, 4, 4, RotationType.CW);
        var geometry = PartGeometrySnapshot.FromProgram(program);
        var parts = new List<NestJobPart> { new("p", geometry, 3) };
        var size = new Size(100, 200);
        var edges = new Spacing(1, 2, 3, 4);
        var stocks = new List<NestPlateStock> { new("s", size, 0, 2, edges, 3) };
        var job = new NestJob(parts, stocks);
        parts.Clear(); stocks.Clear(); program.Codes.Clear(); size.Width = 0; edges.Left = 999;

        Assert.Single(job.Parts);
        Assert.Equal(3, job.Parts[0].Quantity);
        Assert.Equal(7, geometry.Motions.Count);
        Assert.Equal(CodeType.RapidMove, geometry.Motions[5].Type);
        Assert.Equal(CodeType.ArcMove, geometry.Motions[6].Type);
        Assert.Equal(3.123456789, geometry.Motions[6].X);
        Assert.Equal(4, geometry.Motions[6].CenterX);
        Assert.Equal(RotationType.CW, geometry.Motions[6].Rotation);
        Assert.Equal(100, job.Plates[0].Size.Width);
        Assert.Equal(1, job.Plates[0].EdgeSpacing.Left);
        Assert.Equal(0, job.Plates[0].Quantity);
        Assert.Equal("Default", job.Options.PlacementStrategy);
        Assert.Throws<NotSupportedException>(() => ((IList<NestJobPart>)job.Parts).Clear());
    }

    [Fact]
    public void LegacyZeroStepMeansAutomaticNotFixed()
    {
        Assert.Equal(RotationPolicyKind.Automatic, RotationPolicy.FromLegacy(0, 1, 2).Kind);
        Assert.Equal(RotationPolicyKind.Fixed, RotationPolicy.Fixed(1).Kind);
        Assert.Equal(RotationPolicyKind.BoundedSweep, RotationPolicy.BoundedSweep(0, 1, 0.5).Kind);
    }

    private sealed class FakePlateNester : IPlateNester
    {
        public int Calls { get; private set; }
        public PlateCandidate Place(PlatePlacementRequest request, IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default)
        {
            Calls++;
            return new PlateCandidate(Array.Empty<NestJobPlacement>());
        }
    }
}
