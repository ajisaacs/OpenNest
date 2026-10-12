using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class SeamOvercutTests
{
    [Theory]
    [InlineData(Mode.Absolute)]
    [InlineData(Mode.Incremental)]
    public void LineOvercut_IsTrimmedBackToTheSeam(Mode mode)
    {
        var program = LineOvercut();
        program.Mode = mode;
        Assert.False(LeadMaterialSnapshot.Capture(program, Vector.Zero).IsComplete);

        Assert.Equal(1, SeamOvercut.Trim(program));

        var cuts = ExecutionMotionReader.ReadSupported(program, Vector.Zero, null).Motions.Where(m => !m.Rapid).ToArray();
        Assert.Equal(5, cuts.Length);
        Assert.Equal(new Vector(2, 0), cuts[^1].End);
        Assert.Equal(mode, program.Mode);
        Assert.True(LeadMaterialSnapshot.Capture(program, Vector.Zero).IsComplete);
        Assert.Equal(0, SeamOvercut.Trim(program));
    }

    [Theory]
    [InlineData(Mode.Absolute, 0)]
    [InlineData(Mode.Incremental, 0)]
    [InlineData(Mode.Incremental, 0.7)]
    public void ArcOvercut_IsTrimmedBackToTheSeam(Mode mode, double rotation)
    {
        var program = ArcOvercut();
        program.Rotate(rotation);
        program.Mode = mode;
        var location = new Vector(40, 20);
        Assert.False(LeadMaterialSnapshot.Capture(program, location).IsComplete);

        Assert.Equal(1, SeamOvercut.Trim(program));

        var cuts = ExecutionMotionReader.ReadSupported(program, location, null).Motions.Where(m => !m.Rapid).ToArray();
        Assert.Equal(2, cuts.Length);
        Assert.True(cuts[0].Start!.Value.DistanceTo(cuts[^1].End) <= 1e-9);
        var material = LeadMaterialSnapshot.Capture(program, location);
        Assert.True(material.IsComplete, material.Reason);
    }

    [Fact]
    public void Trimmed_LeavesTheSourceProgramUntouched()
    {
        var program = LineOvercut();
        var before = ExplicitContourTests.Fingerprint(program);

        var trimmed = SeamOvercut.Trimmed(program);

        Assert.NotSame(program, trimmed);
        Assert.Equal(before, ExplicitContourTests.Fingerprint(program));
        Assert.Equal(program.Codes.Count - 1, trimmed.Codes.Count);
        var clean = LeadPathValidationTests.Rectangle(0, 0, 10, 10);
        Assert.Same(clean, SeamOvercut.Trimmed(clean));
    }

    [Fact]
    public void OtherSeamContacts_AreNotTrimmed()
    {
        // The jog returns from beside the first cut, not along it: a real sliver, not an overcut.
        var spike = new Program();
        spike.MoveTo(2, 0);
        spike.LineTo(10, 0); spike.LineTo(10, 10); spike.LineTo(0, 10); spike.LineTo(0, 0);
        spike.LineTo(2.03, 0.01);
        spike.LineTo(2, 0);
        // The last cut stops short of the seam: an open contour bridged by the jog.
        var tab = new Program();
        tab.MoveTo(2, 0);
        tab.LineTo(10, 0); tab.LineTo(10, 10); tab.LineTo(0, 10); tab.LineTo(0, 0);
        tab.LineTo(1.97, 0);
        tab.LineTo(2, 0);
        // The overcut is a lead-out, which is not part of the clean contour.
        var leadOut = LineOvercut();
        ((LinearMove)leadOut.Codes[^1]).Layer = LayerType.Leadout;

        Assert.Equal(0, SeamOvercut.Trim(spike));
        Assert.Equal(0, SeamOvercut.Trim(tab));
        Assert.Equal(0, SeamOvercut.Trim(leadOut));
    }

    [Fact]
    public void OvercutDrawing_PlansAndPassesTheOverlapCheck()
    {
        var parameters = ExplicitContourTests.Parameters();
        var drawing = new Drawing("overcut", LineOvercut());
        var first = new Part(drawing, new Vector(0, 0)) { CuttingParameters = parameters };
        var second = new Part(drawing, new Vector(12, 0)) { CuttingParameters = parameters };
        var source = ExplicitContourTests.Fingerprint(drawing.Program);

        var result = CuttingPlanService.Plan(new CuttingPlanRequest([first, second], confirmedParameters: parameters));
        var overlap = PlateOverlapAnalyzer.Analyze([first, second]);

        Assert.Equal(CuttingPlanStatus.Ready, result.Status);
        Assert.True(result.IndependentlyReplayed);
        Assert.Empty(result.Findings);
        foreach (var proposal in result.ProposedOrder)
        {
            var cuts = ExecutionMotionReader.ReadSupported(proposal.CopyProgram(), proposal.Location, null).Motions
                .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display).ToArray();
            Assert.Equal(40, cuts.Sum(m => m.Length), 9);
        }
        Assert.True(overlap.IsComplete, string.Join("; ", overlap.Issues.Select(i => i.Message)));
        Assert.Empty(overlap.Pairs);
        Assert.Equal(source, ExplicitContourTests.Fingerprint(drawing.Program));
    }

    [Fact]
    public void ArcsOfOneCircle_WithSeparatelyDerivedCenters_OverlapOnlyWhereTheirSpansDo()
    {
        // Same circle to within the support tolerance, but not bit for bit.
        var center = new Vector(3, 4);
        var other = new Vector(3 + 4e-9, 4 - 3e-9);
        var first = Arc(center, 0, 80);
        var apart = Arc(other, 100, 170);
        var joined = Arc(other, 80, 170);
        var shared = Arc(other, 40, 170);

        Assert.Empty(first.Contacts(apart, out var apartOverlap));
        Assert.False(apartOverlap);
        first.Contacts(joined, out var joinedOverlap);
        Assert.False(joinedOverlap);
        first.Contacts(shared, out var sharedOverlap);
        Assert.True(sharedOverlap);
        shared.Contacts(first, out sharedOverlap);
        Assert.True(sharedOverlap);

        static PostVerificationGeometry.Curve Arc(Vector center, double startDegrees, double endDegrees) =>
            PostVerificationGeometry.Curve.Create(Point(center, startDegrees), Point(center, endDegrees), center, false);

        static Vector Point(Vector center, double degrees) => center + new Vector(
            System.Math.Cos(degrees * System.Math.PI / 180), System.Math.Sin(degrees * System.Math.PI / 180)) * 5;
    }

    // A 10x10 square entered at (2,0) whose last cut runs 0.03 past the seam, then jogs back.
    private static Program LineOvercut()
    {
        var program = new Program();
        program.MoveTo(2, 0);
        program.LineTo(10, 0); program.LineTo(10, 10); program.LineTo(0, 10); program.LineTo(0, 0);
        program.LineTo(2.03, 0);
        program.LineTo(2, 0);
        return program;
    }

    // A radius-5 disc as two clockwise arcs; the second runs past the seam, then a chord jogs back.
    private static Program ArcOvercut()
    {
        var past = new Vector(-5 * System.Math.Sin(0.006), -5 * System.Math.Cos(0.006));
        var program = new Program();
        program.MoveTo(0, -5);
        program.ArcTo(0, 5, 0, 0, RotationType.CW);
        program.ArcTo(past, new Vector(0, 0), RotationType.CW);
        program.LineTo(0, -5);
        return program;
    }
}
