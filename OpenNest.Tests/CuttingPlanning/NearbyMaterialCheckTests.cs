using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The lead and rapid checks skip material and contours whose extents are well clear of the
/// motion. These cases sit just inside that skip and must still be checked.
/// </summary>
public class NearbyMaterialCheckTests
{
    [Fact]
    public void LeadArcBulgingIntoMaterialBeyondItsEndpoints_IsRefused()
    {
        // The lead arc runs from (-2, 7) through (-1, 4.59) to the joint (0, 5); the other part's
        // material lies wholly below both endpoints but inside the arc's sweep.
        var target = LeadMaterialSnapshot.Capture(LeadPathValidationTests.Rectangle(0, 0, 10, 10), Vector.Zero);
        var lead = new Program();
        lead.MoveTo(-2, 7);
        lead.Codes.Add(new ArcMove(new Vector(0, 5), new Vector(-1, 6), RotationType.CCW) { Layer = LayerType.Leadin });
        lead.LineTo(0, 10);
        var execution = ExecutionMotionReader.Read(lead, Vector.Zero, null, default);
        Assert.True(LeadPathValidator.Check(execution, target, [target]).IsClear);
        var below = LeadMaterialSnapshot.Capture(LeadPathValidationTests.Rectangle(-1.2, 4.3, -0.8, 4.8), Vector.Zero);
        Assert.True(below.IsComplete, below.Reason);

        var result = LeadPathValidator.Check(execution, target, [target, below]);

        Assert.True(result.IsComplete, result.Reason);
        Assert.False(result.IsClear);
    }

    [Fact]
    public void LeadEndingOnAnotherPartsEdge_IsRefused()
    {
        // The other part's extent only touches the lead's: separation zero, never skipped.
        var target = LeadMaterialSnapshot.Capture(LeadPathValidationTests.Rectangle(0, 0, 10, 10), Vector.Zero);
        var lead = new Program();
        lead.MoveTo(-3, 5);
        lead.Codes.Add(new LinearMove(0, 5) { Layer = LayerType.Leadin });
        lead.LineTo(0, 10);
        var execution = ExecutionMotionReader.Read(lead, Vector.Zero, null, default);
        var touching = LeadMaterialSnapshot.Capture(LeadPathValidationTests.Rectangle(-4, 5, -3, 6), Vector.Zero);

        var result = LeadPathValidator.Check(execution, target, [target, touching]);

        Assert.True(result.IsComplete, result.Reason);
        Assert.False(result.IsClear);
    }

    [Fact]
    public void RapidAcrossOnlyTheBulgeOfACompletedArc_IsACrossing()
    {
        // A "D": straight side x = 0 from (0, 0) to (0, 4), arc bulging right to x = 2. Every
        // endpoint has x = 0; the rapid at x = 1 crosses only the bulge.
        var state = new ReleasedContourState();
        var d = new Program();
        d.MoveTo(-0.5, 0);
        d.Codes.Add(new LinearMove(0, 0) { Layer = LayerType.Leadin });
        d.LineTo(0, 4);
        d.Codes.Add(new ArcMove(new Vector(0, 0), new Vector(0, 2), RotationType.CW));
        Assert.Empty(state.Check(Read(d), Vector.Zero, 1).Where(f => f.Kind == PostVerificationKind.RapidCrossing));

        var findings = state.Check(Read(Square(1, 5)), new Vector(1, -1), 2);

        Assert.Contains(findings, f => f.Kind == PostVerificationKind.RapidCrossing && f.OtherPartNumber == 1);
    }

    [Fact]
    public void RapidEndingOnACompletedCorner_IsACrossing()
    {
        // The extents meet only at the corner (2, 2).
        var state = new ReleasedContourState();
        state.Check(Read(Square(0, 0)), Vector.Zero, 1);

        var findings = state.Check(Read(Square(3, 3, new Vector(2, 2))), new Vector(5, -1), 2);

        Assert.Contains(findings, f => f.Kind == PostVerificationKind.RapidCrossing && f.OtherPartNumber == 1);
    }

    [Fact]
    public void RapidJustOutsideASmallCompletedCircle_IsStillACrossing()
    {
        // A circle of radius 0.001 counts as touched up to sqrt(r^2 + 1e-8) from its centre, beyond
        // its radius; the rapid passes 0.000002 outside the radius, inside that band.
        var state = new ReleasedContourState();
        Assert.Empty(state.Check(Read(Circle(0.001)), Vector.Zero, 1)
            .Where(f => f.Kind == PostVerificationKind.RapidCrossing));
        var next = new Program();
        next.MoveTo(0.001002, 0.002);

        var findings = state.Check(Read(next), new Vector(0.001002, -0.002), 2);

        Assert.Contains(findings, f => f.Kind == PostVerificationKind.RapidCrossing && f.OtherPartNumber == 1);
    }

    [Fact]
    public void PrePostReview_RapidJustOutsideASmallCircle_IsStillReported()
    {
        const double radius = 0.003;
        const double y = radius + 0.0000015;
        var first = new Part(new Drawing("circle", CleanCircle(radius)));
        var circle = Circle(radius);
        circle.Codes.Add(new LinearMove(-0.01, y) { Layer = LayerType.Scribe }); // Ends left of the circle.
        Assert.True(first.RestoreLeadInProgram(circle, true));
        var second = new Part(new Drawing("square",
            LeadPathValidationTests.Rectangle(0.01, y + 0.0002, 0.02, y + 0.0102)));
        var square = new Program();
        square.MoveTo(0.01, y);
        square.Codes.Add(new LinearMove(0.01, y + 0.0002) { Layer = LayerType.Leadin });
        square.LineTo(0.01, y + 0.0102); square.LineTo(0.02, y + 0.0102);
        square.LineTo(0.02, y + 0.0002); square.LineTo(0.01, y + 0.0002);
        Assert.True(second.RestoreLeadInProgram(square, true));
        var nest = new Nest();
        var plate = nest.CreatePlate();
        plate.Parts.Add(first);
        plate.Parts.Add(second);

        var report = PostVerificationAnalyzer.Analyze(nest);

        Assert.Contains(report.Findings, f => f.Kind == PostVerificationKind.RapidCrossing
            && f.PartNumber == 2 && f.OtherPartNumber == 1);
    }

    [Fact]
    public void LeadGrazingASmallCircle_StaysAnUncertainCheck()
    {
        // The lead passes 0.0000015 outside a radius-0.003 circle: inside its native contact band,
        // where the native query cannot certify contact either way.
        const double radius = 0.003;
        const double y = radius + 0.0000015;
        var target = LeadMaterialSnapshot.Capture(LeadPathValidationTests.Rectangle(0.01, y, 0.02, y + 0.01), Vector.Zero);
        var other = LeadMaterialSnapshot.Capture(CleanCircle(radius), Vector.Zero);
        Assert.True(other.IsComplete, other.Reason);
        var lead = new Program();
        lead.MoveTo(-0.01, y);
        lead.Codes.Add(new LinearMove(0.01, y) { Layer = LayerType.Leadin });
        lead.LineTo(0.01, y + 0.01);

        var result = LeadPathValidator.Check(Read(lead), target, [target, other]);

        Assert.False(result.IsComplete);
        Assert.Contains("uncertain", result.Reason);
    }

    [Fact]
    public void ExtentWithANaNBound_IsNeverSeparated()
    {
        var partial = new PostVerificationGeometry.Extent(double.NaN, 0, double.NaN, 1);
        var other = new PostVerificationGeometry.Extent(0, 4, 1, 5);

        Assert.False(partial.IsSeparatedFrom(other, 1e-6));
        Assert.False(other.IsSeparatedFrom(partial, 1e-6));
    }

    private static Program CleanCircle(double radius)
    {
        var p = new Program();
        p.MoveTo(radius, 0);
        p.Codes.Add(new ArcMove(new Vector(radius, 0), Vector.Zero, RotationType.CCW));
        return p;
    }

    // A full circle about the origin cut after a lead-in from 0.0002 outside it.
    private static Program Circle(double radius)
    {
        var p = new Program();
        p.MoveTo(radius + 0.0002, 0);
        p.Codes.Add(new LinearMove(radius, 0) { Layer = LayerType.Leadin });
        p.Codes.Add(new ArcMove(new Vector(radius, 0), Vector.Zero, RotationType.CCW));
        return p;
    }

    // A 2 x 2 square at (x, y) with a lead-in from 0.5 below its corner, optionally preceded by
    // a rapid to a given point.
    private static Program Square(double x, double y, Vector? via = null)
    {
        var p = new Program();
        if (via is { } point)
            p.MoveTo(point.X, point.Y);
        p.MoveTo(x, y - 0.5);
        p.Codes.Add(new LinearMove(x, y) { Layer = LayerType.Leadin });
        p.LineTo(x, y + 2); p.LineTo(x + 2, y + 2); p.LineTo(x + 2, y); p.LineTo(x, y);
        return p;
    }

    private static OwnedExecution Read(Program p) => ExecutionMotionReader.Read(p, Vector.Zero, null, default);
}
