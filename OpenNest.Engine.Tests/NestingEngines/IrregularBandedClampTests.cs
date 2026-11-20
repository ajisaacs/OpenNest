using OpenNest.Clamps;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.NestingEngines.Irregular;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// Banded fill on a sheet with a clamp rail along its bottom edge. The larger part's full-height
/// band fills the work area to within less than the rail's depth, so it cannot clear the clamps
/// and the copies on them went to the gap fill, ending the layout at 104.36. A band a row lower
/// seats whole clear of the rail and leaves a strip for the last copies; the layout ends at 103.95.
/// </summary>
public class IrregularBandedClampTests
{
    private static ClampZone[] Rail() => Enumerable.Range(0, 6)
        .Select(i => new ClampZone($"clamp-{i + 1}", new Box(19.25 + 20 * i, 0, 1.5, 0.625))).ToArray();

    [Fact]
    public void LowerBandClearsABottomClampRail()
    {
        var stock = Stock("sheet", 60, 120, spacing: 0.25, edge: new Spacing(0.25, 0.25, 0.25, 0.25),
            clampZones: Rail());
        var job = Job([
            Part("large", Shapes.Rectangle(22.5, 7.2), 20, RotationPolicy.Automatic),
            Part("small", Shapes.Rectangle(18, 6), 20, RotationPolicy.Automatic),
        ], [stock]);

        var result = new IrregularNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        var plate = Assert.Single(NestResultMaterializer.Materialize(job, result).Nest.Plates);
        Assert.Equal(40, plate.Parts.Count);
        var right = plate.Parts.Max(p => p.BoundingBox.Right);
        Assert.True(right < 104.2, $"Layout ends at {right:F3}");
    }
}
