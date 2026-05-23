using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Posts.GravographIS;

namespace OpenNest.Tests.GravographIS;

public class PolylinePrePassTests
{
    [Fact]
    public void Stitch_TwoConnectedSegments_BecomeOnePolyline()
    {
        var inputs = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(1, 0) },
            new[] { new Vector(1, 0), new Vector(1, 1) },
        };

        var stitched = PolylinePrePass.Stitch(inputs);

        Assert.Single(stitched);
        Assert.Equal(3, stitched[0].Count);
        Assert.Equal(new Vector(0, 0), stitched[0][0]);
        Assert.Equal(new Vector(1, 0), stitched[0][1]);
        Assert.Equal(new Vector(1, 1), stitched[0][2]);
    }

    [Fact]
    public void Stitch_FourSegmentsFormingClosedSquare_BecomeOnePolyline()
    {
        var inputs = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(1, 0) },
            new[] { new Vector(1, 0), new Vector(1, 1) },
            new[] { new Vector(1, 1), new Vector(0, 1) },
            new[] { new Vector(0, 1), new Vector(0, 0) },
        };

        var stitched = PolylinePrePass.Stitch(inputs);

        Assert.Single(stitched);
        // Four edges + closing return-to-start = five vertices.
        Assert.Equal(5, stitched[0].Count);
    }

    [Fact]
    public void Stitch_ReversesOneSegmentToMakeAJoin()
    {
        // Second segment is given backward; stitcher should reverse it.
        var inputs = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(1, 0) },
            new[] { new Vector(2, 0), new Vector(1, 0) },
        };

        var stitched = PolylinePrePass.Stitch(inputs);

        Assert.Single(stitched);
        Assert.Equal(3, stitched[0].Count);
        Assert.Equal(new Vector(0, 0), stitched[0][0]);
        Assert.Equal(new Vector(2, 0), stitched[0][stitched[0].Count - 1]);
    }

    [Fact]
    public void Stitch_DisjointSegments_StayDistinct()
    {
        var inputs = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(1, 0) },
            new[] { new Vector(5, 5), new Vector(6, 5) },
        };

        var stitched = PolylinePrePass.Stitch(inputs);

        Assert.Equal(2, stitched.Count);
    }

    [Fact]
    public void Stitch_DropsZeroAndSinglePointPolylines()
    {
        var inputs = new List<IReadOnlyList<Vector>>
        {
            new Vector[] { },
            new[] { new Vector(0, 0) },
            new[] { new Vector(0, 0), new Vector(1, 0) },
        };

        var stitched = PolylinePrePass.Stitch(inputs);

        Assert.Single(stitched);
        Assert.Equal(2, stitched[0].Count);
    }

    [Fact]
    public void Reorder_ReducesTotalPenUpTravelVsWorstCase()
    {
        // Three short polylines at (0,0), (10,0), (5,0). The greedy NN starting
        // from origin should pick (0,0)→(5,0)→(10,0) (travels of 4 + 4 ≈ 8) over
        // the worst-case input order (0,0)→(10,0)→(5,0) (travels 9 + 4 ≈ 13).
        var inputs = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(0, 0), new Vector(1, 0) },
            new[] { new Vector(10, 0), new Vector(11, 0) },
            new[] { new Vector(5, 0), new Vector(6, 0) },
        };

        var reordered = PolylinePrePass.Reorder(inputs);

        Assert.Equal(3, reordered.Count);
        var travelBefore = TotalPenUpTravel(inputs);
        var travelAfter = TotalPenUpTravel(reordered);
        Assert.True(travelAfter < travelBefore,
            $"Expected reorder to reduce pen-up travel; before={travelBefore}, after={travelAfter}");
    }

    [Fact]
    public void Reorder_ReversesPolylineIfTailIsCloser()
    {
        // Origin (0,0); a single polyline whose tail is much closer to origin
        // than its head. Reorder should flip it.
        var inputs = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(10, 0), new Vector(0.5, 0) },
        };

        var reordered = PolylinePrePass.Reorder(inputs, allowReverse: true);

        Assert.Single(reordered);
        Assert.Equal(new Vector(0.5, 0), reordered[0][0]);
        Assert.Equal(new Vector(10, 0), reordered[0][1]);
    }

    [Fact]
    public void Reorder_ReverseDisabled_KeepsDirection()
    {
        var inputs = new List<IReadOnlyList<Vector>>
        {
            new[] { new Vector(10, 0), new Vector(0.5, 0) },
        };

        var reordered = PolylinePrePass.Reorder(inputs, allowReverse: false);

        Assert.Single(reordered);
        Assert.Equal(new Vector(10, 0), reordered[0][0]);
        Assert.Equal(new Vector(0.5, 0), reordered[0][1]);
    }

    private static double TotalPenUpTravel(IEnumerable<IReadOnlyList<Vector>> polylines)
    {
        var total = 0.0;
        Vector? last = null;
        foreach (var p in polylines)
        {
            if (p == null || p.Count < 2) continue;
            if (last.HasValue)
            {
                var dx = p[0].X - last.Value.X;
                var dy = p[0].Y - last.Value.Y;
                total += System.Math.Sqrt(dx * dx + dy * dy);
            }
            last = p[p.Count - 1];
        }
        return total;
    }
}
