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
}
