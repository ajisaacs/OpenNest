using OpenNest.CNC;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests.Fill;

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

    [Fact]
    public void Fill_Triangle_ColumnFillsHeight()
    {
        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing);

        Assert.True(parts.Count > 0);

        // The topmost part should be close to the work area top edge.
        var topEdge = 0.0;
        foreach (var part in parts)
        {
            if (part.BoundingBox.Top > topEdge)
                topEdge = part.BoundingBox.Top;
        }

        // After adjustment, the gap should be small (within one part spacing).
        var gap = workArea.Top - topEdge;
        Assert.True(gap < 1.0,
            $"Gap of {gap:F2} is too large — adjustment should fill close to the top");
    }

    [Fact]
    public void Fill_Triangle_FillsWidthWithMultipleColumns()
    {
        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing);

        // With a 120-wide sheet and ~10-wide parts, we should get multiple columns.
        Assert.True(parts.Count >= 8,
            $"Expected multiple columns but got only {parts.Count} parts");

        // Verify all parts are within bounds.
        foreach (var part in parts)
        {
            Assert.True(part.BoundingBox.Right <= workArea.Right + 0.01);
            Assert.True(part.BoundingBox.Top <= workArea.Top + 0.01);
            Assert.True(part.BoundingBox.Left >= workArea.Left - 0.01);
            Assert.True(part.BoundingBox.Bottom >= workArea.Bottom - 0.01);
        }
    }

    [Fact]
    public void Fill_Rect_ReturnsNonEmpty()
    {
        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRect(15, 10);

        var parts = filler.Fill(drawing);

        Assert.NotNull(parts);
        Assert.True(parts.Count > 0, "Rectangle should produce results");
    }

    [Fact]
    public void Fill_NonZeroOriginWorkArea_PartsWithinBounds()
    {
        // Simulate a remnant sub-region with non-zero origin.
        var workArea = new Box(30, 10, 80, 40);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing);

        Assert.True(parts.Count > 0);

        foreach (var part in parts)
        {
            Assert.True(part.BoundingBox.Left >= workArea.Left - 0.01,
                $"Part left {part.BoundingBox.Left} below work area left {workArea.Left}");
            Assert.True(part.BoundingBox.Bottom >= workArea.Bottom - 0.01,
                $"Part bottom {part.BoundingBox.Bottom} below work area bottom {workArea.Bottom}");
            Assert.True(part.BoundingBox.Right <= workArea.Right + 0.01);
            Assert.True(part.BoundingBox.Top <= workArea.Top + 0.01);
        }
    }

    [Fact]
    public void Fill_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var workArea = new Box(0, 0, 120, 60);
        var filler = new FillExtents(workArea, 0.5);
        var drawing = MakeRightTriangle(10, 8);

        var parts = filler.Fill(drawing, token: cts.Token);

        Assert.NotNull(parts);
    }
}
