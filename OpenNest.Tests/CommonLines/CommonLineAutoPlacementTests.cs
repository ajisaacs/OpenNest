using OpenNest.CommonLines;
using OpenNest.Engine;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Tests.CommonLines;

/// <summary>
/// N26-295's last plate (3/16 stainless, 48 x 120): 1 PT03, 3 PT14 and 34 PT42 at the nest's
/// 0.25 spacing. Without common lines Auto Nest fits 37 and leaves a PT42 for another sheet;
/// pairing PT42s one kerf apart fits all 38. The customer nest stays out of the repository:
/// set <c>CommonLineLastPlateNestPath</c> in test-config.json to run it.
/// </summary>
public class CommonLineAutoPlacementTests
{
    private const double Kerf = 0.012;

    private static NestPipelineRequest LastPlate(string path, CommonLineUsage usage)
    {
        var nest = new NestReader(path).Read();
        var plate = nest.Plates[4];
        var drawings = plate.Parts.Select(p => p.BaseDrawing).Distinct().ToList();
        var pt42 = drawings.Single(d => d.Name.EndsWith("PT42"));
        pt42.AllowCommonLine = true;
        var items = drawings.Select(d => new NestItem
        {
            Drawing = d,
            Quantity = d == pt42 ? 34 : plate.Parts.Count(p => p.BaseDrawing == d),
        }).ToList();
        var stock = new NestPlateStock("48x120", new Size(48, 120), 1, 0.25, new Spacing(0.25, 0.25, 0.25, 0.25), 1);
        return new NestPipelineRequest("Default", items, [stock],
            new NestJobOptions { CommonLineUsage = usage, CommonLineKerf = Kerf });
    }

    [SkippableFact]
    public void LastPlate_FitsTheLastPt42OnlyWithCommonLines()
    {
        var path = TestConfig.GetExistingPath("CommonLineLastPlateNestPath");
        Skip.If(path == null, "N26-295 nest not configured in test-config.json");

        var off = NestPipeline.Run(LastPlate(path!, CommonLineUsage.Off));
        var on = NestPipeline.Run(LastPlate(path!, CommonLineUsage.WhenItAddsParts));

        Assert.True(off.IsValid, string.Join("; ", off.Violations));
        Assert.Equal(37, off.Raw.Plates.Sum(p => p.Placements.Count));
        Assert.All(off.Plates, plate => Assert.Empty(plate.CommonLines));

        Assert.True(on.IsValid, string.Join("; ", on.Violations));
        Assert.Equal(NestJobStatus.Complete, on.Status);
        var plate = Assert.Single(on.Plates);
        Assert.Equal(38, plate.Parts.Count);
        Assert.NotEmpty(plate.CommonLines);
        Assert.All(plate.CommonLines, group =>
        {
            Assert.True(CommonLineGeometry.Check(group).IsIntact);
            Assert.All(group.Members, member => Assert.EndsWith("PT42", member.BaseDrawing.Name));
        });
    }
}
