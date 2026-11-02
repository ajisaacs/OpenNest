using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.Diagnostics;

/// <summary>
/// Contacts between two curves that meet end to end. The native intersection of their supports
/// is ill-conditioned at such a joint (two circles meeting at a shallow angle, or a line meeting
/// its fillet), so the checks derive the pair's other intersection from the shared vertex instead.
/// </summary>
public class AdjacentCurveContactTests
{
    private static PostVerificationGeometry.Curve Arc(Vector start, Vector end, Vector center, bool clockwise = false) =>
        PostVerificationGeometry.Curve.Create(start, end, center, clockwise);

    private static PostVerificationGeometry.Curve Line(Vector start, Vector end) =>
        PostVerificationGeometry.Curve.Create(start, end, null, false);

    // A half circle of radius 5 whose center is off by delta, as a DXF arc rounded apart from
    // its neighbors is, followed by a quarter circle of radius 3 that continues it tangentially.
    // Both ends still meet within the closure epsilon; the circles then cross again beside the
    // joint, where the native query puts a contact a few 1e-5 off the vertex.
    [Theory]
    [InlineData(0)]
    [InlineData(-1e-11)]
    [InlineData(-2e-11)]
    [InlineData(-3e-11)]
    [InlineData(4e-9)]
    [InlineData(-4e-9)]
    public void TangentArcsMeetingEndToEnd_TouchOnlyAtTheirVertex(double delta)
    {
        var vertex = new Vector(10, 10);
        var first = Arc(new Vector(10, 0), vertex, new Vector(10, 5 + delta));
        var second = Arc(vertex, new Vector(7, 7), new Vector(10, 7));

        var contacts = first.Contacts(second, out var overlap);
        var reversed = second.Contacts(first, out var reversedOverlap);

        Assert.False(overlap);
        Assert.False(reversedOverlap);
        Assert.Equal(vertex, Assert.Single(contacts));
        Assert.Equal(vertex, Assert.Single(reversed));
    }

    // A fillet whose circle is tangent inside the arc it continues, as a keyhole's slot ends
    // are: the supporting circles' intersection has no real root (their distance is the
    // radii's difference up to rounding), which the native query reports as NaN.
    [Theory]
    [InlineData(1.125, 0.0625, 1.3761, 2.12)]
    [InlineData(1.1, 0.0625, 0, 0)]
    [InlineData(0.9375, 0.03125, 10.5, -3.25)]
    [InlineData(2.3, 0.1, 100.001, 7)]
    public void FilletTangentInsideTheArcItContinues_TouchesOnlyAtTheirVertex(double radius, double fillet, double x, double y)
    {
        var center = new Vector(x, y);
        var vertex = center + new Vector(radius, 0);
        var filletCenter = center + new Vector(radius - fillet, 0);
        var small = Arc(filletCenter + new Vector(0, fillet), vertex, filletCenter, clockwise: true);
        var large = Arc(vertex, center + new Vector(0, radius), center);

        Assert.Equal(vertex, Assert.Single(small.Contacts(large, out var overlap)));
        Assert.Equal(vertex, Assert.Single(large.Contacts(small, out _)));
        Assert.False(overlap);
    }

    [Fact]
    public void LineMeetingItsFilletEndToEnd_TouchesOnlyAtTheirVertex()
    {
        var vertex = new Vector(10, 0);
        var edge = Line(new Vector(0, 0), vertex);
        var fillet = Arc(vertex, new Vector(15, 5), new Vector(10, 5));

        Assert.Equal(vertex, Assert.Single(edge.Contacts(fillet, out var overlap)));
        Assert.Equal(vertex, Assert.Single(fillet.Contacts(edge, out _)));
        Assert.False(overlap);
    }

    [Fact]
    public void ArcsMeetingEndToEnd_StillReportWhereTheyCrossAgain()
    {
        // The unit circle's upper half and, from the same vertex, a circle through (0, 1).
        var vertex = new Vector(1, 0);
        var upper = Arc(vertex, new Vector(-1, 0), new Vector(0, 0));
        var other = Arc(vertex, new Vector(0, 1), new Vector(1, 1));

        var contacts = upper.Contacts(other, out var overlap);

        Assert.False(overlap);
        Assert.Equal(2, contacts.Count);
        Assert.Contains(contacts, p => p.DistanceTo(vertex) <= 1e-9);
        Assert.Contains(contacts, p => p.DistanceTo(new Vector(0, 1)) <= 1e-9);
    }

    [Fact]
    public void LineMeetingAnArcEndToEnd_StillReportsTheChordsFarEnd()
    {
        // A chord of the unit circle from (1, 0) to (0, 1), then the circle's upper three quarters
        // from (1, 0) through (0, 1): they share (1, 0) and cross at (0, 1).
        var vertex = new Vector(1, 0);
        var chord = Line(new Vector(0, 1), vertex);
        var arc = Arc(vertex, new Vector(0, -1), new Vector(0, 0));

        var contacts = chord.Contacts(arc, out var overlap);

        Assert.False(overlap);
        Assert.Equal(2, contacts.Count);
        Assert.Contains(contacts, p => p.DistanceTo(vertex) <= 1e-9);
        Assert.Contains(contacts, p => p.DistanceTo(new Vector(0, 1)) <= 1e-9);
    }

    [Fact]
    public void ArcsMeetingEndToEnd_IgnoreASecondCrossingOffEitherSpan()
    {
        // The unit circle's right quarter and, from (0, 1), a circle through (-1, 0) that the
        // first arc never reaches.
        var vertex = new Vector(0, 1);
        var quarter = Arc(new Vector(1, 0), vertex, new Vector(0, 0));
        var other = Arc(vertex, new Vector(-2, 1), new Vector(-1, 1));

        Assert.Equal(vertex, Assert.Single(quarter.Contacts(other, out var overlap)));
        Assert.False(overlap);
    }
}
