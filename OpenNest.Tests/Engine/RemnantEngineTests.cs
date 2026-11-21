using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Engine.Jobs.Placement;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Tests.Engine;

public class RemnantEngineTests
{
    [Fact]
    public void VerticalRemnantPlateFiller_UsesHorizontalPreferredDirection()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate, PlateFillPolicy.VerticalRemnant);
        Assert.Equal(NestDirection.Horizontal, filler.PreferredDirection);
    }

    [Fact]
    public void HorizontalRemnantPlateFiller_UsesVerticalPreferredDirection()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate, PlateFillPolicy.HorizontalRemnant);
        Assert.Equal(NestDirection.Vertical, filler.PreferredDirection);
    }

    [Fact]
    public void VerticalRemnantPlateFiller_Fill_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate, PlateFillPolicy.VerticalRemnant);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "VerticalRemnantPlateFiller should fill parts");
    }

    [Fact]
    public void HorizontalRemnantPlateFiller_Fill_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate, PlateFillPolicy.HorizontalRemnant);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "HorizontalRemnantPlateFiller should fill parts");
    }

    [Fact]
    public void PlateFillService_ListsBothRemnantStrategies()
    {
        Assert.Contains("Vertical Remnant", PlateFillService.BuiltInStrategies);
        Assert.Contains("Horizontal Remnant", PlateFillService.BuiltInStrategies);
    }

    [Fact]
    public void VerticalRemnantPlateFiller_ProducesTighterXExtent_ThanDefault()
    {
        var plate = new Plate(60, 120);
        var drawing = TestHelpers.MakeRectDrawing(20, 10);
        var item = new NestItem { Drawing = drawing };

        var defaultFiller = new PlateFiller(plate);
        var remnantFiller = new PlateFiller(plate, PlateFillPolicy.VerticalRemnant);

        var defaultParts = defaultFiller.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );
        var remnantParts = remnantFiller.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(defaultParts.Count > 0);
        Assert.True(remnantParts.Count > 0);

        var defaultXExtent =
            defaultParts.Max(p => p.BoundingBox.Right) - defaultParts.Min(p => p.BoundingBox.Left);
        var remnantXExtent =
            remnantParts.Max(p => p.BoundingBox.Right) - remnantParts.Min(p => p.BoundingBox.Left);

        Assert.True(
            remnantXExtent <= defaultXExtent + 0.01,
            $"Remnant X-extent ({remnantXExtent:F1}) should be <= default filler ({defaultXExtent:F1})"
        );
    }

    [Fact]
    public void VerticalRemnantPlateFiller_LaysAQuantityOfDiscsInFullHeightColumns()
    {
        // Eight-inch discs, 7 then 6 to a full-height column: twelve fit in two columns
        // (about 15 in.), where three half-pitch columns of 4-disc rows take 16.25 in.
        var plate = new Plate(60, 120) { PartSpacing = 0.25 };
        var filler = new PlateFiller(plate, PlateFillPolicy.VerticalRemnant);
        var item = new NestItem
        {
            Drawing = new Drawing("disc", TestHelpers.MakeCircleProgram(0, 0, 4)),
            Quantity = 12,
        };

        var parts = filler.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.Equal(12, parts.Count);
        var xExtent = parts.Max(p => p.BoundingBox.Right) - parts.Min(p => p.BoundingBox.Left);
        var yExtent = parts.Max(p => p.BoundingBox.Top) - parts.Min(p => p.BoundingBox.Bottom);
        Assert.True(xExtent < 15.5, $"X-extent {xExtent:F2} should hold two columns");
        Assert.True(yExtent > 55, $"Y-extent {yExtent:F2} should span the plate's height");
    }

    [Fact]
    public void HorizontalRemnantPlateFiller_LaysAQuantityOfDiscsInFullWidthRows()
    {
        var plate = new Plate(120, 60) { PartSpacing = 0.25 };
        var filler = new PlateFiller(plate, PlateFillPolicy.HorizontalRemnant);
        var item = new NestItem
        {
            Drawing = new Drawing("disc", TestHelpers.MakeCircleProgram(0, 0, 4)),
            Quantity = 12,
        };

        var parts = filler.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.Equal(12, parts.Count);
        var xExtent = parts.Max(p => p.BoundingBox.Right) - parts.Min(p => p.BoundingBox.Left);
        var yExtent = parts.Max(p => p.BoundingBox.Top) - parts.Min(p => p.BoundingBox.Bottom);
        Assert.True(yExtent < 15.5, $"Y-extent {yExtent:F2} should hold two rows");
        Assert.True(xExtent > 55, $"X-extent {xExtent:F2} should span the plate's width");
    }
}
