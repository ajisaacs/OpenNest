using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
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
}
