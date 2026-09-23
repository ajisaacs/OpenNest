using System.Linq;
using Clipper2Lib;
using OpenNest.CNC;
using OpenNest.Engine.Jobs;
using OpenNest.Geometry;

namespace OpenNest.Engine.Opus55.Tests;

public class NoFitCacheTests
{
    [Theory]
    [InlineData(0.0, 0.0)] // B's corner at A's corner: B covers A completely.
    [InlineData(-5.0, -5.0)] // A deep inside B.
    [InlineData(2.0, 0.5)] // Partial overlap.
    public void ForbidsEveryOverlappingOffsetIncludingContainment(double dx, double dy)
    {
        var (small, big) = Orientations();
        var nfp = new NoFitCache(0.1).Get(small, big);

        Assert.True(Forbidden(nfp, new PointD(dx, dy)), $"offset ({dx}, {dy}) should be forbidden");
    }

    [Theory]
    [InlineData(4.0, 0.0)] // Beside A, clear by more than the clearance.
    [InlineData(0.0, -21.0)] // Below A.
    [InlineData(-21.0, 0.0)] // Left of A.
    public void AllowsClearOffsets(double dx, double dy)
    {
        var (small, big) = Orientations();
        var nfp = new NoFitCache(0.1).Get(small, big);

        Assert.False(Forbidden(nfp, new PointD(dx, dy)), $"offset ({dx}, {dy}) should be free");
    }

    /// <summary>A = 3x3 L (concave), B = 20x20 square; both at rotation 0 with origin at the lower-left.</summary>
    private static (Orientation Small, Orientation Big) Orientations()
    {
        var job = new NestJob(
            new[]
            {
                new NestJobPart("small", Snapshot((0, 0), (3, 0), (3, 1), (1, 1), (1, 3), (0, 3)), 1, 0, RotationPolicy.Fixed(0)),
                new NestJobPart("big", Snapshot((0, 0), (20, 0), (20, 20), (0, 20)), 1, 0, RotationPolicy.Fixed(0)),
            },
            new[] { new NestPlateStock("s", new Size(100, 100)) }
        );
        var types = PartCatalog.Build(job);
        return (types[0].Orientations.Single(), types[1].Orientations.Single());
    }

    private static bool Forbidden(Nfp nfp, PointD point)
    {
        var winding = 0;
        foreach (var path in nfp.Region)
            if (Clipper.PointInPolygon(point, path) == PointInPolygonResult.IsInside)
                winding += Clipper.IsPositive(path) ? 1 : -1;
        return winding != 0;
    }

    private static PartGeometrySnapshot Snapshot(params (double X, double Y)[] points)
    {
        var program = new Program();
        program.Codes.Add(new RapidMove(points[0].X, points[0].Y));
        foreach (var (x, y) in points.Skip(1))
            program.Codes.Add(new LinearMove(x, y));
        program.Codes.Add(new LinearMove(points[0].X, points[0].Y));
        return PartGeometrySnapshot.FromProgram(program);
    }
}
