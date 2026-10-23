using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A closest-point entry can land a hair off an entity's vertex. Emission starts the contour
/// at that vertex (the split keeps the entity whole), so the lead-in must end there too, or
/// the cut closes short of where it started and replay refuses the whole plate.
/// </summary>
public class NearVertexEntryTests
{
    private static readonly Vector Center = new(5, 5);
    private const double Radius = 0.3125;

    // A hole drawn as two half arcs meeting at its top and bottom, inside a square perimeter.
    private static Program HalfArcHole()
    {
        var p = ExplicitContourTests.Square(false);
        p.MoveTo(Center.X, Center.Y + Radius);
        p.ArcTo(Center.X, Center.Y - Radius, Center.X, Center.Y, RotationType.CCW);
        p.ArcTo(Center.X, Center.Y + Radius, Center.X, Center.Y, RotationType.CCW);
        return p;
    }

    [Theory]
    [InlineData(4.1e-6)] // just before the top vertex, at the end of the second arc
    [InlineData(-4.1e-6)] // just after it, at the start of the first arc
    public void HoleCutClosesWhereItsLeadInEnds(double angleFromTop)
    {
        var prepared = PreparedContours.Capture(HalfArcHole(), ExplicitContourTests.Parameters());
        var hole = Enumerable.Range(0, prepared.Count).Single(c => c != prepared.PerimeterOrdinal);
        var approach = Center + new Vector(System.Math.Sin(angleFromTop), System.Math.Cos(angleFromTop)) * 10;
        var entry = prepared.ClosestEntry(hole, approach);
        var top = new Vector(Center.X, Center.Y + Radius);
        Assert.InRange(entry.Point.DistanceTo(top), 1e-6, 1e-5);

        var program = prepared.Emit(new[] { entry, prepared.ClosestEntry(prepared.PerimeterOrdinal, new Vector(0, 0)) });
        program.Mode = Mode.Absolute;
        var holeMotions = program.Codes.OfType<Motion>().SkipWhile(m => m is RapidMove)
            .TakeWhile(m => m is not RapidMove).ToArray();

        // Line lead-in, then the two half arcs back to the lead-in's end.
        Assert.Equal(3, holeMotions.Length);
        Assert.True(holeMotions[^1].EndPoint.DistanceTo(holeMotions[0].EndPoint) <= 1e-9,
            $"cut ends {holeMotions[^1].EndPoint.DistanceTo(holeMotions[0].EndPoint)} from its start");
        Assert.True(holeMotions[0].EndPoint.DistanceTo(top) <= 1e-9);
    }
}
