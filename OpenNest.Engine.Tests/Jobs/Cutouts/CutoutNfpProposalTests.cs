using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Engine.Tests.NestingEngines;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

public class CutoutNfpProposalTests
{
    private const double Spacing = 0.25;

    [Fact]
    public void LargeInsertFitsRoundCutoutAndRemainsValidAfterFrameRotation()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1, RotationPolicy.Fixed(System.Math.PI / 2));
        var insert = JobBuilder.Rectangle("insert", 6, 6, 1, RotationPolicy.Fixed(0));
        var pose = CutoutNfpProposal.Find(frame, 0, insert, Spacing);
        Assert.NotNull(pose);
        Assert.Equal(insert.Id, pose.PartId);
        Assert.Equal(0, pose.InstanceIndex);
        Assert.True(insert.Rotation.Allows(pose.Rotation));
        AssertSquareInRoundHole(pose, 6, Spacing);

        var localFrame = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        Assert.True(NestLayoutCheck.Clears(JobPartGeometry.Read(frame.Geometry), localFrame,
            JobPartGeometry.Read(insert.Geometry), pose, Spacing));
        var framePose = new NestJobPlacement(frame.Id, 0, 11, 11, System.Math.PI / 2);
        var movedInsert = pose with
        {
            X = framePose.X - pose.Y,
            Y = framePose.Y + pose.X,
            Rotation = pose.Rotation + framePose.Rotation,
        };
        // This insert is fixed at zero for the local proposal; the rotated variant needs
        // a correspondingly legal global policy before it can be handed to a job.
        var rotatedInsert = JobBuilder.Rectangle("insert", 6, 6, 1, RotationPolicy.Fixed(System.Math.PI / 2));
        Assert.True(NestLayoutCheck.Clears(JobPartGeometry.Read(frame.Geometry), framePose,
            JobPartGeometry.Read(insert.Geometry), movedInsert, Spacing));
        AssertSquareInRoundHole(movedInsert, 6, Spacing, framePose.X, framePose.Y);
        var stock = JobBuilder.Stock("sheet", 22, 22, Spacing);
        var job = JobBuilder.Job(new[] { frame, rotatedInsert }, new[] { stock });
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, new[] { framePose, movedInsert }) },
            new[] { new PartFulfillment(frame.Id, 1, 1, 0), new PartFulfillment(insert.Id, 1, 1, 0) },
            new[] { new StockUsage(stock.Id, 1, null) });
        LayoutAssert.Valid(job, result);
    }

    [Fact]
    public void LargeInsertFitsSquareCutoutWithoutUsingFillLattice()
    {
        var program = TestShapes.Rectangle(20, 20);
        program.MoveTo(5, 5);
        program.LineTo(5, 15);
        program.LineTo(15, 15);
        program.LineTo(15, 5);
        program.LineTo(5, 5);
        var frame = JobBuilder.Part("square-frame", program, 1);
        var insert = JobBuilder.Rectangle("large", 9, 9, 1, RotationPolicy.Fixed(0));
        var pose = CutoutNfpProposal.Find(frame, 0, insert, Spacing);
        Assert.NotNull(pose);
        Assert.All(new[] { (0.0, 0.0), (9.0, 0.0), (9.0, 9.0), (0.0, 9.0) }, corner =>
        {
            Assert.InRange(pose.X + corner.Item1, 5 + Spacing, 15 - Spacing);
            Assert.InRange(pose.Y + corner.Item2, 5 + Spacing, 15 - Spacing);
        });
        Assert.True(NestLayoutCheck.Clears(JobPartGeometry.Read(frame.Geometry),
            new NestJobPlacement(frame.Id, 0, 0, 0, 0), JobPartGeometry.Read(insert.Geometry), pose, Spacing));
        Assert.Null(CutoutNfpProposal.Find(frame, 0, JobBuilder.Rectangle("oversized", 9.6, 9.6, 1),
            Spacing));
    }

    [Fact]
    public void ObliqueFixedInsertRotationIsUsedInInnerFit()
    {
        var program = TestShapes.Rectangle(20, 20);
        program.MoveTo(5, 5);
        program.LineTo(5, 15);
        program.LineTo(15, 15);
        program.LineTo(15, 5);
        program.LineTo(5, 5);
        var frame = JobBuilder.Part("frame", program, 1);
        var insert = JobBuilder.Rectangle("insert", 3, 2, 1, RotationPolicy.Fixed(System.Math.PI / 4));
        var pose = CutoutNfpProposal.Find(frame, 0, insert, Spacing);
        Assert.NotNull(pose);
        Assert.True(insert.Rotation.Allows(pose.Rotation));
        foreach (var (x, y) in new[] { (0.0, 0.0), (3.0, 0.0), (3.0, 2.0), (0.0, 2.0) })
        {
            var rx = pose.X + x * System.Math.Cos(pose.Rotation) - y * System.Math.Sin(pose.Rotation);
            var ry = pose.Y + x * System.Math.Sin(pose.Rotation) + y * System.Math.Cos(pose.Rotation);
            Assert.InRange(rx, 5 + Spacing, 15 - Spacing);
            Assert.InRange(ry, 5 + Spacing, 15 - Spacing);
        }
    }

    [Theory]
    [InlineData(9.51)]
    [InlineData(10.0)]
    [InlineData(9.6)]
    public void OversizedInsertIsNeverProposed(double side)
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);
        var insert = JobBuilder.Rectangle("insert", side, side, 1);
        Assert.Null(CutoutNfpProposal.Find(frame, 0, insert, Spacing));
    }

    [Fact]
    public void UnsupportedCoordinateScaleFailsClosed()
    {
        const double scale = 1e15;
        var program = TestShapes.Polyline((-scale, -scale), (scale, -scale),
            (scale, scale), (-scale, scale));
        program.MoveTo(-scale / 2, -scale / 2);
        program.LineTo(-scale / 2, scale / 2);
        program.LineTo(scale / 2, scale / 2);
        program.LineTo(scale / 2, -scale / 2);
        program.LineTo(-scale / 2, -scale / 2);
        Assert.Null(CutoutNfpProposal.Find(JobBuilder.Part("huge", program, 1), 0,
            JobBuilder.Rectangle("insert", 6, 6, 1), Spacing));
    }

    [Fact]
    public void ImpossibleHugeSpacingFailsBeforeAnyOffset()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);
        var insert = JobBuilder.Rectangle("insert", 6, 6, 1);
        Assert.Null(CutoutNfpProposal.Find(frame, 0, insert, 1e12));
    }

    private static void AssertSquareInRoundHole(NestJobPlacement pose, double side, double spacing,
        double centerX = 0, double centerY = 0)
    {
        var sin = System.Math.Sin(pose.Rotation);
        var cos = System.Math.Cos(pose.Rotation);
        foreach (var (x, y) in new[] { (0.0, 0.0), (side, 0.0), (side, side), (0.0, side) })
        {
            var dx = pose.X + x * cos - y * sin - centerX;
            var dy = pose.Y + x * sin + y * cos - centerY;
            Assert.True(System.Math.Sqrt(dx * dx + dy * dy) <= 5 - spacing + 0.00001,
                $"corner ({dx}, {dy}) does not clear the circular wall");
        }
    }

    [Fact]
    public void CancellationIsNotReportedAsNoFit()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);
        var insert = JobBuilder.Rectangle("insert", 6, 6, 1);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => CutoutNfpProposal.Find(frame, 0, insert, Spacing,
            cancelled.Token));
    }
}
