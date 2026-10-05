using System.Collections.Generic;
using System.Linq;
using OpenNest.Engine;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests.Fill;

public class FillLinearMaxPartsTests
{
    // 10 x 5 rectangles at 0.5 spacing in a 100 x 100 area: 9 per row, 18 rows.
    private static readonly Box Area = new(0, 0, 100, 100);
    private const double Spacing = 0.5;

    private static Drawing Rectangle()
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(10, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(10, 5)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 5)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    private static List<(double X, double Y)> Poses(List<Part> parts) =>
        parts.Select(p => (p.Location.X, p.Location.Y)).OrderBy(p => p.Y).ThenBy(p => p.X).ToList();

    [Fact]
    public void Uncapped_FillsTheWholeArea()
    {
        var parts = new FillLinear(Area, Spacing).Fill(Rectangle(), 0, NestDirection.Horizontal);

        Assert.Equal(9 * 18, parts.Count);
    }

    [Fact]
    public void Cap_StopsAddingRowsOnceReached_AndKeepsTheFullGridPrefix()
    {
        var drawing = Rectangle();
        var full = new FillLinear(Area, Spacing).Fill(drawing, 0, NestDirection.Horizontal);
        var capped = new FillLinear(Area, Spacing) { MaxParts = 20 }.Fill(drawing, 0, NestDirection.Horizontal);

        // Whole rows only: 20 parts need three rows of nine.
        Assert.Equal(27, capped.Count);
        var rows = Poses(full).Select(p => p.Y).Distinct().OrderBy(y => y).Take(3).ToHashSet();
        Assert.Equal(Poses(full).Where(p => rows.Contains(p.Y)).ToList(), Poses(capped));
    }

    [Fact]
    public void Cap_SmallerThanTheFirstRow_ReturnsTheCompleteFirstRow()
    {
        var capped = new FillLinear(Area, Spacing) { MaxParts = 5 }.Fill(Rectangle(), 0, NestDirection.Horizontal);

        Assert.Equal(9, capped.Count);
        Assert.Single(capped.Select(p => p.Location.Y).Distinct());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonpositiveCap_MatchesTheUncappedFill(int maxParts)
    {
        var drawing = Rectangle();
        var full = new FillLinear(Area, Spacing).Fill(drawing, 0, NestDirection.Horizontal);
        var capped = new FillLinear(Area, Spacing) { MaxParts = maxParts }.Fill(drawing, 0, NestDirection.Horizontal);

        Assert.Equal(Poses(full), Poses(capped));
    }

    [Theory]
    [InlineData(NestDirection.Horizontal, 10, 100)]
    [InlineData(NestDirection.Vertical, 100, 5)]
    public void Cap_PerpendicularOnlyFill_StopsAtQuantity(NestDirection direction, double length, double width)
    {
        var area = new Box(0, 0, length, width);
        var parts = new FillLinear(area, Spacing) { MaxParts = 3 }.Fill(Rectangle(), 0, direction);

        Assert.Equal(3, parts.Count);
        var plate = new Plate(width, length) { PartSpacing = Spacing };
        plate.Parts.AddRange(parts);
        Assert.False(plate.HasOverlappingParts(out _));
        Assert.All(parts, part => Assert.True(part.BoundingBox.Right <= area.Right
            && part.BoundingBox.Top <= area.Top));
    }

    [Fact]
    public void Cap_DoesNotPlaceAPartThatCannotFit()
    {
        Assert.Empty(new FillLinear(new Box(0, 0, 4, 4), Spacing) { MaxParts = 1 }
            .Fill(Rectangle(), 0, NestDirection.Horizontal));
    }

    [Fact]
    public void Cap_LargerThanTheAreaHolds_MatchesTheUncappedFill()
    {
        var drawing = Rectangle();
        var full = new FillLinear(Area, Spacing).Fill(drawing, 0, NestDirection.Horizontal);
        var capped = new FillLinear(Area, Spacing) { MaxParts = 1000 }.Fill(drawing, 0, NestDirection.Horizontal);

        Assert.Equal(Poses(full), Poses(capped));
    }
}
