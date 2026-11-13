using OpenNest.Clamps;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Tests.NestingEngines;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Jobs;

public class FixedStrategyNestingEngineTests
{
    [Fact]
    public void ForcesConfiguredStrategyRegardlessOfJobOptions()
    {
        var engine = new FixedStrategyNestingEngine("Strip");
        // The job itself declares an unknown strategy; if FixedStrategyNestingEngine
        // didn't override it, PlateNesterFactory would reject it with NotSupportedException.
        var job = FiniteStockJobTests.Job(1, new NestJobOptions("Not A Real Strategy"));

        var result = engine.Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void PreservesJobMaxPlates()
    {
        var engine = new FixedStrategyNestingEngine("Fill");
        var part = new NestJobPart(
            "part",
            PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(100, 100)),
            6
        );
        var stock = new NestPlateStock(
            "sheet",
            new Size(220, 220),
            quantity: null,
            partSpacing: 2.0,
            edgeSpacing: new Spacing(5.0, 5.0, 5.0, 5.0),
            quadrant: 1
        );
        var job = new NestJob(
            new[] { part },
            new[] { stock },
            new NestJobOptions("Fill", maxPlates: 1)
        );

        var result = engine.Solve(job);

        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(NestJobStopReason.PlateLimitReached, result.StopReason);
        Assert.Single(result.Plates);
    }

    public static TheoryData<string> Strategies =>
        new() { "Lattice", "Strip", "Vertical Remnant", "Horizontal Remnant" };

    [Theory]
    [MemberData(nameof(Strategies))]
    public void ClampedSheet_KeepsTheClampClearance(string strategy)
    {
        // N26-2A5 rev 5: a 26 x 26 sheet with one rail clamp on its front edge. Strip used to
        // lay its strips and pack its leftovers across the clamp, so the runner's validation
        // threw every layout away.
        var clamp = new ClampZone("clamp-1", new Box(19.25, 0, 1.5, 0.625));
        var stock = new NestPlateStock("sheet", new Size(26, 26), 1, 0.25,
            new Spacing(0.25, 0.25, 0.25, 0.25), 1, null, [clamp]);
        var parts = new[]
        {
            new NestJobPart("stop",
                PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(3, 1.5)), 14),
            new NestJobPart("plate",
                PartGeometrySnapshot.FromProgram(TestDrawingFactory.Rectangle(4, 2.5)), 14),
        };
        var job = new NestJob(parts, new[] { stock }, new NestJobOptions(strategy));

        var result = new FixedStrategyNestingEngine(strategy).Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(28, result.Plates.Sum(p => p.Placements.Count));
        // NestLayoutCheck holds every part the clamp clearance away from the zone.
        LayoutAssert.Valid(job, result);
    }

    [Fact]
    public void RejectsNullOrWhitespaceStrategyAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new FixedStrategyNestingEngine(null));
        Assert.Throws<ArgumentException>(() => new FixedStrategyNestingEngine("  "));
    }

    public static TheoryData<string, string, double, int> LockedRotationCases =>
        new()
        {
            { "Fill", "rect-6x4", 0, 20 },
            { "Strip", "rect-6x4", 0, 20 },
            { "Strip", "rect-20x3", 0, 10 },
            { "Strip", "rect-3x20", 0, 10 },
            { "Strip", "rect-6x4", System.Math.PI / 2, 20 },
            { "Strip", "rect-6x4", 0.3, 10 },
            { "Strip", "lshape", 0.3, 10 },
            { "Vertical Remnant", "lshape", 0.3, 10 },
            { "Horizontal Remnant", "lshape", 0.3, 10 },
        };

    [Theory]
    [MemberData(nameof(LockedRotationCases))]
    public void LockedRotation_CompletesAtTheLockedAngle(
        string strategy,
        string shape,
        double angle,
        int quantity
    )
    {
        var program = shape switch
        {
            "rect-6x4" => TestDrawingFactory.Rectangle(6, 4),
            "rect-20x3" => TestDrawingFactory.Rectangle(20, 3),
            "rect-3x20" => TestDrawingFactory.Rectangle(3, 20),
            _ => TestDrawingFactory.LShape(),
        };
        var part = new NestJobPart(
            shape,
            PartGeometrySnapshot.FromProgram(program),
            quantity,
            rotation: RotationPolicy.Fixed(angle)
        );
        var stock = new NestPlateStock("stock", new Size(30, 50), 3, 1, new Spacing(1, 1, 1, 1));
        var job = new NestJob(new[] { part }, new[] { stock }, new NestJobOptions(strategy));

        var result = new FixedStrategyNestingEngine(strategy).Solve(job);

        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(quantity, result.Plates.Sum(p => p.Placements.Count));
        // Checks NestLayoutCheck and that every pose is an angle the policy allows.
        LayoutAssert.Valid(job, result);
    }
}
