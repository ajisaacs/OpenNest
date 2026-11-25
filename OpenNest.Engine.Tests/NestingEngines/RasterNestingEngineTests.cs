using OpenNest.Clamps;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.Raster;
using OpenNest.Geometry;
using Xunit;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class RasterNestingEngineTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.02)]
    [InlineData(0.25)]
    [InlineData(1)]
    public void MasksKeepTheSpacingWithoutRepair(double spacing)
    {
        // Arcs, notches and holes: every layout must pass the layout check as decoded.
        var job = Job(
            [
                Part("ring", Ring(6, 3), 6),
                Part("ell", LShape(9, 7, 3), 8),
                Part("tri", Triangle(7, 7), 8),
                Part("disc", Disc(1), 10),
            ],
            [Stock("s", 24, 36, spacing)]);
        var result = new RasterNestingEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void SmallPartsNestInHoles()
    {
        // A 1 x 1 square fits a 10-wide ring's 8-wide hole; the sheet only holds the ring.
        var job = Job([Part("ring", Ring(10, 8), 1), Part("square", Rectangle(1, 1), 4)],
            [Stock("s", 10.2, 10.2, 0.1, quantity: 1)]);
        var result = new RasterNestingEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void ClampKeepOutsStayClear()
    {
        var job = Job([Part("ell", LShape(4, 3, 1), 20)],
            [Stock("s", 20, 20, 0.2, clampZones: [new ClampZone("c", new Box(8, 0, 4, 2))])]);
        var result = new RasterNestingEngine().Solve(job);
        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void IsRegistered() =>
        Assert.IsType<RasterNestingEngine>(NestingEngineRegistry.Create("Raster"));
}

public sealed class RasterContractTests : EngineContractTests<RasterNestingEngine> { }
