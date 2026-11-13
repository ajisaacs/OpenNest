using OpenNest.Engine;
using OpenNest.Engine.Jobs.Placement.Fillers;
using OpenNest.Geometry;

namespace OpenNest.Tests.Engine;

public class EngineRefactorSmokeTests
{
    [Fact]
    public void PlateFiller_FillNestItem_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "PlateFiller should fill parts");
    }

    [Fact]
    public void PlateFiller_FillGroupParts_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var drawing = TestHelpers.MakeRectDrawing(20, 10);
        var groupParts = new List<Part> { new Part(drawing) };

        var parts = filler.Fill(
            groupParts,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "PlateFiller group fill should produce parts");
    }

    [Fact]
    public void PlateFiller_ForceFullAngleSweep_StillWorks()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        filler.ForceFullAngleSweep = true;
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10) };

        var parts = filler.Fill(
            item,
            plate.WorkArea(),
            null,
            System.Threading.CancellationToken.None
        );

        Assert.True(parts.Count > 0, "ForceFullAngleSweep should still produce results");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlateFiller_StopAtQuantity_StillDeliversExactlyTheQuantity(bool stopAtQuantity)
    {
        var plate = new Plate(60, 120) { PartSpacing = 0.5 };
        var filler = new PlateFiller(plate) { StopAtQuantity = stopAtQuantity };
        var item = new NestItem { Drawing = TestHelpers.MakeRectDrawing(7, 3), Quantity = 23 };

        var parts = filler.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.Equal(23, parts.Count);
        plate.Parts.AddRange(parts);
        Assert.Empty(PartOverlapChecker.FindOverlappingPairs(plate.Parts));
        var area = plate.WorkArea();
        Assert.All(parts, p => Assert.True(
            p.BoundingBox.Left >= area.Left - 1e-6 && p.BoundingBox.Right <= area.Right + 1e-6
                && p.BoundingBox.Bottom >= area.Bottom - 1e-6 && p.BoundingBox.Top <= area.Top + 1e-6,
            "Part outside the work area"));
    }

    [Fact]
    public void StripLayout_Nest_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate, PlateFillPolicy.Fill, PlateLayout.Strips);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10, "large"), Quantity = 10 },
            new NestItem { Drawing = TestHelpers.MakeRectDrawing(8, 5, "small"), Quantity = 5 },
        };

        var parts = filler.Nest(items, null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "Strip layout should nest parts");
    }

    [Fact]
    public void PlateFiller_Nest_ProducesResults()
    {
        var plate = new Plate(60, 120);
        var filler = new PlateFiller(plate);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = TestHelpers.MakeRectDrawing(20, 10, "a"), Quantity = 5 },
            new NestItem { Drawing = TestHelpers.MakeRectDrawing(15, 8, "b"), Quantity = 3 },
        };

        var parts = filler.Nest(items, null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "Base Nest method should place parts");
    }

    [Fact]
    public void BruteForceRunner_StillWorks()
    {
        var plate = new Plate(60, 120);
        var drawing = TestHelpers.MakeRectDrawing(20, 10);

        var result = OpenNest.Engine.ML.BruteForceRunner.Run(
            drawing,
            plate,
            forceFullAngleSweep: true
        );

        Assert.NotNull(result);
        Assert.True(result.PartCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.WinnerEngine));
        Assert.NotEmpty(result.AngleResults);
    }
}
