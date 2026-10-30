using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using Xunit;
using Curve = OpenNest.Diagnostics.PostVerificationGeometry.Curve;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// Captured material outlines are only nominally closed: capture admits closure slack up to
/// NominalContourClosure.Tolerance, and PEP-converted or rotated geometry carries sub-micro-inch
/// vertex jogs where two segments meet. The point-in-material ray test must stay consistent across
/// such a jog. Without that, a jog lying exactly on the test ray's Y reports far-away exterior
/// points as interior, so LeadPathValidator refuses leads that cannot touch anything: plans get
/// needlessly expensive routes or give up inside the search budget.
/// </summary>
public class MaterialContainmentTests
{
    [Fact]
    public void SubMicroinchJogAtRayHeight_KeepsDistantExteriorPointExterior()
    {
        var rings = Ring(
            Line((21.2, 0.6), (31.2, 0.6)),
            Line((31.2, 0.6000001), (41.2, 0.6000001)),   // admitted seam jog on the ray line
            Line((41.2, 0.6000001), (41.2, 46.16)),
            Line((41.2, 46.16), (21.2, 46.16)),
            Line((21.2, 46.16), (21.2, 0.6)));

        Assert.False(LeadMaterialSnapshot.Inside(new Vector(20.75, 0.6), rings[0], CancellationToken.None));
        Assert.False(LeadMaterialSnapshot.Inside(new Vector(20.75, 0.6000001), rings[0], CancellationToken.None));
        Assert.False(LeadMaterialSnapshot.Inside(new Vector(20.75, 0.60000005), rings[0], CancellationToken.None));
        Assert.True(LeadMaterialSnapshot.Inside(new Vector(30, 20), rings[0], CancellationToken.None));
    }

    [Fact]
    public void ClosedWithoutJog_ReportsTheSameAnswersAsTheJoggedCopy()
    {
        var clean = Ring(
            Line((21.2, 0.6), (41.2, 0.6)),
            Line((41.2, 0.6), (41.2, 46.16)),
            Line((41.2, 46.16), (21.2, 46.16)),
            Line((21.2, 46.16), (21.2, 0.6)));
        var jogged = Ring(
            Line((21.2, 0.6), (31.2, 0.6)),
            Line((31.2, 0.6000001), (41.2, 0.6000001)),
            Line((41.2, 0.6000001), (41.2, 46.16)),
            Line((41.2, 46.16), (21.2, 46.16)),
            Line((21.2, 46.16), (21.2, 0.6)));

        foreach (var y in new[] { 0.6, 0.60000005, 0.6000001 })
        {
            var expected = LeadMaterialSnapshot.Inside(new Vector(20.75, y), clean[0], CancellationToken.None);
            Assert.Equal(expected, LeadMaterialSnapshot.Inside(new Vector(20.75, y), jogged[0], CancellationToken.None));
        }
    }

    [Fact]
    public void CircleTestedExactlyAtAnExtremum_KeepsExteriorExteriorAndInteriorInterior()
    {
        // Full circle r=5 about (10,10) as four quarter arcs. Testing the ray exactly along both
        // vertical extrema is where the half-open rule and arc splitting have to agree.
        var center = new Vector(10, 10);
        var rings = Ring(
            Curve.Create(new Vector(15, 10), new Vector(10, 15), center, false),
            Curve.Create(new Vector(10, 15), new Vector(5, 10), center, false),
            Curve.Create(new Vector(5, 10), new Vector(10, 5), center, false),
            Curve.Create(new Vector(10, 5), new Vector(15, 10), center, false));

        Assert.False(LeadMaterialSnapshot.Inside(new Vector(-4, 10), rings[0], CancellationToken.None));
        Assert.False(LeadMaterialSnapshot.Inside(new Vector(-4, 5), rings[0], CancellationToken.None));
        Assert.False(LeadMaterialSnapshot.Inside(new Vector(-4, 15), rings[0], CancellationToken.None));
        Assert.True(LeadMaterialSnapshot.Inside(new Vector(10, 10), rings[0], CancellationToken.None));
        Assert.True(LeadMaterialSnapshot.Inside(new Vector(7.5, 10), rings[0], CancellationToken.None));
    }

    [Fact]
    public void SeamJogBetweenLineAndArc_KeepsDistantExteriorPointExterior()
    {
        // Rounded-left-edge material whose semicircular edge meets the bottom line 1e-7 apart:
        // an admitted closure seam sitting exactly on a datum Y shared with neighbors.
        var center = new Vector(1, 23.38000005);
        var rings = Ring(
            Line((1, 0.6), (41.2, 0.6)),
            Line((41.2, 0.6), (41.2, 46.16)),
            Line((41.2, 46.16), (1, 46.16)),
            Curve.Create(new Vector(1, 46.16), new Vector(1, 0.6000001), center, false));

        Assert.False(LeadMaterialSnapshot.Inside(new Vector(0.5, 0.6), rings[0], CancellationToken.None));
        Assert.False(LeadMaterialSnapshot.Inside(new Vector(-4, 0.6), rings[0], CancellationToken.None));
        Assert.False(LeadMaterialSnapshot.Inside(new Vector(-4, 0.6000001), rings[0], CancellationToken.None));
        Assert.True(LeadMaterialSnapshot.Inside(new Vector(30, 20), rings[0], CancellationToken.None));
    }

    [Fact]
    public void RealConcavityIsNotErasedByTheRayTolerance()
    {
        // A 0.01 in deep notch is far above any admitted seam tolerance: it must still count.
        var rings = Ring(
            Line((0, 0), (1, 0)),
            Line((1, 0), (1, 1)),
            Line((1, 1), (0.5, 1)),
            Line((0.5, 1), (0.5, 0.99)),      // notch floor
            Line((0.5, 0.99), (0, 0.99)),
            Line((0, 0.99), (0, 0)));

        // The 0.01 in notch sits above the left part's top edge; the right tower rises through it.
        Assert.False(LeadMaterialSnapshot.Inside(new Vector(0.25, 0.995), rings[0], CancellationToken.None));
        Assert.True(LeadMaterialSnapshot.Inside(new Vector(0.75, 0.995), rings[0], CancellationToken.None));
        Assert.True(LeadMaterialSnapshot.Inside(new Vector(0.25, 0.5), rings[0], CancellationToken.None));
        Assert.False(LeadMaterialSnapshot.Inside(new Vector(-1, 0.5), rings[0], CancellationToken.None));
    }

    private static IReadOnlyList<IReadOnlyList<Curve>> Ring(params Curve[] curves) =>
        new List<IReadOnlyList<Curve>> { new List<Curve>(curves) };

    private static Curve Line((double X, double Y) start, (double X, double Y) end) =>
        Curve.Create(new Vector(start.X, start.Y), new Vector(end.X, end.Y), null, false);
}
