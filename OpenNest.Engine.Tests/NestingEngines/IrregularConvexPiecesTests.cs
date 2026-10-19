using Clipper2Lib;
using OpenNest.Engine.NestingEngines.Irregular;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularConvexPiecesTests
{
    public static IEnumerable<object[]> Polygons()
    {
        yield return new object[] { "square", Path((0, 0), (4, 0), (4, 4), (0, 4)) };
        yield return new object[] { "ell", Path((0, 0), (3, 0), (3, 1), (1, 1), (1, 3), (0, 3)) };
        yield return new object[] { "chamber", Path((0, 0), (10, 0), (10, 4), (8, 4), (8, 2), (2, 2),
            (2, 8), (8, 8), (8, 6), (10, 6), (10, 10), (0, 10)) };
        yield return new object[] { "star", Path((11.1566, 0), (2.9107, 5.0415), (-5.8475, 10.1282),
            (-5.5736, 0), (-5.3618, -9.287), (2.1573, -3.7365)) };
        yield return new object[] { "comb", Path((0, 0), (12, 0), (12, 6), (10, 6), (10, 2), (8, 2), (8, 6),
            (6, 6), (6, 2), (4, 2), (4, 6), (2, 6), (2, 2), (0, 2)) };
        yield return new object[] { "collinear", Path((0, 0), (2, 0), (4, 0), (4, 2), (4, 4), (2, 4), (0, 4), (0, 2)) };
        yield return new object[] { "clockwise", Path((0, 0), (0, 3), (1, 3), (1, 1), (3, 1), (3, 0)) };
    }

    [Theory]
    [MemberData(nameof(Polygons))]
    public void PiecesAreConvexAndTileThePolygon(string name, PathD polygon)
    {
        var pieces = ConvexPieces.Decompose(polygon, NoFitCache.Precision);

        Assert.NotNull(pieces);
        Assert.All(pieces!, piece => Assert.True(IsConvex(piece), $"{name}: a piece is not convex"));
        Assert.All(pieces!, piece => Assert.True(Clipper.IsPositive(piece), $"{name}: a piece is not CCW"));
        var union = Clipper.Union(new PathsD(pieces!), new PathsD(), FillRule.NonZero, NoFitCache.Precision);
        var positive = new PathsD { Clipper.IsPositive(polygon) ? polygon : Reversed(polygon) };
        var mismatch = Clipper.Xor(union, positive, FillRule.NonZero, NoFitCache.Precision);
        Assert.True(System.Math.Abs(Clipper.Area(mismatch)) < 1e-6, $"{name}: pieces do not tile the polygon");
        Assert.True(System.Math.Abs(pieces!.Sum(p => Clipper.Area(p)) - System.Math.Abs(Clipper.Area(polygon))) < 1e-6,
            $"{name}: pieces overlap or leave gaps");
    }

    [Fact]
    public void ConvexPolygonIsOnePieceAndMergingBeatsTriangles()
    {
        Assert.Single(ConvexPieces.Decompose(Path((0, 0), (4, 0), (4, 4), (0, 4)), NoFitCache.Precision)!);
        // A comb with five teeth needs few convex pieces, far fewer than its 12 triangles.
        var comb = ConvexPieces.Decompose(Path((0, 0), (12, 0), (12, 6), (10, 6), (10, 2), (8, 2), (8, 6),
            (6, 6), (6, 2), (4, 2), (4, 6), (2, 6), (2, 2), (0, 2)), NoFitCache.Precision);
        Assert.InRange(comb!.Count, 2, 7);
    }

    [Fact]
    public void DegeneratePolygonsAreDeclined()
    {
        Assert.Null(ConvexPieces.Decompose(Path((0, 0), (1, 0)), NoFitCache.Precision));
        Assert.Null(ConvexPieces.Decompose(Path((0, 0), (1, 0), (2, 0)), NoFitCache.Precision));
        // A bow tie is not simple: the clipper runs out of ears.
        Assert.Null(ConvexPieces.Decompose(Path((0, 0), (2, 2), (2, 0), (0, 2)), NoFitCache.Precision));
    }

    [Theory]
    [MemberData(nameof(ConcavePairs))]
    public void PiecewiseNoFitPolygonMatchesTheEdgeSweep(string name, PathD fixedOutline, PathD movingOutline)
    {
        // Edge-pair parallelograms plus containment covers, as the cache built before pieces.
        var expected = EdgeSweep(fixedOutline, movingOutline);
        var cache = new NoFitCache(0.25);
        var actual = cache.Get(Orientation(0, fixedOutline), Orientation(1, movingOutline)).Region;

        var mismatch = Clipper.Xor(actual, expected, FillRule.NonZero, NoFitCache.Precision);
        var area = System.Math.Abs(Clipper.Area(expected));
        Assert.True(System.Math.Abs(Clipper.Area(mismatch)) <= 1e-6 * System.Math.Max(1, area),
            $"{name}: piecewise NFP differs from the edge sweep by {Clipper.Area(mismatch)}");
    }

    public static IEnumerable<object[]> ConcavePairs()
    {
        var ell = Path((0, 0), (3, 0), (3, 1), (1, 1), (1, 3), (0, 3));
        var chamber = Path((0, 0), (10, 0), (10, 4), (8, 4), (8, 2), (2, 2), (2, 8), (8, 8), (8, 6), (10, 6), (10, 10), (0, 10));
        var square = Path((0, 0), (2.5, 0), (2.5, 2.5), (0, 2.5));
        var comb = Path((0, 0), (12, 0), (12, 6), (10, 6), (10, 2), (8, 2), (8, 6), (6, 6), (6, 2), (4, 2), (4, 6), (2, 6), (2, 2), (0, 2));
        var wedge = Path((0, 0), (6, 0), (6, 3), (3, 1), (0, 3));
        yield return new object[] { "ell-square", ell, square };
        yield return new object[] { "square-ell", square, ell };
        yield return new object[] { "ell-ell", ell, ell };
        yield return new object[] { "chamber-square", chamber, square };
        yield return new object[] { "comb-wedge", comb, wedge };
        yield return new object[] { "wedge-comb", wedge, comb };
        yield return new object[] { "comb-comb", comb, comb };
    }

    private static PathsD EdgeSweep(PathD fixedOutline, PathD movingOutline)
    {
        var cache = new NoFitCache(0.25);
        var a = cache.Footprint(Orientation(0, fixedOutline));
        var b = cache.Footprint(Orientation(1, movingOutline));
        var negB = new PathD(b.Select(p => new PointD(-p.x, -p.y)));
        var pieces = new PathsD
        {
            Clipper.TranslatePath(a, negB[0].x, negB[0].y),
            Clipper.TranslatePath(negB, a[0].x, a[0].y),
        };
        var previousA = a[^1];
        foreach (var currentA in a)
        {
            var previousB = negB[^1];
            foreach (var currentB in negB)
            {
                var quad = new PathD
                {
                    new(previousA.x + previousB.x, previousA.y + previousB.y),
                    new(currentA.x + previousB.x, currentA.y + previousB.y),
                    new(currentA.x + currentB.x, currentA.y + currentB.y),
                    new(previousA.x + currentB.x, previousA.y + currentB.y),
                };
                if (!Clipper.IsPositive(quad))
                    quad.Reverse();
                pieces.Add(quad);
                previousB = currentB;
            }
            previousA = currentA;
        }
        var region = Clipper.Union(pieces, new PathsD(), FillRule.NonZero, NoFitCache.Precision);
        NfpHoleFilter.RemoveCoveredHoles(region, a, negB);
        return region;
    }

    private static bool IsConvex(PathD path)
    {
        for (var i = 0; i < path.Count; i++)
        {
            var a = path[i];
            var b = path[(i + 1) % path.Count];
            var c = path[(i + 2) % path.Count];
            if ((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x) < -1e-9)
                return false;
        }
        return true;
    }

    private static PathD Reversed(PathD path)
    {
        var copy = new PathD(path);
        copy.Reverse();
        return copy;
    }

    private static PathD Path(params (double X, double Y)[] points) =>
        new(points.Select(p => new PointD(p.X, p.Y)));

    private static Orientation Orientation(int type, PathD outline)
    {
        var bounds = Clipper.GetBounds(outline);
        return new Orientation
        {
            TypeIndex = type,
            Index = 0,
            Rotation = 0,
            Outline = Clipper.IsPositive(outline) ? outline : Reversed(outline),
            Tolerance = 0.002,
            MinX = bounds.left - 0.002,
            MinY = bounds.top - 0.002,
            MaxX = bounds.right + 0.002,
            MaxY = bounds.bottom + 0.002,
        };
    }
}
