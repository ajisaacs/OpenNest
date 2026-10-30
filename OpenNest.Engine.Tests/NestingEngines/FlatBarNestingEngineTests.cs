using System.Linq;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.NestingEngines.FlatBar;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

public class FlatBarNestingEngineTests
{
    [Fact]
    public void CutListLengthsUseTheFewestBars()
    {
        // First fit decreasing needs three bars ([45,40] [35,30,25] [25]); two are exact.
        var lengths = new[] { 45.0, 40, 35, 30, 25, 25 };
        var job = Job(
            lengths.Select((l, i) => Rectangle($"p{i}", l, 3, 1)).ToArray(),
            [Stock("bar", 3, 100)]);

        var result = new FlatBarNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(2, result.Plates.Count);
    }

    [Fact]
    public void PartsLieInOneRowAlongTheBar()
    {
        var job = Job([Rectangle("p", 20, 3, 6)], [Stock("bar", 3, 144, spacing: 0.125)]);

        var result = new FlatBarNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        var placements = Assert.Single(result.Plates).Placements;
        Assert.Equal(6, placements.Count);
        Assert.All(placements, p => Assert.Equal(0, p.Y, 9));
    }

    [Fact]
    public void BarRunningAlongYIsCutAlongY()
    {
        var job = Job([Part("p", Rectangle(3, 20), 6, RotationPolicy.Automatic)], [Stock("bar", 144, 3, spacing: 0.125)]);

        var result = new FlatBarNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(6, Assert.Single(result.Plates).Placements.Count);
    }

    [Fact]
    public void MitredEndsInterlockByTurningAlternateParts()
    {
        // 45-degree mitres: a 23-long box, but turned copies nest 20 apart. Five fit on a
        // 105 bar only when every other part is turned and slid into its neighbor.
        var trapezoid = Part("mitre", Polyline((0, 0), (23, 0), (20, 3), (3, 3)), 5, RotationPolicy.Automatic);
        var job = Job([trapezoid], [Stock("bar", 3, 105, spacing: 0.25)]);

        var flatBar = new FlatBarNestingEngine().Solve(job);
        var rectangles = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, flatBar);
        Assert.Equal(NestJobStatus.Complete, flatBar.Status);
        Assert.Single(flatBar.Plates);
        Assert.Equal(2, rectangles.Plates.Count);
    }

    [Fact]
    public void ClampZoneSplitsTheBar()
    {
        var clamp = new ClampZone("c", new Box(48, -1, 4, 5));
        var job = Job([Rectangle("p", 45, 3, 2)], [Stock("bar", 3, 100, quantity: 1, clampZones: [clamp])]);

        var result = new FlatBarNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
    }

    [Fact]
    public void CheaperShortBarFinishesTheJob()
    {
        // 150 of parts: one long bar plus one short bar is cheaper than two long bars.
        var job = Job(
            [Rectangle("a", 90, 3, 1), Rectangle("b", 60, 3, 1)],
            [
                new NestPlateStock("long", new Size(3, 100), null, 0, default, 1, 100.0),
                new NestPlateStock("short", new Size(3, 60), null, 0, default, 1, 60.0),
            ]);

        var result = new FlatBarNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Equal(["long", "short"], result.Plates.Select(p => p.StockId).Order());
    }
}

public sealed class FlatBarContractTests : EngineContractTests<FlatBarNestingEngine> { }
