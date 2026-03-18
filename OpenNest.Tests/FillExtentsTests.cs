using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class FillExtentsTests
{
    private static Drawing MakeRightTriangle(double w, double h)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("triangle", pgm);
    }

    private static Drawing MakeRect(double w, double h)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(w, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, h)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        return new Drawing("rect", pgm);
    }

    [Fact]
    public void Fill_Triangle_ReturnsPartsWithinWorkArea()
    {
        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing);

        Assert.NotNull(parts);
        Assert.True(parts.Count > 0, "Should place at least one part");

        foreach (var part in parts)
        {
            Assert.True(part.BoundingBox.Right <= workArea.Right + 0.01,
                $"Part right edge {part.BoundingBox.Right} exceeds work area {workArea.Right}");
            Assert.True(part.BoundingBox.Top <= workArea.Top + 0.01,
                $"Part top edge {part.BoundingBox.Top} exceeds work area {workArea.Top}");
        }
    }

    [Fact]
    public void Fill_PartTooLarge_ReturnsEmpty()
    {
        var workArea = new Box(0, 0, 5, 5);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRect(10, 10);

        var parts = filler.Fill(drawing);

        Assert.NotNull(parts);
        Assert.Empty(parts);
    }
}
