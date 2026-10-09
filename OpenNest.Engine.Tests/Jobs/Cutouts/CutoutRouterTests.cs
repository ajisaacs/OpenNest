using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Engine.Tests.NestingEngines;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

public class CutoutRouterTests
{
    private static NestJobPart Frame()
    {
        var program = TestShapes.Rectangle(20, 20);
        program.MoveTo(5, 5);
        program.LineTo(5, 15);
        program.LineTo(15, 15);
        program.LineTo(15, 5);
        program.LineTo(5, 5);
        return JobBuilder.Part("frame", program, 1);
    }

    [Fact]
    public void SmallLatticeAndLargeResidualKeepTheirOriginalRequirementIds()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);
        var small = JobBuilder.Part("small", TestShapes.Rectangle(1, 1), 3);
        var large = JobBuilder.Rectangle("large", 3, 3, 1, RotationPolicy.Fixed(0));
        Assert.Equal(3, CutoutLatticeFill.Fill(frame, 0, small, 3, 0.25).Count);
        var poses = CutoutRouter.Fill(frame, 0, new[] { small, large }, 0.25);
        Assert.Equal(3, poses.Count(p => p.PartId == small.Id));
        Assert.Single(poses, p => p.PartId == large.Id);
        Assert.Equal(new[] { 0, 1, 2 }, poses.Where(p => p.PartId == small.Id).Select(p => p.InstanceIndex));
        AssertPhysical(frame, new[] { small, large }, poses);
    }

    [Fact]
    public void ImpossibleRemainderNeverDisplacesCertifiedCopies()
    {
        var frame = Frame();
        var small = JobBuilder.Rectangle("small", 1, 1, 3, RotationPolicy.Fixed(0));
        var impossible = JobBuilder.Rectangle("impossible", 9.6, 9.6, 1, RotationPolicy.Fixed(0));
        var alone = CutoutRouter.Fill(frame, 0, new[] { small }, 0.25);
        var together = CutoutRouter.Fill(frame, 0, new[] { small, impossible }, 0.25);
        Assert.Equal(alone, together);
        Assert.Equal(3, together.Count);
        AssertPhysical(frame, new[] { small, impossible }, together);
    }

    [Fact]
    public void PriorCopiesAndQuantityCannotBeBypassedByResidualSearch()
    {
        var frame = Frame();
        var small = JobBuilder.Rectangle("small", 2, 2, 4, RotationPolicy.Fixed(0));
        var large = JobBuilder.Rectangle("large", 7, 7, 1, RotationPolicy.Fixed(0));
        var poses = CutoutRouter.Fill(frame, 0, new[] { small, large }, 0.25);
        Assert.InRange(poses.Count(p => p.PartId == small.Id), 0, small.Quantity);
        Assert.InRange(poses.Count(p => p.PartId == large.Id), 0, large.Quantity);
        AssertPhysical(frame, new[] { small, large }, poses);
    }

    [Fact]
    public void ResidualNfpDoesNotOverlapTheFirstLargeInsert()
    {
        var frame = Frame();
        var insert = JobBuilder.Rectangle("large", 6, 6, 2, RotationPolicy.Fixed(0));
        var poses = CutoutRouter.Fill(frame, 0, new[] { insert }, 0.25);
        Assert.Single(poses);
        AssertPhysical(frame, new[] { insert }, poses);
    }

    [Fact]
    public void DuplicateRequirementIdsAreRejectedBeforePlacement()
    {
        var frame = Frame();
        var insert = JobBuilder.Rectangle("same", 1, 1, 3);
        Assert.Throws<ArgumentException>(() => CutoutRouter.Fill(frame, 0,
            new[] { insert, insert }, 0.25));
    }

    [Fact]
    public void OccupiedAwareLatticeFillsAfterAnEarlierCopyBlocksItsFirstPose()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);
        var first = JobBuilder.Part("first", TestShapes.Rectangle(1, 1), 1);
        var more = JobBuilder.Part("more", TestShapes.Rectangle(1, 1), 3);
        var early = CutoutLatticeFill.Fill(frame, 0, more, 3, 0.25);
        Assert.Equal(3, early.Count);
        var blocked = early[0] with { PartId = first.Id };
        var occupied = new[] { (JobPartGeometry.Read(first.Geometry), blocked) };
        var later = CutoutLatticeFill.Fill(frame, 0, more, 3, 0.25,
            CutoutLatticeFill.DefaultShiftSteps, occupied, default);
        Assert.Equal(3, later.Count);
        Assert.All(later, p => Assert.True(NestLayoutCheck.Clears(occupied[0].Item1, blocked,
            JobPartGeometry.Read(more.Geometry), p, 0.25)));
    }

    [Fact]
    public void TinyInsertDoesNotAllocateAnUnboundedLattice()
    {
        var frame = Frame();
        var tiny = JobBuilder.Rectangle("tiny", 0.001, 0.001, 3, RotationPolicy.Fixed(0));
        Assert.Empty(CutoutLatticeFill.Fill(frame, 0, tiny, 3, 0));
        var poses = CutoutRouter.Fill(frame, 0, new[] { tiny }, 0);
        Assert.Equal(3, poses.Count);
    }

    [Fact]
    public void CancellationIsNotNoFit()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => CutoutRouter.Fill(Frame(), 0,
            new[] { JobBuilder.Rectangle("small", 1, 1, 3) }, 0.25, cts.Token));
    }

    private static void AssertPhysical(NestJobPart frame, NestJobPart[] inserts,
        IReadOnlyList<NestJobPlacement> poses)
    {
        var stock = JobBuilder.Stock("sheet", 22, 22, 0.25);
        var job = JobBuilder.Job(new[] { frame }.Concat(inserts).ToArray(), new[] { stock });
        var frameBounds = JobPartGeometry.Read(frame.Geometry).Bounds;
        var offsetX = 1 - frameBounds.Left;
        var offsetY = 1 - frameBounds.Bottom;
        var placements = new[] { new NestJobPlacement(frame.Id, 0, offsetX, offsetY, 0) }
            .Concat(poses.Select(p => p with { X = p.X + offsetX, Y = p.Y + offsetY })).ToArray();
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, placements) },
            new[] { new PartFulfillment(frame.Id, 1, 1, 0) }
                .Concat(inserts.Select(part => new PartFulfillment(part.Id, part.Quantity,
                    poses.Count(p => p.PartId == part.Id), part.Quantity - poses.Count(p => p.PartId == part.Id))))
                .ToArray(),
            new[] { new StockUsage(stock.Id, 1, null) });
        LayoutAssert.Valid(job, result);
        var frameGeometry = JobPartGeometry.Read(frame.Geometry);
        var framePose = new NestJobPlacement(frame.Id, 0, 0, 0, 0);
        foreach (var pose in poses)
        {
            var geometry = JobPartGeometry.Read(inserts.Single(p => p.Id == pose.PartId).Geometry);
            Assert.True(NestLayoutCheck.Clears(frameGeometry, framePose, geometry, pose, 0.25));
            // Independent square-wall oracle: every corner of each rectangular insert clears the hole.
            var bounds = geometry.Bounds;
            foreach (var (x, y) in new[] { (bounds.Left, bounds.Bottom), (bounds.Right, bounds.Bottom),
                (bounds.Right, bounds.Top), (bounds.Left, bounds.Top) })
            {
                var rx = pose.X + x * System.Math.Cos(pose.Rotation) - y * System.Math.Sin(pose.Rotation);
                var ry = pose.Y + x * System.Math.Sin(pose.Rotation) + y * System.Math.Cos(pose.Rotation);
                if (frameBounds.Left >= 0)
                {
                    Assert.InRange(rx, 5.25, 14.75);
                    Assert.InRange(ry, 5.25, 14.75);
                }
                else
                    Assert.True(System.Math.Sqrt(rx * rx + ry * ry) <= 4.75 + 0.00001);
            }
        }
    }
}
