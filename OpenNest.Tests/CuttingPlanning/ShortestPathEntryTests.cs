using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;
using OpenNest.Support;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// A holed part's outside entry lies where the straight line from its cutout to the next
/// part's cutout leaves it, and a support tab does not pull the
/// entry elsewhere.
/// </summary>
public class ShortestPathEntryTests
{
    private const double Length = 15.8125;
    private const double HoleY = 15.25;
    private static readonly Vector Start = new(-2, 0);

    // A long strip with one round hole near its top end.
    private static Program Strip()
    {
        var program = LeadPathValidationTests.Rectangle(0, 0, 1, Length);
        program.MoveTo(0.6875, HoleY);
        program.ArcTo(0.6875, HoleY, 0.5, HoleY, RotationType.CCW);
        return program;
    }

    private static CuttingParameters Parameters()
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 60 };
        parameters.ExternalLeadOut = new LineLeadOut { Length = 0.1, ApproachAngle = 60 };
        parameters.InternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 60 };
        parameters.InternalLeadOut = new NoLeadOut();
        parameters.PierceClearance = 0.0625;
        parameters.TabConfig = new NormalTab { Size = 0.03 };
        return parameters;
    }

    private static List<Part> Strips(int count)
    {
        var drawing = new Drawing("strip", Strip());
        return Enumerable.Range(0, count).Select(i => new Part(drawing, new Vector(i * 1.25, 0))).ToList();
    }

    private static CuttingPlanResult Plan(List<Part> parts, CuttingParameters parameters, SupportBed bed = null)
    {
        var result = CuttingPlanService.Plan(CuttingPlanService.Capture(
            new CuttingPlanRequest(parts, Start, confirmedParameters: parameters, preservePartOrder: true)
            { SupportBed = bed }));
        Assert.True(result.Status == CuttingPlanStatus.Ready,
            string.Join(Environment.NewLine, result.Findings.Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        return result;
    }

    private static void AssertEntersBesideTheHole(CuttingPlanResult result, int parts)
    {
        foreach (var part in result.ProposedOrder.Take(parts))
        {
            var outside = part.ContourChoices[^1].Point;
            Assert.True(outside.DistanceTo(new Vector(1, HoleY)) < 1e-6,
                $"Part {part.SourceOrdinal + 1} should enter on its right edge level with its hole, not at {outside}.");
        }
    }

    [Fact]
    public void HoledStripsEnterBesideTheirHoleTowardTheNextStrip()
    {
        var parts = Strips(3);
        var result = Plan(parts, Parameters());

        // Every strip but the last has a next strip to its right, level with its hole.
        AssertEntersBesideTheHole(result, 2);
    }

    [Theory]
    // Not enough: no single tab can hold the strip level.
    [InlineData(0.25, false)]
    // Enough: a tab on the left edge would hold it level, yet the route still decides.
    [InlineData(0.1, true)]
    public void ASupportTabDoesNotPullTheStripsEntryAway(double stabilityMargin, bool tabCanHold)
    {
        // Grid columns 2 apart, offset so the first strip rests on one line of contacts just right
        // of its center of gravity: only a tab on its left edge would steady it.
        var bed = new SupportGrid(2, 2, offsetX: 0.7, stabilityMargin: stabilityMargin);
        var parameters = Parameters();
        parameters.SupportTabs = true;
        parameters.TabHeldMaxLength = 0;
        var parts = Strips(3);
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts, Start, confirmedParameters: parameters,
            preservePartOrder: true)
        { SupportBed = bed });
        Assert.True(snapshot.Placements[0].Prepared.HasSupportTab);
        var support = SupportAnalyzer.Analyze(bed, 1, parts[0]);
        Assert.Equal(tabCanHold, support.TabCanSupport);
        Assert.False(support.HoldsLevelWithTabAt(new Vector(1, HoleY)));

        var result = Plan(parts, parameters, bed);

        AssertEntersBesideTheHole(result, 2);
    }

    [Fact]
    public void AnInLineCornerLeadInKeepsItsTabGapRetained()
    {
        // The corner lead-in runs back along the first edge's line, off the part; the tab gap lies
        // along the other edge. The open-cut proof must not mistake the lead-in for one in the gap.
        var parameters = Parameters();
        parameters.TabsEnabled = true;
        var part = new Part(new Drawing("plain strip", LeadPathValidationTests.Rectangle(0, 0, 1, Length)));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], Start, confirmedParameters: parameters,
            preservePartOrder: true));
        var prepared = snapshot.Placements[0].Prepared;
        var corner = prepared.AutomaticEntryCandidatesWithFallbacks(prepared.PerimeterOrdinal)
            .Single(c => c.Kind == AutomaticEntryKind.ConvexCorner && c.Choice.Point.DistanceTo(new Vector(1, Length)) < 1e-9).Choice;
        var execution = ExecutionMotionReader.Read(prepared.EmitCandidateForValidation(corner), Vector.Zero, null, default);

        Assert.Contains(execution.Motions, m => m.Layer == LayerType.Leadout);
        Assert.DoesNotContain(new ReleasedContourState().Check(execution, Start, 1),
            f => f.Message == ReleasedContourState.LeadOutRetentionMessage);
    }
}
