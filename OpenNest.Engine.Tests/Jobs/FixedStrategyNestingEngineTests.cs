using OpenNest.Geometry;
using Xunit;
using OpenNest.Engine.Jobs;

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
        var engine = new FixedStrategyNestingEngine("Default");
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
            new NestJobOptions("Default", maxPlates: 1)
        );

        var result = engine.Solve(job);

        Assert.Equal(NestJobStatus.Incomplete, result.Status);
        Assert.Equal(NestJobStopReason.PlateLimitReached, result.StopReason);
        Assert.Single(result.Plates);
    }

    [Fact]
    public void RejectsNullOrWhitespaceStrategyAtConstruction()
    {
        Assert.Throws<ArgumentException>(() => new FixedStrategyNestingEngine(null));
        Assert.Throws<ArgumentException>(() => new FixedStrategyNestingEngine("  "));
    }
}
