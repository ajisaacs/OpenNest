using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class MaximalRectanglesTests
{
    [Fact]
    public void InRegion_Rectangle_ReturnsItself()
    {
        var result = MaximalRectangles.InRegion(Region(Rect(2, 3, 12, 8)));

        var box = Assert.Single(result);
        AssertBox(box, 2, 3, 10, 5);
    }

    [Fact]
    public void InRegion_LShape_ReturnsBothArmsExactly()
    {
        var lShape = Path(0, 0, 10, 0, 10, 4, 4, 4, 4, 10, 0, 10);

        var result = MaximalRectangles.InRegion(Region(lShape));

        Assert.Equal(2, result.Count);
        Assert.Contains(result, box => Matches(box, 0, 0, 10, 4));
        Assert.Contains(result, box => Matches(box, 0, 0, 4, 10));
    }

    [Fact]
    public void InRegion_FrameWithHole_ReturnsTheFourSides()
    {
        var hole = Rect(5, 5, 15, 15);
        hole.Reverse();

        var result = MaximalRectangles.InRegion(Region(Rect(0, 0, 20, 20), hole));

        Assert.Equal(4, result.Count);
        Assert.Contains(result, box => Matches(box, 0, 0, 20, 5));
        Assert.Contains(result, box => Matches(box, 0, 15, 20, 5));
        Assert.Contains(result, box => Matches(box, 0, 0, 5, 20));
        Assert.Contains(result, box => Matches(box, 15, 0, 5, 20));
    }

    [Fact]
    public void InRegion_RoundHole_FindsNearlyTheInscribedSquare()
    {
        // The largest rectangle in a circle of radius 5 is a square of area 2r^2 = 50.
        var circle = Circle(10, 10, 5, 256);

        var result = MaximalRectangles.InRegion(Region(circle));

        Assert.NotEmpty(result);
        Assert.InRange(result[0].Area(), 47.5, 50 + 1e-9);
        AssertAllInside(result, Region(circle));
    }

    [Fact]
    public void InRegion_Diamond_NeedsDivisionsToFollowSlantedEdges()
    {
        // Vertex lines alone cut the diamond into four cells, each crossed by an edge. Even
        // lines let the staircase reach the inscribed square, whose corners touch the edges.
        var diamond = Region(Path(5, 0, 10, 5, 5, 10, 0, 5));

        Assert.Empty(MaximalRectangles.InRegion(diamond, divisions: 1));
        var result = MaximalRectangles.InRegion(diamond, divisions: 16);
        AssertBox(result[0], 2.5, 2.5, 5, 5);
    }

    [Fact]
    public void InRegion_SlantedEdges_NeverCrossTheBoundary()
    {
        // Rotated square and a star: every edge is slanted, so every result is a staircase fit.
        var diamond = Path(5, 0, 10, 5, 5, 10, 0, 5);
        var star = new PathD();
        for (var i = 0; i < 10; i++)
        {
            var angle = System.Math.PI * i / 5 + 0.1;
            var radius = i % 2 == 0 ? 10 : 4;
            star.Add(new PointD(radius * System.Math.Cos(angle), radius * System.Math.Sin(angle)));
        }

        foreach (var region in new[] { Region(diamond), Region(star) })
        {
            var result = MaximalRectangles.InRegion(region, divisions: 16);

            Assert.NotEmpty(result);
            AssertAllInside(result, region);
        }
    }

    [Fact]
    public void InRegion_MinDimension_DropsNarrowRectangles()
    {
        var lShape = Path(0, 0, 10, 0, 10, 4, 4, 4, 4, 10, 0, 10);

        var result = MaximalRectangles.InRegion(Region(lShape), minDimension: 5);

        Assert.Empty(result);
    }

    [Fact]
    public void InRegion_EmptyRegion_ReturnsNothing()
    {
        Assert.Empty(MaximalRectangles.InRegion(new PathsD()));
    }

    private static PathsD Region(params PathD[] paths) => new(paths);

    private static PathD Rect(double left, double bottom, double right, double top) =>
        Path(left, bottom, right, bottom, right, top, left, top);

    private static PathD Path(params double[] coordinates)
    {
        var path = new PathD();
        for (var i = 0; i < coordinates.Length; i += 2)
            path.Add(new PointD(coordinates[i], coordinates[i + 1]));
        return path;
    }

    private static PathD Circle(double x, double y, double radius, int vertices)
    {
        var path = new PathD();
        for (var i = 0; i < vertices; i++)
        {
            var angle = 2 * System.Math.PI * i / vertices;
            path.Add(new PointD(x + radius * System.Math.Cos(angle), y + radius * System.Math.Sin(angle)));
        }
        return path;
    }

    private static bool Matches(Box box, double x, double y, double length, double width) =>
        System.Math.Abs(box.X - x) < 1e-9
        && System.Math.Abs(box.Y - y) < 1e-9
        && System.Math.Abs(box.Length - length) < 1e-9
        && System.Math.Abs(box.Width - width) < 1e-9;

    private static void AssertBox(Box box, double x, double y, double length, double width) =>
        Assert.True(
            Matches(box, x, y, length, width),
            $"Expected ({x}, {y}) {length} x {width}, got ({box.X}, {box.Y}) {box.Length} x {box.Width}."
        );

    private static void AssertAllInside(IEnumerable<Box> boxes, PathsD region)
    {
        foreach (var box in boxes)
        {
            var outside = Clipper.Difference(
                Region(Rect(box.Left, box.Bottom, box.Right, box.Top)),
                region,
                FillRule.EvenOdd,
                8
            );
            Assert.True(
                Clipper.Area(outside) < 1e-9,
                $"({box.X}, {box.Y}) {box.Length} x {box.Width} leaves the region by {Clipper.Area(outside)}."
            );
        }
    }
}
