using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class OpenPathMaterialTests
{
    private static Polygon Rectangle(double left, double bottom, double right, double top)
    {
        var polygon = new Polygon();
        polygon.Vertices.AddRange(new[]
        {
            new Vector(left, bottom), new Vector(right, bottom),
            new Vector(right, top), new Vector(left, top), new Vector(left, bottom),
        });
        polygon.UpdateBounds();
        return polygon;
    }

    [Fact]
    public void SlopedPathThroughSquareMaterialHasPositiveInteriorLength()
    {
        var square = Rectangle(40, 40, 42, 42);
        var path = new List<Line> { new(new Vector(20, 40), new Vector(70, 42)) };
        // At x=41, y=40.84 is strictly inside [40,42]^2.
        Assert.True(OpenPathMaterial.Enters(square, null, path));
    }

    [Fact]
    public void WhollyContainedSegmentIsNotMisreadAsClearBoundaryGap()
    {
        var square = Rectangle(0, 0, 10, 10);
        var path = new List<Line> { new(new Vector(3, 5), new Vector(7, 5)) };
        Assert.True(OpenPathMaterial.Enters(square, null, path));
    }

    [Fact]
    public void BoundaryContactAndHoleInteriorRemainClear()
    {
        var square = Rectangle(0, 0, 10, 10);
        var alongBoundary = new List<Line> { new(new Vector(-1, 10), new Vector(11, 10)) };
        Assert.False(OpenPathMaterial.Enters(square, null, alongBoundary));

        var hole = Rectangle(4, 4, 6, 6);
        var insideHole = new List<Line> { new(new Vector(4.5, 5), new Vector(5.5, 5)) };
        Assert.False(OpenPathMaterial.Enters(square, new List<Polygon> { hole }, insideHole));
        var crossingHoleWall = new List<Line> { new(new Vector(5, 5), new Vector(7, 5)) };
        Assert.True(OpenPathMaterial.Enters(square, new List<Polygon> { hole }, crossingHoleWall));
    }

    [Fact]
    public void ObliquePathCanHaveOverlappingBoxesWithoutEnteringMaterial()
    {
        var square = Rectangle(40, 40, 42, 42);
        var oblique = new List<Line> { new(new Vector(18, 32), new Vector(68, 64)) };
        // At x=40 the line is y=46.08, not inside this square.
        Assert.True(oblique[0].BoundingBox.Left < square.BoundingBox.Right);
        Assert.True(oblique[0].BoundingBox.Bottom < square.BoundingBox.Top);
        Assert.False(OpenPathMaterial.Enters(square, null, oblique));
    }
}
