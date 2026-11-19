using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Desktop.Tests;

public class DrawingSortTests
{
    // "big" has the largest area; "ell" the largest bounding box but, being an L, less area.
    private readonly Drawing big = new("big", Rectangle(20, 20));
    private readonly Drawing ell = new("ell", LShape(30, 30, 2));
    private readonly Drawing small = new("small", Rectangle(5, 5));

    [Fact]
    public void NameSortsAlphabetically()
    {
        var arranged = DrawingSort.Arrange(new[] { small, ell, big }, DrawingSortOrder.Name, false);
        Assert.Equal(new[] { "big", "ell", "small" }, Names(arranged));
    }

    [Fact]
    public void AreaPutsTheLargestAreaFirst()
    {
        Assert.True(big.Area > ell.Area);
        var arranged = DrawingSort.Arrange(new[] { small, ell, big }, DrawingSortOrder.Area, false);
        Assert.Equal(new[] { "big", "ell", "small" }, Names(arranged));
    }

    [Fact]
    public void BoundingBoxPutsTheLargestBoxFirst()
    {
        var arranged = DrawingSort.Arrange(new[] { small, big, ell }, DrawingSortOrder.BoundingBox, false);
        Assert.Equal(new[] { "ell", "big", "small" }, Names(arranged));
    }

    [Fact]
    public void RemainingPutsTheMostLeftToNestFirstAndBreaksTiesByName()
    {
        var nest = new Nest("sort");
        var plate = new Plate(100, 100) { Quantity = 1 };
        nest.Plates.Add(plate);
        foreach (var (drawing, required) in new[] { (big, 4), (ell, 2), (small, 2) })
        {
            drawing.Quantity.Required = required;
            nest.Drawings.Add(drawing);
        }
        plate.Parts.Add(new Part(big));
        Assert.Equal(3, big.Quantity.Remaining);

        var arranged = DrawingSort.Arrange(nest.Drawings, DrawingSortOrder.Remaining, false);
        Assert.Equal(new[] { "big", "ell", "small" }, Names(arranged));

        plate.Parts.Add(new Part(big));
        plate.Parts.Add(new Part(big));
        arranged = DrawingSort.Arrange(nest.Drawings, DrawingSortOrder.Remaining, false);
        Assert.Equal(new[] { "ell", "small", "big" }, Names(arranged));
    }

    [Fact]
    public void HidingFullyNestedLeavesOutOnlyDrawingsWhoseQuantityIsMet()
    {
        var nest = new Nest("hide");
        var plate = new Plate(100, 100) { Quantity = 1 };
        nest.Plates.Add(plate);
        big.Quantity.Required = 1;
        ell.Quantity.Required = 2;
        small.Quantity.Required = 0;
        nest.Drawings.Add(big);
        nest.Drawings.Add(ell);
        nest.Drawings.Add(small);
        plate.Parts.Add(new Part(big));
        plate.Parts.Add(new Part(ell));

        var arranged = DrawingSort.Arrange(nest.Drawings, DrawingSortOrder.Area, true);

        Assert.Equal(new[] { "ell", "small" }, Names(arranged));
    }

    [Theory]
    [InlineData("Area", DrawingSortOrder.Area)]
    [InlineData("BoundingBox", DrawingSortOrder.BoundingBox)]
    [InlineData("Remaining", DrawingSortOrder.Remaining)]
    [InlineData("Name", DrawingSortOrder.Name)]
    [InlineData("", DrawingSortOrder.Name)]
    [InlineData(null, DrawingSortOrder.Name)]
    [InlineData("Bogus", DrawingSortOrder.Name)]
    [InlineData("42", DrawingSortOrder.Name)]
    public void ParseReadsTheStoredNameAndFallsBackToName(string stored, DrawingSortOrder expected) =>
        Assert.Equal(expected, DrawingSort.Parse(stored));

    [Fact]
    public void EveryOrderHasOneMenuChoice() =>
        Assert.Equal(Enum.GetValues<DrawingSortOrder>(), DrawingSort.Choices.Select(c => c.Order).ToArray());

    private static string[] Names(IEnumerable<Drawing> drawings) => drawings.Select(d => d.Name).ToArray();

    private static Program Rectangle(double width, double length) =>
        Polygon(new Vector(0, 0), new Vector(width, 0), new Vector(width, length), new Vector(0, length));

    private static Program LShape(double width, double length, double leg) =>
        Polygon(
            new Vector(0, 0), new Vector(width, 0), new Vector(width, leg),
            new Vector(leg, leg), new Vector(leg, length), new Vector(0, length));

    private static Program Polygon(params Vector[] points)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(points[0]));
        foreach (var point in points.Skip(1))
            program.Codes.Add(new LinearMove(point));
        program.Codes.Add(new LinearMove(points[0]));
        return program;
    }
}
