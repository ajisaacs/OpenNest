using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Tests.Sequencing;

public class DrawnPathSequencingTests
{
    private static Vector[][] Lines(params Vector[][] lines) => lines;

    [Fact]
    public void Apply_CutsPartsInTheOrderTheSnakePathReachesThem()
    {
        // Two rows of three 10x10 squares; the path runs along the bottom row
        // left to right, then back along the top row right to left.
        var drawing = TestHelpers.MakeSquareDrawing();
        var parts = new[]
        {
            new Part(drawing, new Vector(0, 0)), new Part(drawing, new Vector(20, 0)), new Part(drawing, new Vector(40, 0)),
            new Part(drawing, new Vector(0, 20)), new Part(drawing, new Vector(20, 20)), new Part(drawing, new Vector(40, 20)),
        };
        var plate = TestHelpers.MakePlate(60, 120, parts[4], parts[0], parts[5], parts[2], parts[3], parts[1]);
        var nested = drawing.Quantity.Nested;
        var path = new[] { new Vector(-5, 5), new Vector(55, 5), new Vector(55, 25), new Vector(-5, 25) };

        Assert.True(DrawnPathSequencing.Apply(plate, Lines(path)));

        Assert.Equal(new[] { parts[0], parts[1], parts[2], parts[5], parts[4], parts[3] }, plate.Parts);
        Assert.Equal(nested, drawing.Quantity.Nested);
    }

    [Fact]
    public void Apply_CutsLineByLineAndTheJumpBetweenLinesClaimsNothing()
    {
        // Both rows run left to right. The jump from the bottom row's end back to the
        // top row's start crosses the middle part of each row but is not a line.
        var drawing = TestHelpers.MakeSquareDrawing();
        var bottom = new[] { new Part(drawing, new Vector(0, 0)), new Part(drawing, new Vector(20, 0)), new Part(drawing, new Vector(40, 0)) };
        var top = new[] { new Part(drawing, new Vector(0, 20)), new Part(drawing, new Vector(20, 20)), new Part(drawing, new Vector(40, 20)) };
        var plate = TestHelpers.MakePlate(60, 120, top[2], bottom[1], top[0], bottom[2], top[1], bottom[0]);

        var lines = Lines(
            new[] { new Vector(-5, 5), new Vector(55, 5) },
            new[] { new Vector(-5, 25), new Vector(55, 25) });

        Assert.True(DrawnPathSequencing.Apply(plate, lines));

        Assert.Equal(bottom.Concat(top), plate.Parts);
    }

    [Fact]
    public void CutOrder_PreviewsApplyWithoutReorderingThePlate()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var a = new Part(drawing, new Vector(0, 0));
        var b = new Part(drawing, new Vector(20, 0));
        var c = new Part(drawing, new Vector(40, 0));
        var plate = TestHelpers.MakePlate(60, 120, c, a, b);
        var reorders = 0;
        plate.PartsReordered += (_, _) => reorders++;
        var lines = Lines(new[] { new Vector(-5, 5), new Vector(55, 5) });

        var preview = DrawnPathSequencing.CutOrder(plate, lines);

        Assert.Equal(new[] { a, b, c }, preview);
        Assert.Equal(new[] { c, a, b }, plate.Parts);
        Assert.Equal(0, reorders);
        Assert.Null(DrawnPathSequencing.CutOrder(plate, Lines(new[] { new Vector(0, 0) })));

        Assert.True(DrawnPathSequencing.Apply(plate, lines));
        Assert.Equal(preview, plate.Parts);
    }

    [Fact]
    public void Apply_PartGoesToTheNearestLineEvenWhenDrawnLater()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var a = new Part(drawing, new Vector(0, 0));
        var b = new Part(drawing, new Vector(20, 0));
        var stray = new Part(drawing, new Vector(40, 30));
        var plate = TestHelpers.MakePlate(60, 120, stray, b, a);

        // The first line passes 'stray' at a distance; the second runs right through it.
        var lines = Lines(
            new[] { new Vector(5, 5), new Vector(45, 5), new Vector(45, 20) },
            new[] { new Vector(45, 50), new Vector(45, 30) });

        Assert.True(DrawnPathSequencing.Apply(plate, lines));

        Assert.Equal(new[] { a, b, stray }, plate.Parts);
    }

    [Fact]
    public void Apply_PartPassedTwiceFollowsTheNearerPass()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var start = new Part(drawing, new Vector(0, 0));
        var passed = new Part(drawing, new Vector(40, 0));
        var top = new Part(drawing, new Vector(40, 40));
        var plate = TestHelpers.MakePlate(60, 120, passed, top, start);

        // The path skirts 'passed' on the way up to 'top', then comes back through it.
        var path = new[] { new Vector(5, 5), new Vector(45, 15), new Vector(45, 45), new Vector(45, 5) };

        Assert.True(DrawnPathSequencing.Apply(plate, Lines(path)));
        Assert.Equal(new[] { start, top, passed }, plate.Parts);

        // Reversed, the path runs through 'passed' first.
        Assert.True(DrawnPathSequencing.Apply(plate, Lines(path.Reverse().ToArray())));
        Assert.Equal(new[] { passed, top, start }, plate.Parts);
    }

    [Fact]
    public void Apply_KeepsCutFirstPartsAheadOfTheRest()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var coupon = TestHelpers.MakeSquareDrawing();
        coupon.CutFirst = true;
        var a = new Part(drawing, new Vector(0, 0));
        var b = new Part(drawing, new Vector(20, 0));
        var test = new Part(coupon, new Vector(40, 0));
        var plate = TestHelpers.MakePlate(60, 120, a, b, test);

        Assert.True(DrawnPathSequencing.Apply(plate, Lines(new[] { new Vector(55, 5), new Vector(-5, 5) })));

        Assert.Equal(new[] { test, b, a }, plate.Parts);
    }

    [Fact]
    public void Apply_IgnoresLinesWithoutLength()
    {
        var drawing = TestHelpers.MakeSquareDrawing();
        var a = new Part(drawing, new Vector(20, 0));
        var b = new Part(drawing, new Vector(0, 0));
        var plate = TestHelpers.MakePlate(60, 120, a, b);
        var reorders = 0;
        plate.PartsReordered += (_, _) => reorders++;

        Assert.False(DrawnPathSequencing.Apply(plate, Lines()));
        Assert.False(DrawnPathSequencing.Apply(plate, Lines(new[] { new Vector(0, 0) })));
        Assert.False(DrawnPathSequencing.Apply(plate, Lines(new[] { new Vector(3, 3), new Vector(3, 3) })));
        Assert.Equal(new[] { a, b }, plate.Parts);
        Assert.Equal(0, reorders);

        // A dot drawn first does not count as line one.
        Assert.True(DrawnPathSequencing.Apply(plate, Lines(new[] { new Vector(30, 5) }, new[] { new Vector(-5, 5), new Vector(35, 5) })));
        Assert.Equal(new[] { b, a }, plate.Parts);
    }
}
