using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class NativeContactRegressionTests
{
    [Fact]
    public void TangentArcLead_RefusesFilteredSupportingCircleNaNs()
    {
        var target = LeadMaterialSnapshot.Capture(LeadPathValidationTests.Rectangle(0, -1, 1, 1), Vector.Zero);
        var obstacle = new Program();
        obstacle.MoveTo(-0.22, 0.26);
        obstacle.ArcTo(-0.22, 0.26, -0.22, 0.16, RotationType.CCW);
        var material = LeadMaterialSnapshot.Capture(obstacle, Vector.Zero);
        Assert.True(target.IsComplete, target.Reason);
        Assert.True(material.IsComplete, material.Reason);
        var lead = new Program();
        lead.MoveTo(-0.2, 0);
        lead.Codes.Add(new ArcMove(Vector.Zero, new Vector(-0.1, 0), RotationType.CW) { Layer = LayerType.Leadin });
        lead.LineTo(0, 1);
        var execution = ExecutionMotionReader.Read(lead, Vector.Zero, null, default);
        var witness = new Vector(-0.16, 0.08);
        Assert.True(execution.Motions[1].Curve.Contains(witness));
        Assert.True(material.Rings[0][0].Contains(witness));
        var result = LeadPathValidator.Check(execution, target, [material]);
        Assert.False(result.IsComplete);
        Assert.False(result.IsClear);
    }

    [Theory]
    [InlineData(0.1, -0.22, 0.16)]
    [InlineData(0.2, -0.28, 0.24)]
    [InlineData(0.05, -0.19, 0.12)]
    public void CircularNearTangency_BothDirectionsNeverCertifyNoContact(double radius, double x, double y)
    {
        var a = PostVerificationGeometry.Curve.Create(new Vector(-0.2, 0), Vector.Zero, new Vector(-0.1, 0), true);
        var center = new Vector(x, y);
        var b = PostVerificationGeometry.Curve.Create(center + new Vector(0, radius), center + new Vector(0, -radius), center, true);
        foreach (var reverse in new[] { false, true })
        {
            try
            {
                var contacts = (reverse ? b : a).Contacts(reverse ? a : b, out var overlap);
                Assert.True(overlap || contacts.Count > 0, "Near-tangent circular query silently certified no contact.");
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // Numerical uncertainty is a refusal, never a disjoint certificate.
            }
        }
    }

    [Fact]
    public void TouchingArcSplitHoles_RefuseMaterialCertificate()
    {
        var source = LeadPathValidationTests.Rectangle(-1, -1, 1, 1);
        foreach (var center in new[] { new Vector(-0.1, 0), new Vector(-0.22, 0.16) })
        {
            source.MoveTo(center.X, center.Y + 0.1);
            source.ArcTo(center.X, center.Y - 0.1, center.X, center.Y, RotationType.CCW);
            source.ArcTo(center.X, center.Y + 0.1, center.X, center.Y, RotationType.CCW);
        }
        var snapshot = LeadMaterialSnapshot.Capture(source, Vector.Zero);
        Assert.False(snapshot.IsComplete);
        Assert.NotNull(snapshot.Reason);
    }

    // A line meeting a fillet arc tangentially at their shared vertex: rounding can drop the
    // tangent root of the native line/circle quadratic, and the exact ray from the line's far end
    // then rediscovered the already-recorded vertex as a missed contact.
    [Theory]
    [InlineData(0.125, 7, 0, 0)]
    [InlineData(1.0, 33, 55.6, 4.8)]
    [InlineData(0.5, 61, 110.3, 57.1)]
    [InlineData(0.25, 80, 0, 0)]
    public void TangentFilletRing_CertifiesAsSimpleMaterial(double radius, double degrees, double x, double y)
    {
        var snapshot = LeadMaterialSnapshot.Capture(
            RoundedRectangle(7, 3, radius, degrees * System.Math.PI / 180, new Vector(x, y)), Vector.Zero);

        Assert.True(snapshot.IsComplete, snapshot.Reason);
    }

    [Fact]
    public void RotatedFilletedPart_PlansReadyOnAPlate()
    {
        var plate = new Nest().CreatePlate();
        plate.Size = new Size(100, 100);
        plate.Parts.Add(new Part(new Drawing("filleted",
            RoundedRectangle(7, 3, 1, 33 * System.Math.PI / 180, Vector.Zero)), new Vector(20, 20)));

        var proposal = CuttingPlanBatch.Capture([plate], ExplicitContourTests.Parameters(), false).Plan();

        Assert.True(proposal.CanApply, string.Join(Environment.NewLine, proposal.Describe("in")));
    }

    // The line ends 0.0000015 above a radius-0.003 circle: inside the circle's exact contact band,
    // where the native query finds nothing. That end is not a recorded contact, so the ray that
    // reaches it from the other end must still run.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineEndingInsideACircleContactBand_StaysUncertain(bool reversed)
    {
        const double radius = 0.003;
        var near = new Vector(0, radius + 0.0000015);
        var far = new Vector(-0.01, near.Y);
        var line = reversed
            ? PostVerificationGeometry.Curve.Create(near, far, null, false)
            : PostVerificationGeometry.Curve.Create(far, near, null, false);
        var circle = PostVerificationGeometry.Curve.Create(new Vector(radius, 0), new Vector(radius, 0), Vector.Zero, false);
        Assert.False(circle.Contains(near));

        Assert.Throws<NotSupportedException>(() => line.Contacts(circle, out _));
        Assert.Throws<NotSupportedException>(() => circle.Contacts(line, out _));
    }

    private static Program RoundedRectangle(double width, double height, double radius, double angle, Vector at)
    {
        var cos = System.Math.Cos(angle);
        var sin = System.Math.Sin(angle);
        Vector Place(double x, double y) => new(at.X + x * cos - y * sin, at.Y + x * sin + y * cos);
        var program = new Program();
        var start = Place(radius, 0);
        program.MoveTo(start.X, start.Y);
        void Line(double x, double y)
        {
            var end = Place(x, y);
            program.LineTo(end.X, end.Y);
        }
        void Fillet(double x, double y, double cx, double cy)
        {
            var end = Place(x, y);
            var center = Place(cx, cy);
            program.ArcTo(end.X, end.Y, center.X, center.Y, RotationType.CCW);
        }
        Line(width - radius, 0);
        Fillet(width, radius, width - radius, radius);
        Line(width, height - radius);
        Fillet(width - radius, height, width - radius, height - radius);
        Line(radius, height);
        Fillet(0, height - radius, radius, height - radius);
        Line(0, radius);
        Fillet(radius, 0, radius, radius);
        return program;
    }
}
