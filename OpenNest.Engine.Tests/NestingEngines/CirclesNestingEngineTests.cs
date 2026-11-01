using System;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.NestingEngines;
using OpenNest.Engine.NestingEngines.Circles;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>Disc packing of round parts. Every layout is checked by the shared layout check.</summary>
public class CirclesNestingEngineTests
{
    [Fact]
    public void NonRoundJobBidsNothing()
    {
        var job = Job([Part("disc", Disc(2), 3), Part("square", Rectangle(4, 4), 3)], [Stock("s", 20, 20)]);
        var progress = 0;
        var result = new CirclesNestingEngine().Solve(job, new Capture(_ => progress++));

        Assert.Empty(result.Plates);
        Assert.Equal(0, progress);
        Assert.All(result.Fulfillment, f => Assert.Equal(f.Requested, f.Unplaced));
        Assert.Equal(NestJobStopReason.NoPlacementFound, result.StopReason);
    }

    [Fact]
    public void EqualDiscsPackHexagonallyDenserThanASquareGrid()
    {
        // A square grid holds 100 unit-radius discs on 20.1 x 20.1; staggered rows hold 105.
        var job = Job([Part("disc", Disc(1), 200)], [Stock("s", 20.1, 20.1, quantity: 1)]);
        var result = new CirclesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.True(Assert.Single(result.Plates).Placements.Count >= 105,
            $"{result.Plates[0].Placements.Count} discs");
    }

    [Fact]
    public void SmallDiscsFillTheGapsBetweenLargeOnes()
    {
        // Four r=5 discs fit a 21 x 21 sheet only as a square; r=1 discs fit only in the five
        // gaps between them and the edges.
        var job = Job([Part("large", Disc(5), 4), Part("small", Disc(1), 5)],
            [Stock("s", 21, 21, spacing: 0.1, quantity: 1)]);
        var result = new CirclesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(9, Assert.Single(result.Plates).Placements.Count);
    }

    [Fact]
    public void ClampZonesAndEdgeClearanceAreKept()
    {
        var clamps = new[] { new ClampZone("a", new Box(4, 0, 2, 1)), new ClampZone("b", new Box(14, 0, 2, 1)) };
        var job = Job([Part("disc", Disc(1.5), 60)],
            [Stock("s", 20, 20, spacing: 0.25, edge: new Spacing(0.5, 0.5, 0.5, 0.5), quantity: 1, clampZones: clamps)]);
        var result = new CirclesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.NotEmpty(Assert.Single(result.Plates).Placements);
    }

    [Fact]
    public void ARailsOwnClearanceIsTheOneTheLayoutIsCheckedAgainst()
    {
        // Rail clearance 0 under 0.25 edge spacing: discs may pass closer to a clamp than the
        // edge spacing, and the layout check must judge them by the clearance they were packed to.
        var plate = new Plate(new Size(60, 120)) { PartSpacing = 0.25, EdgeSpacing = new Spacing(0.25, 0.25) };
        plate.SetClampRail(new ClampRail(1.5, 0.625, new double[] { 20, 40, 60, 80, 100, 120 }, clearance: 0));
        var job = Job([Part("disc", Disc(4), 150)], [DrawingJobMapper.FromPlate("s", plate, 1)]);
        var result = new CirclesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        var clamps = job.Plates[0].ClampZones.Select(z => z.Bounds.Offset(job.Plates[0].EdgeSpacing.Bottom));
        Assert.Contains(Assert.Single(result.Plates).Placements, p => clamps.Any(z => Reaches(z, p.X, p.Y, 4)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void RandomMixedDiametersNeverOverlap(int seed)
    {
        var random = new Random(seed);
        var parts = Enumerable.Range(0, 6)
            .Select(i => Part($"p{i}", random.Next(2) == 0
                    ? Disc(0.5 + random.NextDouble() * 4)
                    : Ring(2 + random.NextDouble() * 8, 1),
                1 + random.Next(15), RotationPolicy.Automatic))
            .ToArray();
        var job = Job(parts, [Stock("a", 24, 36, spacing: 0.2 * random.Next(3)), Stock("b", 30, 30, spacing: 0.2)]);
        var result = new CirclesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        foreach (var plate in result.Plates)
        {
            var centers = plate.Placements.Select(p => new Vector(p.X, p.Y)).ToArray();
            for (var i = 0; i < centers.Length; i++)
                for (var j = i + 1; j < centers.Length; j++)
                    Assert.NotEqual(centers[i], centers[j]);
        }
    }

    [Fact]
    public void GearsPackByTheirTipCircle()
    {
        var job = Job([Part("gear", Gear(12, 4, 3.6), 10, RotationPolicy.Fixed(0.3))], [Stock("s", 30, 30, spacing: 0.25)]);
        var result = new CirclesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.All(result.Plates.SelectMany(p => p.Placements), p => Assert.Equal(0.3, p.Rotation, 12));
    }

    [Fact]
    public void ReportsEachBetterRunOrTailEndingWithTheReturnedLayout()
    {
        var job = Job([Part("large", Disc(4), 7), Part("small", Disc(1.5), 25)],
            [Stock("a", 24, 30, spacing: 0.25), Stock("b", 40, 40, spacing: 0.25)]);

        var plans = PlanAssert.ReportedAndEndWithTheResult(() => new CirclesNestingEngine(), job);

        // Only improvements are reported: each plan beats the one before it.
        var scores = plans.Select(p => CandidateScore.Of(job, p, 0)).ToList();
        Assert.All(scores.Zip(scores.Skip(1)), s => Assert.True(s.Second.IsBetterThan(s.First)));
    }

    /// <summary>Whether a disc at this center would enter the box.</summary>
    private static bool Reaches(Box box, double x, double y, double radius)
    {
        var dx = System.Math.Max(0, System.Math.Max(box.Left - x, x - box.Right));
        var dy = System.Math.Max(0, System.Math.Max(box.Bottom - y, y - box.Top));
        return dx * dx + dy * dy < radius * radius;
    }

    /// <summary>A polygonal gear: alternating tip and root radii.</summary>
    private static Program Gear(int teeth, double tip, double root) =>
        Polyline(Enumerable.Range(0, 2 * teeth).Select(i =>
        {
            var angle = System.Math.PI * i / teeth;
            var r = i % 2 == 0 ? tip : root;
            return (r * System.Math.Cos(angle), r * System.Math.Sin(angle));
        }).ToArray());

    private sealed class Capture(Action<NestJobProgress> action) : IProgress<NestJobProgress>
    { public void Report(NestJobProgress value) => action(value); }
}

public sealed class CirclesContractTests : EngineContractTests<CirclesNestingEngine>
{
    protected override Program Block(double size) => Disc(size / 2);

    protected override Program Mixed(int which) => which switch
    {
        0 => Ring(9, 4),
        1 => Disc(3.5),
        _ => Ring(6, 3),
    };

    protected override Program Etchable() => Disc(5);
}
