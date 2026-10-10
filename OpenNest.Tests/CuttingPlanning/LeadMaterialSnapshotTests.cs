using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class LeadMaterialSnapshotTests
{
    [Theory]
    [InlineData(false, 0, 0, 0)]
    [InlineData(true, 0, 0, 0)]
    [InlineData(false, 0.7, 40, 20)]
    [InlineData(true, 0.7, 40, 20)]
    public void AdjacentArcs_IntersectionOnExtensionIsNotASelfIntersection(bool atSeam, double angle, double x, double y)
    {
        var program = ArcJoint(0.00005, false, atSeam);
        program.Rotate(angle);
        var location = new Vector(x, y);
        var execution = ExecutionMotionReader.ReadSupported(program, location, null);
        var arcs = execution.Motions.Where(m => m.Curve?.IsFiniteArc == true).Select(m => m.Curve!).ToArray();
        var contacts = arcs[0].Contacts(arcs[1], out var overlap);
        Assert.False(overlap);
        Assert.Contains(contacts, p => arcs.Any(a => a.IsOutsideArcSpan(p)));

        var material = LeadMaterialSnapshot.Capture(program, location);

        Assert.True(material.IsComplete, material.Reason);
    }

    [Fact]
    public void AdjacentArcs_RealSecondIntersectionRemainsRejected()
    {
        var program = ArcJoint(-0.00005, true, false);
        var arcs = ExecutionMotionReader.ReadSupported(program, Vector.Zero, null).Motions
            .Where(m => m.Curve?.IsFiniteArc == true).Select(m => m.Curve!).ToArray();
        var contacts = arcs[0].Contacts(arcs[1], out var overlap);
        Assert.False(overlap);
        Assert.Contains(contacts, p => p.DistanceTo(Vector.Zero) > 0.00001
            && !arcs[0].IsOutsideArcSpan(p) && !arcs[1].IsOutsideArcSpan(p));
        var material = LeadMaterialSnapshot.Capture(program, Vector.Zero);

        Assert.False(material.IsComplete);
        Assert.Contains("Material boundaries", material.Reason);
    }

    private static Program ArcJoint(double centerOffset, bool reverseSecond, bool atSeam)
    {
        // Near-tangent circles meet at zero and about 0.000106 away. The native
        // angular band of the radius-18 arc admits its extension beyond zero.
        var first = new Vector(-18, 18);
        var center = new Vector(centerOffset, 1);
        var end = center + new Vector(center.DistanceTo(Vector.Zero), 0);
        var program = new Program();
        program.MoveTo(atSeam ? Vector.Zero : first);
        if (!atSeam)
            program.ArcTo(0, 0, 0, 18, RotationType.CCW);
        program.ArcTo(end.X, end.Y, center.X, center.Y, reverseSecond ? RotationType.CW : RotationType.CCW);
        program.LineTo(first);
        if (atSeam)
            program.ArcTo(0, 0, 0, 18, RotationType.CCW);
        return program;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundedCornerContact_RoundingAtAdjacentEndpointIsAccepted(bool atSeam)
    {
        // Rounded imported corner: its native tangent contact falls 1.52e-8
        // from the authored arc/line joint. Exercise both consecutive and seam adjacency.
        var program = new Program();
        var start = new Vector(120.509177, 14.879559);
        program.MoveTo(start);
        program.Codes.Add(new ArcMove(new Vector(120.5, 14.9375),
            new Vector(120.6875000024, 14.9374999848), RotationType.CW));
        program.LineTo(120.5, 28.5625);
        program.Codes.Add(new ArcMove(new Vector(120.6875, 28.75),
            new Vector(120.6875, 28.5625), RotationType.CW));
        program.LineTo(127.6875, 28.75);
        program.Codes.Add(new ArcMove(new Vector(127.875, 28.5625),
            new Vector(127.6875, 28.5625), RotationType.CW));
        program.LineTo(127.875, 14.9375);
        program.Codes.Add(new ArcMove(new Vector(127.6875, 14.75),
            new Vector(127.6875, 14.9375), RotationType.CW));
        program.LineTo(120.6875, 14.75);
        program.Codes.Add(new ArcMove(start,
            new Vector(120.6874999967, 14.9375000024), RotationType.CW));
        if (atSeam)
        {
            var first = program.Codes[1];
            program.Codes.RemoveAt(1);
            program.Codes.Add(first);
            program.Codes[0] = new RapidMove(120.5, 14.9375);
        }

        var material = LeadMaterialSnapshot.Capture(program, Vector.Zero);

        Assert.True(material.IsComplete, material.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CrossingOrRetracedBoundary_RemainsRejected(bool retraced)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(8, 8);
        if (retraced)
        {
            program.LineTo(4, 4);
            program.LineTo(0, 8);
        }
        else
        {
            program.LineTo(0, 8);
            program.LineTo(8, 0);
        }
        program.LineTo(0, 0);

        var material = LeadMaterialSnapshot.Capture(program, Vector.Zero);

        Assert.False(material.IsComplete);
        Assert.Contains("Material boundaries", material.Reason);
    }

    [Fact]
    public void SeparateTouchingHoles_RemainRejected()
    {
        var part = BestEffortCuttingPlanTests.TouchingContours();

        var material = LeadMaterialSnapshot.Capture(part.BaseDrawing.Program, Vector.Zero);

        Assert.False(material.IsComplete);
        Assert.Contains("Material boundaries", material.Reason);
    }
}
