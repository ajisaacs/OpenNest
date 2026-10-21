using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class RotatingCalipersTests
{
    private static readonly Vector[] Triangle = { new(0, 0), new(10, 0), new(12, 1) };

    private static Polygon HullStartingAt(Vector[] vertices, int start)
    {
        var hull = new Polygon();
        for (var i = 0; i < vertices.Length; i++)
            hull.Vertices.Add(vertices[(start + i) % vertices.Length]);
        return hull;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AsymmetricHull_FindsTheBestEdgeInAnyVertexOrder(int start)
    {
        // The edge (12,1)-(0,0) gives area 10; the others give 12 and 50. Starting at
        // (0,0), that edge is last, in the half the opposite-edge shortcut skipped.
        var result = RotatingCalipers.MinimumBoundingRectangle(HullStartingAt(Triangle, start));

        Assert.Equal(10, result.Area, 9);
        Assert.Equal(System.Math.Sqrt(145), result.Width, 9);
        Assert.Equal(System.Math.Atan2(-1, -12), result.Angle, 9);
    }

    [Fact]
    public void PointList_FindsTheBestEdgeOfItsHull()
    {
        var result = RotatingCalipers.MinimumBoundingRectangle(Triangle);

        Assert.Equal(10, result.Area, 9);
    }

    [Fact]
    public void SymmetricHull_KeepsAFirstHalfEdgeOrientation()
    {
        // A rotated rectangle's opposite edges tie up to rounding; the result must stay
        // on the first two edges rather than flip 180° onto the last two.
        var angle = 0.4;
        var corners = new[] { new Vector(0, 0), new Vector(30, 0), new Vector(30, 7), new Vector(0, 7) };
        var hull = new Polygon();
        foreach (var corner in corners)
            hull.Vertices.Add(corner.Rotate(angle));

        var result = RotatingCalipers.MinimumBoundingRectangle(hull);

        Assert.Equal(210, result.Area, 6);
        var firstHalfAngles = new[] { angle, angle + System.Math.PI / 2 }.Select(a => System.Math.Round(a, 9));
        Assert.Contains(System.Math.Round(result.Angle, 9), firstHalfAngles);
    }
}
