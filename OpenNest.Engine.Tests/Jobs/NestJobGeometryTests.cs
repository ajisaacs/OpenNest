using OpenNest.Geometry;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;

namespace OpenNest.Engine.Tests.Jobs;

public class NestJobGeometryTests
{
    [Fact]
    public void CandidateOutsideUsableWorkAreaFailsWithoutMutatingInput()
    {
        var part = new NestJobPart(
            "part",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 3)),
            1
        );
        var sourceGeometry = part.Geometry.Motions.ToArray();
        var stock = new NestPlateStock(
            "stock",
            new Size(20, 30),
            1,
            edgeSpacing: new Spacing(2, 1, 3, 4)
        );
        var job = new NestJob(new[] { part }, new[] { stock });
        var runner = new NestJobRunner(_ => new CandidateNester(
            new[] { new NestJobPlacement("part", 0, 26, 1, 0) }
        ));

        Assert.Throws<InvalidOperationException>(() => runner.Solve(job));

        Assert.Equal(sourceGeometry, job.Parts[0].Geometry.Motions);
        Assert.Equal(1, job.Parts[0].Quantity);
        Assert.Equal(1, job.Plates[0].Quantity);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, -11, 0)]
    [InlineData(3, -11, -7)]
    [InlineData(4, 0, -7)]
    public void UnequalRectanglesFitAtEachQuadrantsUsableOrigin(int quadrant, double x, double y)
    {
        var part = new NestJobPart(
            "part",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(2, 3)),
            1
        );
        var stock = new NestPlateStock("stock", new Size(7, 11), 1, quadrant: quadrant);
        var job = new NestJob(new[] { part }, new[] { stock });

        var result = Solve(job, new NestJobPlacement("part", 0, x, y, 0));

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(
            new NestJobPlacement("part", 0, x, y, 0),
            Assert.Single(result.Plates[0].Placements)
        );
    }

    [Fact]
    public void FixedAndBoundedRotationPoliciesRejectDisallowedAngles()
    {
        var fixedPart = new NestJobPart(
            "part",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 2)),
            1,
            rotation: RotationPolicy.Fixed(System.Math.PI / 2)
        );
        var fixedJob = new NestJob(
            new[] { fixedPart },
            new[] { new NestPlateStock("stock", new Size(10, 10), 1) }
        );
        Assert.Throws<InvalidOperationException>(() =>
            Solve(fixedJob, new NestJobPlacement("part", 0, 0, 0, 0))
        );

        var fixedResult = Solve(
            fixedJob,
            new NestJobPlacement("part", 0, 2, 0, System.Math.PI / 2)
        );
        Assert.Equal(NestJobStatus.Complete, fixedResult.Status);

        var boundedPart = new NestJobPart(
            "part",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 2)),
            1,
            rotation: RotationPolicy.BoundedSweep(0, System.Math.PI / 2, System.Math.PI / 4)
        );
        var boundedJob = new NestJob(
            new[] { boundedPart },
            new[] { new NestPlateStock("stock", new Size(10, 10), 1) }
        );
        Assert.Throws<InvalidOperationException>(() =>
            Solve(boundedJob, new NestJobPlacement("part", 0, 2, 0, System.Math.PI / 3))
        );

        var boundedResult = Solve(
            boundedJob,
            new NestJobPlacement("part", 0, 2, 0, System.Math.PI / 4)
        );
        Assert.Equal(NestJobStatus.Complete, boundedResult.Status);
    }

    [Fact]
    public void EdgeTouchingIsAllowedAtZeroSpacingAndRejectedAtPositiveSpacing()
    {
        var part = new NestJobPart(
            "part",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(2, 2)),
            2
        );
        var touching = new[]
        {
            new NestJobPlacement("part", 0, 0, 0, 0),
            new NestJobPlacement("part", 1, 2, 0, 0),
        };
        var zeroSpacing = new NestJob(
            new[] { part },
            new[] { new NestPlateStock("stock", new Size(10, 10), 1) }
        );
        var positiveSpacing = new NestJob(
            new[] { part },
            new[] { new NestPlateStock("stock", new Size(10, 10), 1, 0.1) }
        );

        Assert.Equal(NestJobStatus.Complete, Solve(zeroSpacing, touching).Status);
        Assert.Throws<InvalidOperationException>(() => Solve(positiveSpacing, touching));
    }

    [Fact]
    public void OverlapAndContainmentAreRejected()
    {
        var outer = new NestJobPart(
            "outer",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(6, 6)),
            1
        );
        var inner = new NestJobPart(
            "inner",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(2, 2)),
            1
        );
        var job = new NestJob(
            new[] { outer, inner },
            new[] { new NestPlateStock("stock", new Size(20, 20), 1) }
        );

        Assert.Throws<InvalidOperationException>(() =>
            Solve(
                job,
                new NestJobPlacement("outer", 0, 0, 0, 0),
                new NestJobPlacement("inner", 0, 2, 2, 0)
            )
        );
    }

    [Fact]
    public void EmptyStockStopsWithoutCallingCandidateNester()
    {
        var nester = new CountingNester();
        var part = new NestJobPart(
            "part",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(2, 2)),
            1
        );
        var job = new NestJob(new[] { part }, Array.Empty<NestPlateStock>());

        var result = new NestJobRunner(_ => nester).Solve(job);

        Assert.Equal(NestJobStopReason.StockExhausted, result.StopReason);
        Assert.Equal(0, nester.Calls);
    }

    [Theory]
    [InlineData("Default")]
    [InlineData("Strip")]
    public void RealEngineSmokeCasesPreserveInputAndProduceSafeAccounting(string strategy)
    {
        var drawing = new Drawing("generated rectangle", TestDrawingFactory.Rectangle(6, 4));
        var part = DrawingJobMapper.FromDrawing("part", drawing, 3);
        var sourceGeometry = part.Geometry.Motions.ToArray();
        var stock = new NestPlateStock("stock", new Size(30, 50), 1, 1, new Spacing(1, 1, 1, 1));
        var job = new NestJob(new[] { part }, new[] { stock }, new NestJobOptions(strategy));

        var result = new NestJobRunner(PlateNesterFactory.Create).Solve(job);
        var materialized = NestResultMaterializer.Materialize(job, result);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.All(
            result.Fulfillment,
            fulfillment =>
                Assert.Equal(fulfillment.Requested, fulfillment.Placed + fulfillment.Unplaced)
        );
        Assert.Equal(sourceGeometry, job.Parts[0].Geometry.Motions);
        Assert.Equal(3, job.Parts[0].Quantity);
        Assert.Equal(1, job.Plates[0].Quantity);
        Assert.All(
            materialized.Nest.Plates,
            plate =>
            {
                var workArea = plate.WorkArea();
                Assert.All(
                    plate.Parts,
                    placed =>
                    {
                        Assert.True(placed.BoundingBox.Left >= workArea.Left - 1e-6);
                        Assert.True(placed.BoundingBox.Right <= workArea.Right + 1e-6);
                        Assert.True(placed.BoundingBox.Bottom >= workArea.Bottom - 1e-6);
                        Assert.True(placed.BoundingBox.Top <= workArea.Top + 1e-6);
                    }
                );
                for (var left = 0; left < plate.Parts.Count; left++)
                for (var right = left + 1; right < plate.Parts.Count; right++)
                    Assert.False(plate.Parts[left].Intersects(plate.Parts[right], out _));
            }
        );
    }

    private static NestJobResult Solve(NestJob job, params NestJobPlacement[] placements) =>
        new NestJobRunner(_ => new CandidateNester(placements)).Solve(job);

    private sealed class CandidateNester(IEnumerable<NestJobPlacement> placements) : IPlateNester
    {
        public PlateCandidate Place(
            PlatePlacementRequest request,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        ) => new(placements);
    }

    private sealed class CountingNester : IPlateNester
    {
        public int Calls { get; private set; }

        public PlateCandidate Place(
            PlatePlacementRequest request,
            IProgress<NestJobProgress>? progress = null,
            CancellationToken token = default
        )
        {
            Calls++;
            return new PlateCandidate(Array.Empty<NestJobPlacement>());
        }
    }
}
