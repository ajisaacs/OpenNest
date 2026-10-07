using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// S09: at a part boundary the outside contour's automatic entry faces the NEXT part, and
/// the emitted rapids are no worse than the measured legacy layout (BASELINE: legacy picks
/// the entry nearest the arrival — for this fixture the lower-left corner each time, so
/// every departure trails the left edge and each inter-part rapid carries the full 10.1
/// pitch plus lead offsets). No-hole parts only — holed parts keep the legacy path for S12.
/// </summary>
public class PerimeterLookAheadTests
{
    // Measured on the legacy search before this slice (Capture_LegacyBaselineNumbers,
    // three squares at 0/10.5/21, origin start, 0.15 line leads, no lead-out): every entry
    // the arrival-nearest lower-left corner (0,0); rapids 0.000000, 10.500000, 10.500000;
    // total 21.000000. The look-ahead start must not make the total worse and must face
    // the next part.
    private const double LegacyTotalRapids = 21.0;

    private static CuttingParameters Parameters()
    {
        var parameters = ExplicitContourTests.Parameters();
        parameters.ExternalLeadIn = new LineLeadIn { Length = 0.15, ApproachAngle = 45 };
        parameters.ExternalLeadOut = new NoLeadOut();
        return parameters;
    }

    private static Part Square(double x, CuttingParameters parameters, string name = "sq")
    {
        var part = new Part(new Drawing(name, LeadPathValidationTests.Rectangle(0, 0, 10, 10)),
            new Vector(x, 0));
        part.CuttingParameters = parameters;
        return part;
    }

    private static CuttingPlanRequest Request(Part[] parts, CuttingParameters parameters, int budget = 20000) =>
        new(parts, Vector.Zero, budget, parameters);

    /// <summary>Actual air moves: each rapid's distance from the previous motion's end to the following cut end.</summary>
    private static List<double> Rapids(CuttingPlanResult result)
    {
        var rapids = new List<double>();
        var position = Vector.Zero;
        foreach (var placement in result.ProposedOrder)
        {
            var motions = placement.Execution.Motions;
            for (var i = 0; i < motions.Count; i++)
            {
                if (!motions[i].Rapid)
                    continue;
                var next = motions.Skip(i + 1).First(m => !m.Rapid);
                if (position.DistanceTo(motions[i].End) > 1e-9)
                    rapids.Add(position.DistanceTo(next.End));
                position = next.End;
            }
            position = placement.Execution.DeparturePoint;
        }
        return rapids;
    }

    [Fact]
    public void ThreePartsAlongX_LeftToRightReadyWithFirstStartFacingTheNextPart()
    {
        var parameters = Parameters();
        var parts = new[] { Square(0, parameters), Square(10.5, parameters), Square(21.0, parameters) };

        var result = CuttingPlanService.Plan(Request(parts, parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(result.IndependentlyReplayed);
        Assert.Equal(3, result.ProposedOrder.Count);

        var first = result.ProposedOrder[0];
        var entry = Assert.Single(first.ContourChoices).Point + first.Location;
        // Facing the next part (centre 15.5, 5) means the +X side of the first sheet,
        // not the legacy arrival-nearest lower-left corner.
        Assert.True(entry.X >= 10.0 - 1e-9, $"first entry {entry} faces away from the next part");
    }

    [Fact]
    public void EmittedRapidsNoWorseThanTheMeasuredLegacyLayout()
    {
        var parameters = Parameters();
        var parts = new[] { Square(0, parameters), Square(10.5, parameters), Square(21.0, parameters) };

        var result = CuttingPlanService.Plan(Request(parts, parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.True(Rapids(result).Sum() <= LegacyTotalRapids + 1e-6,
            $"total rapids {Rapids(result).Sum():F6} exceed legacy {LegacyTotalRapids:F6}");
    }

    [Fact]
    public void SecondPartFacesThird_AndLastPartFacesArrivalNotOrigin()
    {
        var parameters = Parameters();
        var parts = new[] { Square(0, parameters), Square(10.5, parameters), Square(21.0, parameters) };

        var result = CuttingPlanService.Plan(Request(parts, parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var ordered = result.ProposedOrder.OrderBy(p => p.SourceOrdinal).ToArray();
        var secondEntry = Assert.Single(ordered[1].ContourChoices).Point + ordered[1].Location;
        // Facing the third part (centre 26.0, 5): +X side of the middle sheet.
        Assert.True(secondEntry.X >= 10.5 + 10.0 - 1e-9, $"second entry {secondEntry} faces away from the third part");
        // Last part: no target — its entry is chosen near the arrival point, never pulled
        // toward the plate origin.
        var arrival = ordered[1].Execution.DeparturePoint;
        var lastEntry = Assert.Single(ordered[2].ContourChoices).Point + ordered[2].Location;
        Assert.True(arrival.DistanceTo(lastEntry) < Vector.Zero.DistanceTo(lastEntry),
            $"last entry {lastEntry} is nearer the plate origin than the arrival {arrival}");
    }

    [Fact]
    public void RotatedAndTranslatedLayout_FacingIsInGlobalSpace()
    {
        var parameters = Parameters();
        var sheet = Square(0, parameters);
        sheet.Rotate(System.Math.PI / 4); // about the origin: the diamond spans x -7.07..7.07
        var parts = new[] { sheet, Square(25, parameters) };

        var result = CuttingPlanService.Plan(Request(parts, parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var rotated = result.ProposedOrder.Single(p => p.SourceOrdinal == 0);
        var entry = rotated.ContourChoices.Single().Point + rotated.Location;
        // The diamond's +X half faces the next part at x=25; the legacy arrival-nearest
        // point would be the origin corner (0,0) or below.
        Assert.True(entry.X > 3.0, $"rotated first entry {entry} faces away from the next part");
    }

    [Fact]
    public void BlockedPreferredStart_CertifiedFallbackStillReady()
    {
        // A sheet hugging the right side of part 1 (0.05 gap) blocks every +X-facing lead;
        // the planner must fall back to a candidate whose emitted leads certify clear.
        var parameters = Parameters();
        var blocker = new Part(new Drawing("block", LeadPathValidationTests.Rectangle(0, -2, 9.9, 12)), Vector.Zero);
        blocker.Location = new Vector(10.05, 0);
        blocker.CuttingParameters = parameters;
        var parts = new[] { Square(0, parameters), blocker, Square(20.4, parameters) };

        var result = CuttingPlanService.Plan(Request(parts, parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var first = result.ProposedOrder.Single(p => p.SourceOrdinal == 0);
        var entry = first.ContourChoices.Single().Point + first.Location;
        var material = LeadMaterialSnapshot.Capture(
            LeadPathValidationTests.Rectangle(0, 0, 10, 10), Vector.Zero);
        var execution = ExecutionMotionReader.Read(first.CopyProgram(), first.Location, null, default);
        var leads = LeadPathValidator.Check(execution, material, []);
        Assert.True(leads.IsClear, leads.Reason);
    }

    [Fact]
    public void NoCandidateFits_SurfacesContourFindingNotBudget()
    {
        // Shrink-wrap material 0.05 around part 1: every external lead enters it, so the
        // finite candidate catalogue PROVES no lead-in fits; the search must say that, not
        // hide behind the expansion budget.
        var parameters = Parameters();
        var wrap = new Part(new Drawing("wrap", LeadPathValidationTests.Rectangle(-0.05, -0.05, 10.05, 10.05)),
            Vector.Zero);
        wrap.CuttingParameters = parameters;
        var square = Square(0, parameters);
        var request = new CuttingPlanRequest([square, wrap], Vector.Zero, 20000, parameters);

        var result = CuttingPlanService.Plan(request);

        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, result.Status);
        Assert.Contains(result.Findings,
            f => (f.Message ?? string.Empty).Contains("No tested lead-in fits", StringComparison.Ordinal));
        Assert.DoesNotContain(result.Findings,
            f => (f.Message ?? string.Empty).Contains("Expansion budget", StringComparison.Ordinal));
    }

    [Fact]
    public void BudgetOne_ReportsBudgetNotImpossibility()
    {
        var parameters = Parameters();
        var parts = new[] { Square(0, parameters), Square(10.5, parameters) };

        var result = CuttingPlanService.Plan(Request(parts, parameters, budget: 1));

        Assert.Equal(CuttingPlanStatus.NoSolutionWithinBudget, result.Status);
        Assert.Contains(result.Findings,
            f => (f.Message ?? string.Empty).Contains("budget", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Findings,
            f => (f.Message ?? string.Empty).Contains("No tested lead-in fits", StringComparison.Ordinal));
    }

    [Fact]
    public void CancellationIsHonouredMidSearch()
    {
        var parameters = Parameters();
        var parts = Enumerable.Range(0, 6).Select(i => Square(i * 10.5, parameters)).ToArray();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        var result = CuttingPlanService.Plan(Request(parts, parameters), cancel.Token);

        Assert.Equal(CuttingPlanStatus.Cancelled, result.Status);
    }

    [Fact]
    public void LockedProgramFingerprintIsUntouchedByLookAhead()
    {
        var parameters = Parameters();
        var locked = Square(0, parameters);
        var prepared = PreparedContours.Capture(locked.Program, parameters);
        var emitted = prepared.Emit([prepared.ClosestEntry(0, Vector.Zero)]);
        Assert.True(locked.RestoreLeadInProgram(emitted, false));
        locked.LeadInsLocked = true;
        var before = ExplicitContourTests.Fingerprint(locked.Program);

        var result = CuttingPlanService.Plan(Request([locked, Square(10.5, parameters)], parameters));

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        Assert.Equal(before, ExplicitContourTests.Fingerprint(locked.Program));
        var placement = result.ProposedOrder.Single(p => p.SourceOrdinal == 0);
        Assert.False(placement.IsRegenerated); // locked programs never gain automatic choices
    }

    [Fact]
    public void PreservePartOrder_RightToLeftFirstStartFacesLeft()
    {
        // Right-to-left supplied sequence: the x=20.2 sheet is cut first and must face the
        // next part on its -X side.
        var parameters = Parameters();
        var parts = new[] { Square(21.0, parameters), Square(10.5, parameters), Square(0, parameters) };
        var request = new CuttingPlanRequest(parts, Vector.Zero, 20000, parameters, preservePartOrder: true);

        var result = CuttingPlanService.Plan(request);

        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var first = result.ProposedOrder[0];
        Assert.Equal(0, first.SourceOrdinal);
        var entry = first.ContourChoices.Single().Point + first.Location;
        // Facing the next part (centre 15.5, 5) means the -X side of the x=21 sheet.
        Assert.True(entry.X <= 21.0 + 1e-9, $"first entry {entry} faces away from the next part");

        // Last part (x=0..10): no target — arrival-nearest means its +X side (the tool
        // arrives from the middle sheet), NOT the plate-origin corner at (0,0).
        var last = result.ProposedOrder[2];
        var lastEntry = last.ContourChoices.Single().Point + last.Location;
        Assert.True(lastEntry.X >= 10.0 - 1e-9,
            $"last entry {lastEntry} faces the plate origin instead of the arrival");
    }

    [Fact]
    public void FullFallbackSearch_TargetsNearestReadyPart()
    {
        // Middle-first geometry: the leftmost part is blocked until the middle one moves
        // (forced by a shrink-wrap on its other side), so the learned-order replay must
        // recompute look-ahead after replanning — the middle part must face whichever part
        // the new sequence cuts after it, never a stale target.
        var parameters = Parameters();
        var parts = new[] { Square(0, parameters), Square(10.5, parameters), Square(21.0, parameters) };

        var result = CuttingPlanService.Plan(Request(parts, parameters));

        // Sanity: normal Ready; the facing entry of each non-last part faces its successor
        // in the RESULTING order, not merely the request order.
        Assert.True(result.Status == CuttingPlanStatus.Ready, Describe(result));
        var sequence = result.ProposedOrder.Select(p => p.SourceOrdinal).ToArray();
        for (var i = 0; i < sequence.Length - 1; i++)
        {
            var entry = result.ProposedOrder[i].ContourChoices.Single().Point + result.ProposedOrder[i].Location;
            var nextCentre = Centre(result.ProposedOrder[i + 1]);
            var here = result.ProposedOrder[i].Location;
            // The opposite side of the current sheet from the next part: the facing check
            // is "entry is closer to the next centre than the wrong-side point is".
            var wrongSide = nextCentre.X >= here.X + 5
                ? new Vector(here.X - 5, here.Y + 5)
                : new Vector(here.X + 15, here.Y + 5);
            Assert.True(entry.DistanceTo(nextCentre) < wrongSide.DistanceTo(nextCentre),
                $"order slot {i} entry {entry} faces away from next centre {nextCentre}");
        }
    }

    private static Vector Centre(FixedProgramPlacement placement)
    {
        var cuts = placement.Execution.Motions
            .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null)
            .Select(m => m.Curve.ToEntity().BoundingBox).ToList();
        return cuts.Count == 0 ? placement.Execution.DeparturePoint : cuts.GetBoundingBox().Center;
    }

    private static string Describe(CuttingPlanResult result) =>
        $"{result.Status}: {string.Join("; ", result.Findings.Take(5).Select(f => f.Message))}";
}
