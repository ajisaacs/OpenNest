using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;
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
}
