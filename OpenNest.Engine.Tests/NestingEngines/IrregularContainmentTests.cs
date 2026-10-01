using Clipper2Lib;
using OpenNest.Engine.NestingEngines.Irregular;

namespace OpenNest.Engine.Tests.NestingEngines;

public class IrregularContainmentTests
{
    [Fact]
    public void ConcaveContainmentDoesNotCreateSpuriousFreeRegions()
    {
        // Synthetic radial profiles, both star-shaped about the origin. Their filled
        // Minkowski sum is star-shaped too, so it cannot contain an enclosed free region.
        // Rounding the boundary sweep before adding containment used to leave a thin hole.
        var large = Orientation(0,
            (11.1566, 0), (2.9107, 5.0415), (-5.8475, 10.1282),
            (-5.5736, 0), (-5.3618, -9.287), (2.1573, -3.7365));
        var small = Orientation(1,
            (3.53487, 0), (0.95703, 0.95703), (0, 3.50634),
            (-0.86934, 0.86934), (-3.05031, 0), (-1.06221, -1.06221),
            (0, -3.56937), (0.97029, -0.97029));
        var cache = new NoFitCache(0.25);

        var nfp = cache.Get(large, small);

        Assert.Single(nfp.Region);
        Assert.True(Clipper.IsPositive(nfp.Region[0]));
        Assert.Equal(PointInPolygonResult.IsInside,
            Clipper.PointInPolygon(new PointD(0, 0), nfp.Region[0], NoFitCache.Precision));
    }

    [Fact]
    public void NarrowEntranceStillPreservesAUsableConcavePocket()
    {
        // The opening is narrower than the moving square, but the chamber is larger.
        // A legal static placement inside it is an enclosed hole in configuration space,
        // not a material cutout. Removing every negative NFP ring would lose this fit.
        var chamber = Orientation(0,
            (0, 0), (10, 0), (10, 4), (8, 4), (8, 2), (2, 2),
            (2, 8), (8, 8), (8, 6), (10, 6), (10, 10), (0, 10));
        var square = Orientation(1, (0, 0), (2.5, 0), (2.5, 2.5), (0, 2.5));
        var cache = new NoFitCache(0.25);

        var nfp = cache.Get(chamber, square);

        Assert.Contains(nfp.Region, path => !Clipper.IsPositive(path));
        Assert.False(Forbidden(nfp, new PointD(4, 4)));
        Assert.True(Forbidden(nfp, new PointD(1, 4)));
        Assert.False(Forbidden(nfp, new PointD(11, 4)));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(100000000.0)]
    public void RemovesOnlyRingsWhollyInsideOneForbiddenCell(double scale)
    {
        var a = new PathD { new(0, 0), new(10 * scale, 0), new(0, 10 * scale) };
        var b = new PathD { new(0, 0), new(scale, 0), new(0, scale) };
        var outer = new PathD { new(-scale, -scale), new(12 * scale, -scale), new(12 * scale, 12 * scale), new(-scale, 12 * scale) };
        var covered = new PathD { new(scale, scale), new(scale, 2 * scale), new(2 * scale, scale) };
        // One vertex and the centroid are forbidden, but this ring extends beyond A+B.
        var partlyCovered = new PathD { new(2 * scale, 2 * scale), new(2 * scale, 8 * scale), new(12 * scale, 2 * scale) };
        var region = new PathsD { outer, covered, partlyCovered };

        NfpHoleFilter.RemoveCoveredHoles(region, a, b);

        Assert.Equal(2, region.Count);
        Assert.Same(outer, region[0]);
        Assert.Same(partlyCovered, region[1]);
    }

    [Fact]
    public void SeparateForbiddenCellsDoNotCertifyTheSpaceBetweenThem()
    {
        var a = new PathD { new(0, 0), new(10, 0), new(10, 2), new(2, 2), new(2, 10), new(0, 10) };
        var b = new PathD { new(0, 0), new(0.1, 0), new(0, 0.1) };
        // Both ends overlap an arm, but the middle crosses usable space in the notch.
        var ring = new PathD { new(1, 8), new(8, 1), new(7.9, 1) };
        if (Clipper.IsPositive(ring)) ring.Reverse();
        var region = new PathsD { ring };

        NfpHoleFilter.RemoveCoveredHoles(region, a, b);

        Assert.Same(ring, Assert.Single(region));
    }

    [Fact]
    public void RoundedEarDecisionCannotCertifySpaceOutsideTheSource()
    {
        // ABP is a positive turn on the 1e-4 lattice, but its double cross product
        // is negative. Floating-point ear clipping can emit C,A,B across the notch.
        var a = new PathD
        {
            new(-269144028945 / 10000.0, 180906060632 / 10000.0),
            new(463355031050 / 10000.0, 832229610235 / 10000.0),
            new(-702076855 / 10000.0, 419599288578 / 10000.0),
            new(-269144028945 / 10000.0, 1483553159838 / 10000.0),
        };
        var b = new PathD { new(0, 0), new(0.0001, 0), new(0, 0.0001) };
        var x = (a[1].x + a[2].x + a[3].x) / 3;
        var y = (a[1].y + a[2].y + a[3].y) / 3;
        var ring = new PathD { new(x - 10, y - 10), new(x - 10, y + 10), new(x + 10, y - 10) };
        var region = new PathsD { ring };
        Assert.Equal(PointInPolygonResult.IsOutside,
            Clipper.PointInPolygon(new PointD(x, y), a, NoFitCache.Precision));

        NfpHoleFilter.RemoveCoveredHoles(region, a, b);

        Assert.Same(ring, Assert.Single(region));
    }

    private static bool Forbidden(Nfp nfp, PointD point)
    {
        var winding = 0;
        foreach (var path in nfp.Region)
            if (Clipper.PointInPolygon(point, path, NoFitCache.Precision) == PointInPolygonResult.IsInside)
                winding += Clipper.IsPositive(path) ? 1 : -1;
        return winding != 0;
    }

    private static Orientation Orientation(int type, params (double X, double Y)[] points)
    {
        var outline = new PathD(points.Select(p => new PointD(p.X, p.Y)));
        var bounds = Clipper.GetBounds(outline);
        return new Orientation
        {
            TypeIndex = type,
            Index = 0,
            Rotation = 0,
            Outline = outline,
            Tolerance = 0.002,
            MinX = bounds.left - 0.002,
            MinY = bounds.top - 0.002,
            MaxX = bounds.right + 0.002,
            MaxY = bounds.bottom + 0.002,
        };
    }
}
