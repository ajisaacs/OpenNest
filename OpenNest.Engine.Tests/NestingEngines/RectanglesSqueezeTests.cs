using OpenNest.Converters;
using OpenNest.Engine.Jobs;
using OpenNest.Engine.Jobs.Adapters;
using OpenNest.Engine.NestingEngines.Rectangles;
using OpenNest.Geometry;
using static OpenNest.Engine.Tests.NestingEngines.JobBuilder;
using static OpenNest.Engine.Tests.NestingEngines.Shapes;

namespace OpenNest.Engine.Tests.NestingEngines;

/// <summary>
/// The sheet that finishes a job is re-packed into a shorter or narrower bin, so the drop it
/// leaves is as large as the packer can make it.
/// </summary>
public class RectanglesSqueezeTests
{
    /// <summary>
    /// N26-2A5: 14 door stops (4.168 x 1.5) and 14 lock plates (2 x 2) on a 26 x 26 sheet with a
    /// front clamp. Every fit rule on the whole sheet stopped at 8.0 high; the shop's hand layout
    /// holds all 28 under 7.5 (three rows of four stops, two stops on end over the clamp, a 2 x 2
    /// block of plates, then a row of plates across the top), leaving an 18" drop.
    /// </summary>
    [Fact]
    public void FinishingSheetPacksIntoTheShortestStripItFinds()
    {
        var clamp = new ClampZone("clamp-1", new Box(19.25, 0, 1.5, 0.625));
        var job = Job(
            [
                Part("door stop", Rectangle(4.168108, 1.5), 14),
                Part("lock plate", Rectangle(2, 2), 14),
            ],
            [Stock("sheet", 26, 26, spacing: 0.25, edge: new Spacing(0.25, 0.25, 0.25, 0.25), clampZones: [clamp])]);

        var result = new RectanglesNestingEngine().Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(NestJobStatus.Complete, result.Status);
        Assert.Single(result.Plates);
        Assert.True(Top(job, result.Plates[0]) <= 7.5 + 1e-9, $"layout top {Top(job, result.Plates[0])}");
    }

    [Fact]
    public void SheetsThatDoNotFinishTheJobAreNotSqueezed()
    {
        // Four 9 x 9 squares fill each 20 x 20 sheet; only the last sheet could be squeezed, and
        // it repeats the full layout, so every sheet keeps one pass of the twelve rule/mode packs.
        var job = Job([Rectangle("a", 9, 9, 40)], [Stock("s", 20, 20, spacing: 0.2)]);
        var calls = 0;

        var result = new RectanglesNestingEngine { SheetPacked = _ => calls++ }.Solve(job);

        LayoutAssert.Valid(job, result);
        Assert.Equal(12, calls);
    }

    private static double Top(NestJob job, NestJobPlateResult sheet) =>
        sheet.Placements.Max(pose =>
        {
            var part = job.Parts.Single(p => p.Id == pose.PartId);
            var geometry = ConvertProgram.ToGeometry(DrawingJobMapper.ToProgram(part.Geometry))
                .Where(e => SpecialLayers.IsMaterial(e.Layer)).ToArray();
            foreach (var entity in geometry)
            {
                entity.Rotate(pose.Rotation);
                entity.Offset(pose.X, pose.Y);
            }
            return geometry.Max(e => e.Top);
        });
}
