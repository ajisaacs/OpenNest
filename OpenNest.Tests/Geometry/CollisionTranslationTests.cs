using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class CollisionTranslationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Check_ContainedClockwisePolygonFarFromOriginDoesNotDependOnOperandOrder(bool swap)
    {
        var outer = Square(0, 0, 1e9);
        var small = Square(999999998, 999999998, 1);
        small.Reverse();

        var result = swap ? Collision.Check(small, outer) : Collision.Check(outer, small);

        Assert.True(result.Overlaps);
        // Do not use Polygon.Area here: this test isolates triangulation, not that legacy API.
        Assert.Equal(1, result.OverlapRegions.Sum(StableArea), 8);
    }

    [Fact]
    public void Check_ClockwiseHoleFarFromOriginIsSubtracted()
    {
        var outer = Square(0, 0, 1e9);
        var hole = Square(999999998, 999999998, 1);
        hole.Reverse();
        var insert = Square(999999998.25, 999999998.25, 0.5);

        var result = Collision.Check(outer, insert, new List<Polygon> { hole });

        Assert.False(result.Overlaps);
        Assert.Empty(result.OverlapRegions);
    }

    [Theory]
    [InlineData(1000000000)]
    [InlineData(-1000000000)]
    public void Triangulate_ClockwiseTranslatedUnitSquareHasFullArea(double offset)
    {
        var polygon = Square(offset, offset, 1);
        polygon.Reverse();
        var triangles = ConvexDecomposition.Triangulate(polygon);
        Assert.Equal(2, triangles.Count);
        Assert.Equal(1, triangles.Sum(StableArea), 8);
    }

    private static Polygon Square(double x, double y, double size)
    {
        var polygon = new Polygon();
        polygon.Vertices.AddRange(new[] { new Vector(x, y), new Vector(x + size, y),
            new Vector(x + size, y + size), new Vector(x, y + size), new Vector(x, y) });
        polygon.UpdateBounds();
        return polygon;
    }

    private static double StableArea(Polygon polygon)
    {
        var area = 0.0;
        var origin = polygon.Vertices[0];
        for (var i = 1; i + 1 < polygon.Vertices.Count; i++)
        {
            var a = polygon.Vertices[i] - origin;
            var b = polygon.Vertices[i + 1] - origin;
            area += a.X * b.Y - a.Y * b.X;
        }
        return System.Math.Abs(area) / 2;
    }
}
