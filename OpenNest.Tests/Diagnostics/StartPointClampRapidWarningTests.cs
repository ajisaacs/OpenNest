using System;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Tests.Diagnostics;

/// <summary>
/// D10 (user decision 2026-10-13, "keep the warning"): a clamp whose physical footprint
/// covers the modeled program start (0,0) makes every direct XY rapid that leaves the start
/// point cross it. That warning stays: no exemption for the first rapid, the start point or
/// "head is already up", and no persisted bypass — every posting attempt re-reports it.
/// Fixtures build the rail with <see cref="Plate.SetClampRail"/> and predict footprints from
/// the physical rule (center +/- width/2 from the origin along the edge, depth into the
/// sheet) plus an independent segment/box oracle, never from the analyzer's own math.
/// </summary>
public class StartPointClampRapidWarningTests
{
    private const double OriginCenter = 0.25;
    private const double ClampWidth = 1.5;
    private const double ClampDepth = 0.625;

    private static int XSign(int quadrant) => quadrant is 2 or 3 ? -1 : 1;
    private static int YSign(int quadrant) => quadrant is 3 or 4 ? -1 : 1;

    private static Box PhysicalFootprint(int quadrant, double center)
    {
        var near = center - ClampWidth / 2;
        var far = center + ClampWidth / 2;
        var x = XSign(quadrant) > 0 ? near : -far;
        var y = YSign(quadrant) > 0 ? 0 : -ClampDepth;
        return new Box(x, y, ClampWidth, ClampDepth);
    }

    /// <summary>Independent oracle: does the closed segment from->to touch the closed box?</summary>
    private static bool SegmentTouchesBox(Vector from, Vector to, Box box)
    {
        var tEnter = 0.0;
        var tExit = 1.0;
        Clip(-dx(from, to), from.X - box.Left);
        Clip(dx(from, to), box.Right - from.X);
        Clip(-dy(from, to), from.Y - box.Bottom);
        Clip(dy(from, to), box.Top - from.Y);
        return tEnter <= tExit;

        void Clip(double p, double q)
        {
            if (p == 0)
            {
                if (q < 0) tEnter = 2; // provably disjoint
                return;
            }
            var ratio = q / p;
            if (p < 0) tEnter = System.Math.Max(tEnter, ratio);
            else tExit = System.Math.Min(tExit, ratio);
        }
    }

    private static double dx(Vector from, Vector to) => to.X - from.X;
    private static double dy(Vector from, Vector to) => to.Y - from.Y;

    private static Plate RailPlate(int quadrant, params double[] centers)
    {
        var plate = new Plate(81, 120) { Quadrant = quadrant };
        plate.SetClampRail(new ClampRail(ClampWidth, ClampDepth, centers));
        return plate;
    }

    private static Part ScribeSquare(Vector location)
    {
        var program = new Program();
        program.MoveTo(0, 0);
        program.LineTo(0.1, 0);
        program.LineTo(0.1, 0.1);
        program.LineTo(0, 0.1);
        program.LineTo(0, 0);
        foreach (var line in program.Codes.OfType<LinearMove>())
            line.Layer = LayerType.Scribe;
        return new Part(new Drawing("scribe", program), location);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void OriginCoveringClampWarnsOnceOnTheFirstRapid(int quadrant)
    {
        // Oracle precondition: the rail rule puts a clamp at center 0.25 over the start point.
        var footprint = PhysicalFootprint(quadrant, OriginCenter);
        Assert.True(footprint.Left <= 0 && 0 <= footprint.Right);
        Assert.True(footprint.Bottom <= 0 && 0 <= footprint.Top);

        var plate = RailPlate(quadrant, OriginCenter);
        var partLocation = new Vector(XSign(quadrant) * 40, YSign(quadrant) * 20);
        plate.Parts.Add(ScribeSquare(partLocation));
        // The oracle sees the incoming rapid (0,0) -> part start crossing the footprint.
        Assert.True(SegmentTouchesBox(Vector.Zero, partLocation, footprint));

        // Every posting attempt re-runs the analysis; nothing is remembered between attempts.
        var report = PostVerificationAnalyzer.Analyze(NestOf(plate));
        var again = PostVerificationAnalyzer.Analyze(NestOf(plate));

        var rapid = Assert.Single(report.Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
        Assert.Equal("clamp-1", rapid.ClampId);
        Assert.Equal(1, rapid.PartNumber);
        Assert.Equal(Vector.Zero, rapid.RapidStart);
        var contact = Assert.Single(rapid.ContactPoints);
        Assert.InRange(contact.X, footprint.Left, footprint.Right);
        Assert.InRange(contact.Y, footprint.Bottom, footprint.Top);
        Assert.Contains("Direct XY rapid crosses active clamp 'clamp-1'", rapid.Message);

        var repeated = Assert.Single(again.Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
        Assert.Equal(rapid.ClampId, repeated.ClampId);

        // Consent is per attempt: never postable without it, always postable with it.
        Assert.False(report.CanPost(false));
        Assert.True(report.CanPost(true));
        Assert.False(again.CanPost(false));
        Assert.True(again.CanPost(true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RailWithoutOriginClampGivesNoClampRapidFinding(int quadrant)
    {
        var plate = RailPlate(quadrant, 6);
        var footprint = PhysicalFootprint(quadrant, 6);
        Assert.False(footprint.Left <= 0 && 0 <= footprint.Right &&
            footprint.Bottom <= 0 && 0 <= footprint.Top);

        var partLocation = new Vector(XSign(quadrant) * 10, YSign(quadrant) * 20);
        plate.Parts.Add(ScribeSquare(partLocation));
        // The oracle: this first rapid passes above the clamp, and the part clears it.
        Assert.False(SegmentTouchesBox(Vector.Zero, partLocation, footprint));

        var report = PostVerificationAnalyzer.Analyze(NestOf(plate));

        Assert.DoesNotContain(report.Findings, f => f.Kind == PostVerificationKind.RapidCrossing);
        Assert.DoesNotContain(report.Findings, f => f.ClampId != null);
    }

    private static Nest NestOf(Plate plate)
    {
        var nest = new Nest();
        nest.Plates.Add(plate);
        return nest;
    }
}
