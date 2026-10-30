using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Diagnostics;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

public class HoleDepartureTests
{
    private static readonly Vector Start = new(4, -3);

    // Holes on one axis with a cutout between them whose corners are rounded by a tiny radius. A
    // corner arc's midpoint lies on both facing sides of the cutout, so it ranks as the ideal
    // corner, but the tool leaves it along its 45 degree tangent: the shallow rapid to the hole
    // just above re-enters the corner and runs back across the cutout just cut.
    private static Program RoundedCutoutBetweenHoles()
    {
        const double r = 0.015;
        var clean = LeadPathValidationTests.Rectangle(0, 0, 8, 10);
        Circle(clean, new Vector(4, 2.3), 0.156);
        clean.MoveTo(3 + r, 3);
        clean.LineTo(5 - r, 3); clean.ArcTo(5, 3 + r, 5 - r, 3 + r, RotationType.CCW);
        clean.LineTo(5, 5 - r); clean.ArcTo(5 - r, 5, 5 - r, 5 - r, RotationType.CCW);
        clean.LineTo(3 + r, 5); clean.ArcTo(3, 5 - r, 3 + r, 5 - r, RotationType.CCW);
        clean.LineTo(3, 3 + r); clean.ArcTo(3 + r, 3, 3 + r, 3 + r, RotationType.CCW);
        Circle(clean, new Vector(4, 5.36), 0.156);
        Circle(clean, new Vector(4, 8.5), 0.156);
        return clean;
    }

    private static void Circle(Program program, Vector center, double radius)
    {
        program.MoveTo(center.X + radius, center.Y);
        program.ArcTo(center.X + radius, center.Y, center.X, center.Y, RotationType.CCW);
    }

    // The saved settings this regression came from: 0.15 line lead-ins at 90 degrees, no lead-outs.
    private static CuttingParameters Parameters()
    {
        var lead = new LineLeadIn { Length = 0.15, ApproachAngle = 90 };
        return new CuttingParameters
        {
            ExternalLeadIn = lead,
            InternalLeadIn = lead,
            ExternalLeadOut = new NoLeadOut(),
            InternalLeadOut = new NoLeadOut(),
            PierceClearance = 0
        };
    }

    [Fact]
    public void PreferredHoleEntryLeavesTowardTheNextHole()
    {
        var part = new Part(new Drawing("rounded cutout", RoundedCutoutBetweenHoles()));
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest([part], Start,
            confirmedParameters: Parameters(), preservePartOrder: true));
        var source = snapshot.Placements[0];
        var prepared = source.Prepared;
        var adapter = new ContourEntryFeasibility(prepared, source.Location, source.Material, []);
        var outside = ContourEntrySelection.Select(prepared.AutomaticEntryCandidatesWithFallbacks(prepared.PerimeterOrdinal)
            .RankTowardNextCut(null, Start), c => adapter.Check(c.Choice)).Choices[0];
        var centers = prepared.HoleCenters().Select(c => c ?? Vector.Zero).ToArray();
        var route = new[] { 0, 1, 2, 3 };
        var preferred = PreferredContourEntries.TryPlan(prepared, outside, route, centers, Start,
            c => adapter.Check(c.Choice));
        Assert.True(preferred.IsPreferred, preferred.Reason);

        var cutout = preferred.HoleChoices[1];
        var next = PreferredContourEntries.Pierce(prepared, preferred.HoleChoices[2], default);
        var curves = ExecutionMotionReader.Read(prepared.EmitCandidateForValidation(cutout), Vector.Zero, null, default)
            .Motions.Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display).Select(m => m.Curve).ToArray();
        Assert.False(PostVerificationGeometry.Crosses(cutout.Point, next, curves, default),
            $"The cutout's entry {cutout.Point} leaves back across the cutout toward {next}.");
        var choices = preferred.HoleChoices.Append(outside).ToArray();
        var execution = ExecutionMotionReader.Read(prepared.Emit(choices), Vector.Zero, Start, default);
        Assert.Empty(new ReleasedContourState().Check(execution, Start, 1));
    }

    [Fact]
    public void PartsWithRoundedCutoutsPlanOnTheirPreferredHoleChains()
    {
        var parts = Enumerable.Range(0, 3)
            .Select(i => new Part(new Drawing($"rounded cutout {i}", RoundedCutoutBetweenHoles()), new Vector(i * 10, 0)))
            .ToArray();
        var fingerprints = parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)).ToArray();
        var snapshot = CuttingPlanService.Capture(new CuttingPlanRequest(parts, Start, confirmedParameters: Parameters()));
        var stall = JointCuttingPlanSearch.StallExpansionsPerEntry * snapshot.MaxEntries
            * snapshot.Placements.Max(p => p.Prepared.Count);

        // Within one stall the preferred pass must find every part's hole chain; a corner entry
        // whose way out re-enters the cutout fails each outside endpoint's chain in turn.
        var result = CuttingPlanService.Plan(snapshot.WithSearchLimits(stall, snapshot.MaxEntries));

        Assert.True(result.Status == CuttingPlanStatus.Ready,
            $"{result.Status}: " + string.Join("; ", result.Findings.Select(f => f.Message)));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(fingerprints, parts.Select(p => ExplicitContourTests.Fingerprint(p.Program)));
    }
}
