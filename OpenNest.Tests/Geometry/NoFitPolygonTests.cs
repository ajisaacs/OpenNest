using Clipper2Lib;
using OpenNest.Geometry;

namespace OpenNest.Tests.Geometry;

public class NoFitPolygonTests
{
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(-5, -5, true)]
    [InlineData(2, 0.5, true)]
    [InlineData(4, 0, false)]
    [InlineData(0, -21, false)]
    [InlineData(-21, 0, false)]
    public void PortedContainmentCases(double x, double y, bool forbidden)
    {
        var a = Ring((0, 0), (3, 0), (3, 1), (1, 1), (1, 3), (0, 3));
        Assert.Equal(forbidden, Inside(NoFitPolygon.Compute(a, Square(20)), x, y));
    }

    [Fact]
    public void SquareFitsInNotch()
    {
        var a = Ring((0, 0), (5, 0), (5, 1), (1, 1), (1, 5), (0, 5));
        Assert.False(Inside(NoFitPolygon.Compute(a, Square(2)), 2, 2));
        Assert.True(Inside(NoFitPolygon.Compute(a, Square(2)), 0.5, 2));
    }

    [Fact]
    public void PerimeterOnlyNfpCannotRepresentPartInHole()
    {
        var outer = Square(10);
        var hole = Move(Square(6), 2, 2);
        var moving = Square(1);
        Assert.False(Collision.HasOverlap(outer, Move(moving, 4, 4), new List<Polygon> { hole }));
        // The API explicitly fills its single perimeter. Hole-aware callers must use Collision.
        Assert.True(Inside(NoFitPolygon.Compute(outer, moving), 4, 4));
    }

    [Fact]
    public void ConvexWindingClosureAndOriginAreNormalized()
    {
        var a = ClipperBridge.ToPath(Move(Square(3), 7, 4), true);
        var b = ClipperBridge.ToPath(Move(Square(2), 1, 2), false);
        b.Add(b[0]);
        var result = NoFitPolygon.Compute(a, b);
        Assert.Equal(25, System.Math.Abs(Clipper.Area(result)), 8);
        Assert.True(Inside(result, 6, 2));
        Assert.False(Inside(result, 10, 2));
    }

    [Fact]
    public void SeededConcavePairsAgreeAwayFromBoundary()
    {
        var random = new Random(760125);
        var decisions = 0;
        for (var pair = 0; pair < 100; pair++)
        {
            var a = Star(random);
            var b = Star(random);
            var nfp = NoFitPolygon.Compute(a, b);
            for (var sample = 0; sample < 100; sample++)
            {
                var x = random.NextDouble() * 20 - 10;
                var y = random.NextDouble() * 20 - 10;
                // Exclude a 0.01 boundary band: tiny contact wedges can have area below
                // Collision's 1e-5 area floor even beyond Clipper's 1e-4 grid.
                if (NearBoundary(nfp, x, y, 0.01))
                    continue;
                Assert.True(Collision.HasOverlap(a, Move(b, x, y)) == Inside(nfp, x, y), $"pair={pair} sample={sample} x={x:R} y={y:R}");
                decisions++;
            }
        }
        Assert.True(decisions > 9800);
    }

    internal static Polygon Star(Random random)
    {
        var points = new (double, double)[8];
        for (var i = 0; i < points.Length; i++)
        {
            var angle = i * System.Math.PI / 4;
            var radius = (i % 2 == 0 ? 3 : 1) * (0.8 + random.NextDouble() * 0.4);
            points[i] = (radius * System.Math.Cos(angle), radius * System.Math.Sin(angle));
        }
        return Ring(points);
    }

    internal static Polygon Square(double size) => Ring((0, 0), (size, 0), (size, size), (0, size));

    internal static Polygon Ring(params (double X, double Y)[] points)
    {
        var polygon = new Polygon();
        foreach (var (x, y) in points)
            polygon.Vertices.Add(new Vector(x, y));
        polygon.Close();
        polygon.UpdateBounds();
        return polygon;
    }

    internal static Polygon Move(Polygon polygon, double x, double y) =>
        ClipperBridge.ToPolygon(Clipper.TranslatePath(ClipperBridge.ToPath(polygon, new Vector()), x, y));

    private static bool Inside(PathsD region, double x, double y)
    {
        var winding = 0;
        foreach (var path in region)
            if (Clipper.PointInPolygon(new PointD(x, y), path) == PointInPolygonResult.IsInside)
                winding += Clipper.IsPositive(path) ? 1 : -1;
        return winding != 0;
    }

    private static bool NearBoundary(PathsD paths, double x, double y, double tolerance)
    {
        foreach (var path in paths)
            for (var i = 0; i < path.Count; i++)
            {
                var a = path[i];
                var b = path[(i + 1) % path.Count];
                var dx = b.x - a.x;
                var dy = b.y - a.y;
                var t = System.Math.Clamp(((x - a.x) * dx + (y - a.y) * dy) / (dx * dx + dy * dy), 0, 1);
                var ex = x - a.x - t * dx;
                var ey = y - a.y - t * dy;
                if (ex * ex + ey * ey < tolerance * tolerance)
                    return true;
            }
        return false;
    }
}

