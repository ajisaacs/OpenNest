using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

public class OverlapMarkerTests
{
    // 4x4 frame with a 2x2 hole: the area centroid (2, 2) is in the hole.
    private static readonly List<Vector> Frame = Closed(new Vector(0, 0), new Vector(4, 0), new Vector(4, 4), new Vector(0, 4));
    private static readonly List<List<Vector>> FrameHole =
        new() { Closed(new Vector(1, 1), new Vector(1, 3), new Vector(3, 3), new Vector(3, 1)) };

    [Fact]
    public void PoleIsDeepInsideTheMaterial()
    {
        var marker = OverlapMarker.Find(Frame, FrameHole, 12, new Vector(2, 2));

        // The frame's bars are 1 wide: the pole sits on a bar's center line.
        Assert.True(Inside(marker), $"{marker.X:R}, {marker.Y:R}");
        var depth = new[] { marker.X, 4 - marker.X, marker.Y, 4 - marker.Y }.Min();
        Assert.InRange(depth, 0.4, 0.6);
    }

    [Fact]
    public void ExhaustedSearchFallsBackToAnInteriorScanLinePoint()
    {
        // One cell of budget: the search cannot leave the hole-centerd bounding-box cell.
        var marker = OverlapMarker.Find(Frame, FrameHole, 12, new Vector(2, 2), maxCells: 1);

        Assert.True(Inside(marker), $"{marker.X:R}, {marker.Y:R}");
    }

    [Fact]
    public void NoInteriorRunReturnsTheFallback()
    {
        var fallback = new Vector(7, 7);
        var marker = OverlapMarker.Find(Closed(new Vector(0, 0), new Vector(1, 0), new Vector(2, 0)),
            new List<List<Vector>>(), 0, fallback, maxCells: 1);

        Assert.Equal(fallback, marker);
    }

    private static bool Inside(Vector point) =>
        point.X > 0 && point.X < 4 && point.Y > 0 && point.Y < 4
        && !(point.X >= 1 && point.X <= 3 && point.Y >= 1 && point.Y <= 3);

    private static List<Vector> Closed(params Vector[] points) => points.Append(points[0]).ToList();
}
