using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PolyLabelTests
{
    private static Polygon Square(double size)
    {
        var p = new Polygon();
        p.Vertices.Add(new Vector(0, 0));
        p.Vertices.Add(new Vector(size, 0));
        p.Vertices.Add(new Vector(size, size));
        p.Vertices.Add(new Vector(0, size));
        return p;
    }

    [Fact]
    public void Square_ReturnsCenterPoint()
    {
        var poly = Square(100);

        var result = PolyLabel.Find(poly);

        Assert.Equal(50, result.X, 1.0);
        Assert.Equal(50, result.Y, 1.0);
    }

    [Fact]
    public void Triangle_ReturnsIncenter()
    {
        var p = new Polygon();
        p.Vertices.Add(new Vector(0, 0));
        p.Vertices.Add(new Vector(100, 0));
        p.Vertices.Add(new Vector(50, 86.6));

        var result = PolyLabel.Find(p);

        // Incenter of equilateral triangle is at (50, ~28.9)
        Assert.Equal(50, result.X, 1.0);
        Assert.Equal(28.9, result.Y, 1.0);
        Assert.True(p.ContainsPoint(result));
    }

    [Fact]
    public void LShape_ReturnsPointInBottomLobe()
    {
        // L-shape: 100x100 with 50x50 cut from top-right
        var p = new Polygon();
        p.Vertices.Add(new Vector(0, 0));
        p.Vertices.Add(new Vector(100, 0));
        p.Vertices.Add(new Vector(100, 50));
        p.Vertices.Add(new Vector(50, 50));
        p.Vertices.Add(new Vector(50, 100));
        p.Vertices.Add(new Vector(0, 100));

        var result = PolyLabel.Find(p);

        Assert.True(p.ContainsPoint(result));
        // The bottom 100x50 lobe is the widest region
        Assert.True(result.Y < 50, $"Expected label in bottom lobe, got Y={result.Y}");
    }

    [Fact]
    public void ThinRectangle_CenteredOnBothAxes()
    {
        var p = new Polygon();
        p.Vertices.Add(new Vector(0, 0));
        p.Vertices.Add(new Vector(200, 0));
        p.Vertices.Add(new Vector(200, 10));
        p.Vertices.Add(new Vector(0, 10));

        var result = PolyLabel.Find(p);

        Assert.Equal(100, result.X, 1.0);
        Assert.Equal(5, result.Y, 1.0);
        Assert.True(p.ContainsPoint(result));
    }

    [Fact]
    public void SquareWithLargeHole_AvoidsHole()
    {
        var outer = Square(100);

        var hole = new Polygon();
        hole.Vertices.Add(new Vector(20, 20));
        hole.Vertices.Add(new Vector(80, 20));
        hole.Vertices.Add(new Vector(80, 80));
        hole.Vertices.Add(new Vector(20, 80));

        var result = PolyLabel.Find(outer, new[] { hole });

        // Point should be inside outer but outside hole
        Assert.True(outer.ContainsPoint(result));
        Assert.False(hole.ContainsPoint(result));
    }

    [Fact]
    public void CShape_ReturnsPointInLeftBar()
    {
        // C-shape opening to the right: left bar is 20 wide, top/bottom arms are 20 tall
        var p = new Polygon();
        p.Vertices.Add(new Vector(0, 0));
        p.Vertices.Add(new Vector(100, 0));
        p.Vertices.Add(new Vector(100, 20));
        p.Vertices.Add(new Vector(20, 20));
        p.Vertices.Add(new Vector(20, 80));
        p.Vertices.Add(new Vector(100, 80));
        p.Vertices.Add(new Vector(100, 100));
        p.Vertices.Add(new Vector(0, 100));

        var result = PolyLabel.Find(p);

        Assert.True(p.ContainsPoint(result));
        // Label should be in the left vertical bar (x < 20), not at bbox center (50, 50)
        Assert.True(result.X < 20, $"Expected label in left bar, got X={result.X}");
    }

    [Fact]
    public void DegeneratePolygon_ReturnsFallback()
    {
        var p = new Polygon();
        p.Vertices.Add(new Vector(5, 5));

        var result = PolyLabel.Find(p);

        Assert.Equal(5, result.X, 0.01);
        Assert.Equal(5, result.Y, 0.01);
    }
}
