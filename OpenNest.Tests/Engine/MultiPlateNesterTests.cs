using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace OpenNest.Tests.Engine;

public class MultiPlateNesterTests
{
    private static Drawing MakeDrawing(string name, double width, double length)
    {
        var program = new OpenNest.CNC.Program();
        program.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(width, 0)));
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(width, length)));
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, length)));
        program.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        var drawing = new Drawing(name, program);
        drawing.UpdateArea();
        return drawing;
    }

    private static NestItem MakeItem(string name, double width, double length, int qty = 1)
    {
        return new NestItem
        {
            Drawing = MakeDrawing(name, width, length),
            Quantity = qty,
        };
    }

    [Fact]
    public void SortByBoundingBoxArea_OrdersLargestFirst()
    {
        var items = new List<NestItem>
        {
            MakeItem("small", 10, 10),
            MakeItem("large", 40, 60),
            MakeItem("medium", 20, 30),
        };

        var sorted = MultiPlateNester.SortItems(items, PartSortOrder.BoundingBoxArea);

        Assert.Equal("large", sorted[0].Drawing.Name);
        Assert.Equal("medium", sorted[1].Drawing.Name);
        Assert.Equal("small", sorted[2].Drawing.Name);
    }

    [Fact]
    public void SortBySize_OrdersByLongestDimension()
    {
        var items = new List<NestItem>
        {
            MakeItem("short-wide", 50, 20),    // longest = 50
            MakeItem("tall-narrow", 10, 80),    // longest = 80
            MakeItem("square", 30, 30),         // longest = 30
        };

        var sorted = MultiPlateNester.SortItems(items, PartSortOrder.Size);

        Assert.Equal("tall-narrow", sorted[0].Drawing.Name);
        Assert.Equal("short-wide", sorted[1].Drawing.Name);
        Assert.Equal("square", sorted[2].Drawing.Name);
    }

    // --- Task 4: Part Classification ---

    [Fact]
    public void Classify_LargePart_WhenWidthExceedsHalfWorkArea()
    {
        var workArea = new Box(0, 0, 96, 48);
        var bb = new Box(0, 0, 50, 20); // width 50 > half of 96 = 48
        var result = MultiPlateNester.Classify(bb, workArea);
        Assert.Equal(PartClass.Large, result);
    }

    [Fact]
    public void Classify_LargePart_WhenLengthExceedsHalfWorkArea()
    {
        var workArea = new Box(0, 0, 96, 48);
        var bb = new Box(0, 0, 20, 30); // length 30 > half of 48 = 24
        var result = MultiPlateNester.Classify(bb, workArea);
        Assert.Equal(PartClass.Large, result);
    }

    [Fact]
    public void Classify_MediumPart_NotLargeButAreaAboveThreshold()
    {
        var workArea = new Box(0, 0, 96, 48);
        // workArea = 4608, 1/9 = 512. bb = 40*15 = 600 > 512
        // 40 < 48 (half of 96), 15 < 24 (half of 48) — not Large
        var bb = new Box(0, 0, 40, 15);
        var result = MultiPlateNester.Classify(bb, workArea);
        Assert.Equal(PartClass.Medium, result);
    }

    [Fact]
    public void Classify_SmallPart()
    {
        var workArea = new Box(0, 0, 96, 48);
        // workArea = 4608, 1/9 = 512. bb = 10*10 = 100 < 512
        var bb = new Box(0, 0, 10, 10);
        var result = MultiPlateNester.Classify(bb, workArea);
        Assert.Equal(PartClass.Small, result);
    }

    // --- Task 5: Scrap Zone Identification ---

    [Fact]
    public void IsScrapRemnant_BothDimensionsBelowThreshold_ReturnsTrue()
    {
        var remnant = new Box(0, 0, 10, 8);
        Assert.True(MultiPlateNester.IsScrapRemnant(remnant, 12.0));
    }

    [Fact]
    public void IsScrapRemnant_OneDimensionAboveThreshold_ReturnsFalse()
    {
        // 11 x 120 — narrow but long, should be preserved
        var remnant = new Box(0, 0, 11, 120);
        Assert.False(MultiPlateNester.IsScrapRemnant(remnant, 12.0));
    }

    [Fact]
    public void IsScrapRemnant_BothDimensionsAboveThreshold_ReturnsFalse()
    {
        var remnant = new Box(0, 0, 20, 30);
        Assert.False(MultiPlateNester.IsScrapRemnant(remnant, 12.0));
    }

    [Fact]
    public void FindScrapZones_ReturnsOnlyScrapRemnants()
    {
        // 96x48 plate with a 70x40 part placed at origin
        var plate = new Plate(96, 48) { PartSpacing = 0.25 };
        var drawing = MakeDrawing("big", 70, 40);
        var part = new Part(drawing);
        plate.Parts.Add(part);

        var scrap = MultiPlateNester.FindScrapZones(plate, 12.0);

        // All returned zones should have both dims < 12
        foreach (var zone in scrap)
        {
            Assert.True(zone.Width < 12.0 && zone.Length < 12.0,
                $"Zone {zone.Width:F1}x{zone.Length:F1} is not scrap — at least one dimension >= 12");
        }
    }

    // --- Task 6: Plate Creation Helper ---

    [Fact]
    public void CreatePlate_UsesTemplateWhenNoOptions()
    {
        var template = new Plate(96, 48) { PartSpacing = 0.25, Quadrant = 1 };
        template.EdgeSpacing = new Spacing { Left = 1, Right = 1, Top = 1, Bottom = 1 };

        var plate = MultiPlateNester.CreatePlate(template, null, null);

        Assert.Equal(96, plate.Size.Width);
        Assert.Equal(48, plate.Size.Length);
        Assert.Equal(0.25, plate.PartSpacing);
        Assert.Equal(1, plate.Quadrant);
    }

    [Fact]
    public void CreatePlate_PicksSmallestFittingOption()
    {
        var template = new Plate(96, 48) { PartSpacing = 0.25, Quadrant = 1 };
        template.EdgeSpacing = new Spacing { Left = 1, Right = 1, Top = 1, Bottom = 1 };

        var options = new List<PlateOption>
        {
            new() { Width = 48, Length = 96, Cost = 100 },
            new() { Width = 60, Length = 120, Cost = 200 },
            new() { Width = 72, Length = 144, Cost = 300 },
        };

        // Part needs 50x50 work area — 48x96 (after edge spacing: 46x94) — 46 < 50, doesn't fit.
        // 60x120 (58x118) does fit.
        var minBounds = new Box(0, 0, 50, 50);

        var plate = MultiPlateNester.CreatePlate(template, options, minBounds);

        Assert.Equal(60, plate.Size.Width);
        Assert.Equal(120, plate.Size.Length);
    }

    [Fact]
    public void EvaluateUpgrade_PrefersCheaperOption()
    {
        var currentOption = new PlateOption { Width = 48, Length = 96, Cost = 100 };
        var upgradeOption = new PlateOption { Width = 60, Length = 120, Cost = 160 };
        var newPlateOption = new PlateOption { Width = 48, Length = 96, Cost = 100 };

        // Upgrade cost = 160 - 100 = 60
        // New plate cost with 50% utilization, 50% salvage:
        // remnantFraction = 0.5, salvageCredit = 0.5 * 100 * 0.5 = 25
        // netNewCost = 100 - 25 = 75
        // Upgrade (60) < new plate (75), so upgrade wins
        var decision = MultiPlateNester.EvaluateUpgradeVsNew(
            currentOption, upgradeOption, newPlateOption, 0.5, 0.5);

        Assert.True(decision.ShouldUpgrade);
    }

    // --- Task 7: Main Orchestration ---

    [Fact]
    public void Nest_LargePartsGetOwnPlates()
    {
        var template = new Plate(96, 48) { PartSpacing = 0.25, Quadrant = 1 };
        template.EdgeSpacing = new Spacing();

        var items = new List<NestItem>
        {
            MakeItem("big1", 80, 40, 1),
            MakeItem("big2", 70, 35, 1),
        };

        var result = MultiPlateNester.Nest(
            items, template,
            plateOptions: null,
            salvageRate: 0.5,
            sortOrder: PartSortOrder.BoundingBoxArea,
            minRemnantSize: 12.0,
            allowPlateCreation: true,
            existingPlates: null,
            progress: null,
            token: CancellationToken.None);

        // Each large part should be on its own plate.
        Assert.True(result.Plates.Count >= 2,
            $"Expected at least 2 plates, got {result.Plates.Count}");
    }

    [Fact]
    public void Nest_SmallPartsGoIntoScrapZones()
    {
        var template = new Plate(96, 48) { PartSpacing = 0.25, Quadrant = 1 };
        template.EdgeSpacing = new Spacing();

        var items = new List<NestItem>
        {
            MakeItem("big", 80, 40, 1),
            MakeItem("tiny", 5, 5, 3),
        };

        var result = MultiPlateNester.Nest(
            items, template,
            plateOptions: null,
            salvageRate: 0.5,
            sortOrder: PartSortOrder.BoundingBoxArea,
            minRemnantSize: 12.0,
            allowPlateCreation: true,
            existingPlates: null,
            progress: null,
            token: CancellationToken.None);

        // Small parts should be placed on the same plate as the big part
        // (in scrap zones), not on a new plate.
        Assert.Equal(1, result.Plates.Count);
        Assert.True(result.Plates[0].Parts.Count > 1);
    }

    [Fact]
    public void Nest_RespectsAllowPlateCreation()
    {
        var template = new Plate(96, 48) { PartSpacing = 0.25, Quadrant = 1 };
        template.EdgeSpacing = new Spacing();

        var items = new List<NestItem>
        {
            MakeItem("big1", 80, 40, 1),
            MakeItem("big2", 70, 35, 1),
        };

        var result = MultiPlateNester.Nest(
            items, template,
            plateOptions: null,
            salvageRate: 0.5,
            sortOrder: PartSortOrder.BoundingBoxArea,
            minRemnantSize: 12.0,
            allowPlateCreation: false,
            existingPlates: null,
            progress: null,
            token: CancellationToken.None);

        // No existing plates and no plate creation — nothing can be placed.
        Assert.Empty(result.Plates);
        Assert.Equal(2, result.UnplacedItems.Count);
    }

    [Fact]
    public void Nest_UsesExistingPlates()
    {
        var template = new Plate(96, 48) { PartSpacing = 0.25, Quadrant = 1 };
        template.EdgeSpacing = new Spacing();

        var existingPlate = new Plate(96, 48) { PartSpacing = 0.25, Quadrant = 1 };
        existingPlate.EdgeSpacing = new Spacing();

        // Use a part small enough to be classified as Medium on a 96x48 plate.
        // Plate WorkArea: Width=96, Length=48. Half: 48, 24.
        // Part 24x22: Length=24 (not > 24), Width=22 (not > 48) — not Large.
        // Area = 528 > 4608/9 = 512 — Medium.
        var items = new List<NestItem>
        {
            MakeItem("medium", 24, 22, 1),
        };

        var result = MultiPlateNester.Nest(
            items, template,
            plateOptions: null,
            salvageRate: 0.5,
            sortOrder: PartSortOrder.BoundingBoxArea,
            minRemnantSize: 12.0,
            allowPlateCreation: true,
            existingPlates: new List<Plate> { existingPlate },
            progress: null,
            token: CancellationToken.None);

        // Part should be placed on the existing plate, not a new one.
        Assert.Single(result.Plates);
        Assert.False(result.Plates[0].IsNew);
    }
}
