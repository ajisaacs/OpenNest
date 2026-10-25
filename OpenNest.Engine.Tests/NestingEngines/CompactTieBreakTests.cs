using OpenNest.Engine.Jobs;
using OpenNest.Engine.Tests.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// Without salvage credit (desktop Auto Nest on one plate size) every complete one-sheet layout
/// costs the same, so engines must break the tie toward the larger free offcut. Shaped after
/// N26-23S: four 7.083 x 22.577 and four 6.236 x 18.688 boxes on a clamped 60 x 120 sheet,
/// which once nested 27.86" long when turning two of the smaller boxes fits it in 21.3".
/// </summary>
public class CompactTieBreakTests
{
    [Theory]
    [InlineData("Rectangles")]
    [InlineData("Irregular")]
    [InlineData("Default")]
    public void EqualCostOneSheetLayoutsPreferTheLargestOffcut(string engine)
    {
        var template = new Plate(60, 120)
        {
            PartSpacing = 0.3,
            EdgeSpacing = new Spacing(0.3, 0.75, 0.3, 0.25),
        };
        template.SetClampRail(new ClampRail(1.5, 0.625, new double[] { 20, 40, 60, 80, 100, 120, 140 }));
        var items = new[]
        {
            new NestItem { Drawing = new Drawing("leg-bottom", TestDrawingFactory.Rectangle(7.083, 22.577)), Quantity = 4 },
            new NestItem { Drawing = new Drawing("leg-top", TestDrawingFactory.Rectangle(6.236, 18.688)), Quantity = 4 },
        };

        var result = NestPipeline.Run(new NestPipelineRequest(engine, items,
            NestStockBuilder.FromTemplate(template, null),
            new NestJobOptions(maxPlates: 100, salvageRate: 0, minimumSalvageDimension: 0)));

        Assert.True(result.IsValid, string.Join("; ", result.Violations));
        var parts = Assert.Single(result.Plates).Parts;
        Assert.Equal(8, parts.Count);
        Assert.True(parts.Max(p => p.BoundingBox.Right) < 21.5, $"used length {parts.Max(p => p.BoundingBox.Right):0.###}");
    }
}
