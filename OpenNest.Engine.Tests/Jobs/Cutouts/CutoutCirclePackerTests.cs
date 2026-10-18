using OpenNest.Converters;
using OpenNest.Engine.CirclePacking;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Cutouts;
using OpenNest.Engine.Tests.NestingEngines;
using OpenNest.Geometry;
using TestShapes = OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.Jobs.Cutouts;

public class CutoutCirclePackerTests
{
    private const double Spacing = 0.25;

    [Fact]
    public void SingleInsertSitsConcentric()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);
        var insert = JobBuilder.Part("disc", TestShapes.Disc(3), 1);

        var pose = Assert.Single(CutoutCirclePacker.Fill(frame, 0, new[] { insert }, Spacing));

        Assert.Equal(0, pose.X, 6);
        Assert.Equal(0, pose.Y, 6);
        AssertPhysical(frame, new[] { (insert, new Vector()) }, new[] { pose });
    }

    // Optimal container-to-circle radius ratios for n equal circles; all are rings, with a
    // center circle for n = 7..9. Six is left out: its ratio already holds seven.
    [Theory]
    [InlineData(2, 2.0)]
    [InlineData(3, 2.1547)]
    [InlineData(4, 2.4143)]
    [InlineData(5, 2.7014)]
    [InlineData(7, 3.0)]
    [InlineData(8, 3.3049)]
    [InlineData(9, 3.6132)]
    public void EqualDiscsReachTheKnownOptimalCount(int n, double ratio)
    {
        var insert = JobBuilder.Part("disc", TestShapes.Disc(1), n + 3);
        var radius = Item.Create(JobPartGeometry.Read(insert.Geometry)).Radius;
        var gap = Spacing + CutoutCirclePacker.Margin;
        // Container and circles grown by half the gap, so the ratio applies to them directly.
        var hole = ratio * (radius + gap / 2) - gap / 2 + gap + Item.FlattenAllowance + 0.001;
        var frame = JobBuilder.Part("frame", TestShapes.Ring(2 * hole + 4, 2 * hole), 1);

        var poses = CutoutCirclePacker.Fill(frame, 0, new[] { insert }, Spacing);

        Assert.Equal(n, poses.Count);
        Assert.Equal(Enumerable.Range(0, n), poses.Select(p => p.InstanceIndex));
        AssertPhysical(frame, new[] { (insert, new Vector()) }, poses);
    }

    [Fact]
    public void MixedSizesFillTheGapsLargestFirst()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(40, 30), 1);
        var large = JobBuilder.Part("large", TestShapes.Disc(4), 6);
        var small = JobBuilder.Part("small", TestShapes.Disc(1), 20);

        var poses = CutoutCirclePacker.Fill(frame, 0, new[] { small, large }, Spacing);

        Assert.Equal(large.Quantity, poses.Count(p => p.PartId == large.Id));
        Assert.True(poses.Count(p => p.PartId == small.Id) >= 10);
        AssertPhysical(frame, new[] { (small, new Vector()), (large, new Vector()) }, poses);
    }

    [Fact]
    public void RotatedOffCenterInsertMapsItsCenterIntoTheHole()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);
        var insert = JobBuilder.Part("disc", FromEntities(new Circle(50, 20, 2)), 2,
            RotationPolicy.Fixed(System.Math.PI / 2));

        var poses = CutoutCirclePacker.Fill(frame, 0, new[] { insert }, Spacing);

        Assert.Equal(2, poses.Count);
        Assert.All(poses, p => Assert.Equal(System.Math.PI / 2, p.Rotation, 9));
        AssertPhysical(frame, new[] { (insert, new Vector(50, 20)) }, poses);
    }

    [Fact]
    public void RouterTakesACompleteCirclePacking()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(30, 20), 1);
        var insert = JobBuilder.Part("disc", TestShapes.Disc(3), 7);
        var circles = CutoutCirclePacker.Fill(frame, 0, new[] { insert }, Spacing);
        var origins = new List<bool>();

        var poses = CutoutRouter.Fill(frame, 0, new[] { insert }, Spacing, (_, lattice) => origins.Add(lattice));

        // Ratio 10 / 3.25 > 3: six around one in the center.
        Assert.Equal(7, circles.Count);
        Assert.Equal(circles, poses);
        Assert.All(origins, Assert.False);
    }

    [Fact]
    public void SquareHolesAndSquareInsertsAreNotRound()
    {
        var square = JobPartGeometry.Read(JobBuilder.Part("frame", Square(20, 10), 1).Geometry);
        var ring = JobPartGeometry.Read(JobBuilder.Part("ring", TestShapes.Ring(20, 10), 1).Geometry);
        var disc = JobBuilder.Part("disc", TestShapes.Disc(2), 1);

        Assert.Null(CutoutCirclePacker.RoundHole(square.Cutouts[0]));
        Assert.False(CutoutCirclePacker.Applies(square.Cutouts[0], new[] { disc }));
        Assert.True(CutoutCirclePacker.Applies(ring.Cutouts[0], new[] { disc }));
        Assert.False(CutoutCirclePacker.Applies(ring.Cutouts[0],
            new[] { disc, JobBuilder.Rectangle("square", 2, 2, 1) }));
        Assert.Empty(CutoutCirclePacker.Fill(JobBuilder.Part("frame", Square(20, 10), 1), 0,
            new[] { disc }, Spacing));
    }

    [Fact]
    public void InsertLargerThanTheHolePlacesNothing()
    {
        var frame = JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1);

        Assert.Empty(CutoutCirclePacker.Fill(frame, 0,
            new[] { JobBuilder.Part("disc", TestShapes.Disc(4.8), 1) }, Spacing));
    }

    [Fact]
    public void CancellationIsNotNoFit()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => CutoutCirclePacker.Fill(
            JobBuilder.Part("frame", TestShapes.Ring(20, 10), 1), 0,
            new[] { JobBuilder.Part("disc", TestShapes.Disc(1), 3) }, Spacing, cts.Token));
    }

    /// <summary>The layout check on a sheet, plus an independent analytic disc oracle.</summary>
    private static void AssertPhysical(NestJobPart frame, (NestJobPart Part, Vector Center)[] inserts,
        IReadOnlyList<NestJobPlacement> poses)
    {
        var frameGeometry = JobPartGeometry.Read(frame.Geometry);
        var bounds = frameGeometry.Bounds;
        var stock = JobBuilder.Stock("sheet", bounds.Width + 2, bounds.Length + 2, Spacing);
        var parts = new[] { frame }.Concat(inserts.Select(i => i.Part)).ToArray();
        var job = JobBuilder.Job(parts, new[] { stock });
        var dx = 1 - bounds.Left;
        var dy = 1 - bounds.Bottom;
        var placements = new[] { new NestJobPlacement(frame.Id, 0, dx, dy, 0) }
            .Concat(poses.Select(p => p with { X = p.X + dx, Y = p.Y + dy })).ToArray();
        var result = new NestJobResult(NestJobStatus.Complete, NestJobStopReason.Completed,
            new[] { new NestJobPlateResult(0, stock, placements) },
            parts.Select(part =>
            {
                var placed = placements.Count(p => p.PartId == part.Id);
                return new PartFulfillment(part.Id, part.Quantity, placed, part.Quantity - placed);
            }).ToArray(),
            new[] { new StockUsage(stock.Id, 1, null) });
        LayoutAssert.Valid(job, result);

        var hole = CutoutCirclePacker.RoundHole(frameGeometry.Cutouts[0])!;
        var discs = poses.Select(pose =>
        {
            var (part, local) = inserts.Single(i => i.Part.Id == pose.PartId);
            var radius = JobPartGeometry.Read(part.Geometry).Bounds.Width / 2;
            var cos = System.Math.Cos(pose.Rotation);
            var sin = System.Math.Sin(pose.Rotation);
            var center = new Vector(pose.X + local.X * cos - local.Y * sin, pose.Y + local.X * sin + local.Y * cos);
            return (Center: center, Radius: radius);
        }).ToArray();
        foreach (var disc in discs)
            Assert.True(disc.Center.DistanceTo(hole.Center) + disc.Radius <= hole.Radius - Spacing,
                $"disc at {disc.Center} reaches the hole wall");
        for (var i = 0; i < discs.Length; i++)
            for (var j = i + 1; j < discs.Length; j++)
                Assert.True(discs[i].Center.DistanceTo(discs[j].Center) >= discs[i].Radius + discs[j].Radius + Spacing,
                    $"discs at {discs[i].Center} and {discs[j].Center} are closer than spacing");
    }

    private static OpenNest.CNC.Program FromEntities(params Entity[] entities) =>
        ConvertGeometry.ToProgram(entities.ToList());

    private static OpenNest.CNC.Program Square(double outer, double inner)
    {
        var program = TestShapes.Rectangle(outer, outer);
        var a = (outer - inner) / 2;
        var b = a + inner;
        program.MoveTo(a, a);
        program.LineTo(a, b);
        program.LineTo(b, b);
        program.LineTo(b, a);
        program.LineTo(a, a);
        return program;
    }
}
