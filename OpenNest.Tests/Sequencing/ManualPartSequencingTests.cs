using OpenNest.Geometry;
using OpenNest.Sequencing;

namespace OpenNest.Tests.Sequencing;

public class ManualPartSequencingTests
{
    [Fact]
    public void Move_SetsExactOneBasedSequenceWithoutChangingQuantity()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var first = new Part(drawing, new Vector(0, 0));
        var second = new Part(drawing, new Vector(20, 0));
        var third = new Part(drawing, new Vector(40, 0));
        var plate = TestHelpers.MakePlate(60, 120, first, second, third);
        var nested = drawing.Quantity.Nested;
        var reorders = 0;
        plate.PartsReordered += (_, _) => reorders++;

        Assert.True(ManualPartSequencing.Move(plate, third, 1));
        Assert.Equal(new[] { third, first, second }, plate.Parts);
        Assert.True(ManualPartSequencing.Move(plate, first, 3));
        Assert.Equal(new[] { third, second, first }, plate.Parts);
        Assert.Equal(nested, drawing.Quantity.Nested);
        Assert.Equal(2, reorders);
    }

    [Fact]
    public void Move_RejectsAbsentPartAndOutOfRangeWithoutChangingPlate()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var part = new Part(drawing, new Vector(0, 0));
        var plate = TestHelpers.MakePlate(60, 120, part);
        var reorders = 0;
        plate.PartsReordered += (_, _) => reorders++;

        Assert.False(ManualPartSequencing.Move(plate, part, 0));
        Assert.False(ManualPartSequencing.Move(plate, part, 2));
        Assert.False(ManualPartSequencing.Move(plate, new Part(drawing, new Vector(0, 0)), 1));
        Assert.Equal(new[] { part }, plate.Parts);
        Assert.Equal(0, reorders);
    }
}
