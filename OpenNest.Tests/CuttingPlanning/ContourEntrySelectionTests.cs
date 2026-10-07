using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Engine.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Tests.CuttingPlanning;

/// <summary>
/// The bounded, lazy selection over ranked candidates and the S07 verdict: cap first, side
/// coverage as a corrective scan, honest shortfall metadata — and the untouched tail is
/// never evaluated. Counts, not elapsed time.
/// </summary>
public class ContourEntrySelectionTests
{
    private static readonly PreparedContours Owner =
        PreparedContours.Capture(ExplicitContourTests.Square(false), ExplicitContourTests.Parameters());

    private static ContourEntryCandidate Cand(double x, double y, AutomaticEntryKind kind = AutomaticEntryKind.ConvexCorner) =>
        new(new ContourChoice(0, 0, new Vector(x, y)) { Owner = Owner }, kind);

    private static Func<ContourEntryCandidate, ContourFeasibilityVerdict> Feasible(params (double X, double Y)[] clearAt)
    {
        var set = new HashSet<(long, long)>(clearAt.Select(p => Cand(p.X, p.Y).GeometryKey));
        return c => set.Contains(c.GeometryKey)
            ? new(ContourFeasibilityStatus.Clear, null)
            : new(ContourFeasibilityStatus.Blocked, "test block");
    }

    private static Func<ContourEntryCandidate, ContourFeasibilityVerdict> AllClear() =>
        _ => new(ContourFeasibilityStatus.Clear, null);

    private static bool Selected(ContourSelectionResult result, double x, double y) =>
        result.Choices.Any(c => c.Point.DistanceTo(new Vector(x, y)) < 1e-9);

    [Fact]
    public void LaterSafeCandidateSurvivesTwentyRejectedRivals()
    {
        // 18 blocked facing candidates cannot crowd out the one safe point at the tail.
        var candidates = new List<ContourEntryCandidate>();
        for (var i = 0; i < 18; i++)
            candidates.Add(Cand(i * 0.001, 0));
        candidates.Add(Cand(100, 100));

        var result = ContourEntrySelection.Select(candidates, Feasible((100, 100)));

        Assert.Single(result.Choices);
        Assert.True(Selected(result, 100, 100));
        Assert.Equal(19, result.EvaluatedCount);
        Assert.Equal(ContourSelectionShortfall.Exhausted, result.Shortfall);
    }

    [Fact]
    public void EveryFeasibleSideIsRepresented()
    {
        // Corners on left/bottom and right/bottom, plus one point each on top-left and
        // top-right; a wide tail of blocked points so coverage must chase the sides lazily.
        var candidates = new List<ContourEntryCandidate>
        {
            Cand(0, 0), Cand(10, 0),            // bottom corners
            Cand(0, 10, AutomaticEntryKind.StraightMidpoint),   // top-left
            Cand(10, 10, AutomaticEntryKind.TangentJoint),      // top-right
        };
        for (var i = 1; i < 30; i++)
            candidates.Add(Cand(i * 0.3, 0, AutomaticEntryKind.NearCorner)); // blocked bottom tail

        var result = ContourEntrySelection.Select(candidates,
            Feasible((0, 0), (10, 0), (0, 10), (10, 10)), maxEntries: 6);

        Assert.Equal(4, result.Choices.Count);
        Assert.True(Selected(result, 0, 10));
        Assert.True(Selected(result, 10, 10));
        // The blocked tail was chased to its end exactly once per missing side scan.
        Assert.Equal(ContourSelectionShortfall.Exhausted, result.Shortfall);
    }

    [Fact]
    public void BlockedSideIsOmittedWithoutManufacturingPoints()
    {
        // No feasible candidate touches the top side.
        var candidates = new List<ContourEntryCandidate>
        {
            Cand(0, 0), Cand(10, 0), Cand(0, 10), Cand(10, 10),
        };

        var result = ContourEntrySelection.Select(candidates, Feasible((0, 0), (10, 0)));

        Assert.Equal(2, result.Choices.Count);
        Assert.False(Selected(result, 0, 10));
        Assert.False(Selected(result, 10, 10));
        // Every selected choice was feasible: nothing was manufactured.
        Assert.All(result.Choices, c => Assert.InRange(c.Point.Y, 0, 0));
    }

    [Fact]
    public void CapIsNeverExceededAndRankingComesFirst()
    {
        var candidates = new List<ContourEntryCandidate>();
        for (var i = 0; i < 10; i++)
            candidates.Add(Cand(10 - i * 0.01, 0)); // first-ranked points on the right/bottom

        var result = ContourEntrySelection.Select(candidates, AllClear(), maxEntries: 3);

        Assert.Equal(3, result.Choices.Count);
        Assert.Equal(3, result.EvaluatedCount); // stopped the moment the cap filled
        Assert.Equal(ContourSelectionShortfall.None, result.Shortfall);
        Assert.Equal(10.0, result.Choices[0].Point.X, 6); // rank-first point kept
    }

    [Fact]
    public void NormalCaseNeverEvaluatesTheUntouchedTail()
    {
        // Four corners first (all sides covered), then a long clear tail. The cap fills at
        // 16 with full coverage, so the chase runs empty and the tail stays untouched.
        var candidates = new List<ContourEntryCandidate>
        {
            Cand(0, 0), Cand(10, 0), Cand(0, 10), Cand(10, 10),
        };
        for (var i = 0; i < 36; i++)
            candidates.Add(Cand(1 + i * 0.1, 1));

        var result = ContourEntrySelection.Select(candidates, AllClear(), maxEntries: 16);

        Assert.Equal(16, result.Choices.Count);
        Assert.Equal(16, result.EvaluatedCount);
        Assert.DoesNotContain(candidates[16].GeometryKey, result.EvaluatedKeys);
        Assert.DoesNotContain(candidates[^1].GeometryKey, result.EvaluatedKeys);
        Assert.Equal(ContourSelectionShortfall.None, result.Shortfall);
    }

    [Fact]
    public void AllBlockedIsExhaustedGeometricImpossibility()
    {
        var candidates = new[] { Cand(0, 0), Cand(10, 0), Cand(0, 10), Cand(10, 10) };

        var result = ContourEntrySelection.Select(candidates,
            _ => new(ContourFeasibilityStatus.Blocked, "test block"));

        Assert.Empty(result.Choices);
        Assert.Equal(4, result.EvaluatedCount);
        Assert.Equal(ContourSelectionShortfall.Exhausted, result.Shortfall);
        Assert.Contains("No tested lead-in fits", result.Reason);
    }

    [Fact]
    public void IncompleteCheckIsNeverGeometricImpossibility()
    {
        var candidates = new[] { Cand(0, 0), Cand(10, 0) };
        var first = true;

        var result = ContourEntrySelection.Select(candidates, _ =>
        {
            ContourFeasibilityVerdict verdict = first
                ? new(ContourFeasibilityStatus.Incomplete, "incomplete material")
                : new(ContourFeasibilityStatus.Clear, null);
            first = false;
            return verdict;
        });

        Assert.Equal(ContourSelectionShortfall.Incomplete, result.Shortfall);
        Assert.DoesNotContain("No tested lead-in fits", result.Reason ?? string.Empty);
        Assert.Contains("not a geometric verdict", result.Reason);
    }

    [Fact]
    public void SmallCapsObeyTheCapNotCoverage()
    {
        var candidates = new List<ContourEntryCandidate>
        {
            Cand(0, 0), Cand(10, 0), Cand(0, 10), Cand(10, 10),
        };

        var cap1 = ContourEntrySelection.Select(candidates, AllClear(), maxEntries: 1);
        var cap3 = ContourEntrySelection.Select(candidates, AllClear(), maxEntries: 3);

        Assert.Single(cap1.Choices);
        Assert.Equal(3, cap3.Choices.Count);
        Assert.Equal(1, cap1.EvaluatedCount); // cap fills before any coverage scan
        Assert.Equal(3, cap3.EvaluatedCount);
    }

    [Fact]
    public void InvalidCapStaysInvalid()
    {
        Assert.Throws<ArgumentException>(() =>
            ContourEntrySelection.Select(Array.Empty<ContourEntryCandidate>(), _ => null!, 0));
        Assert.Throws<ArgumentException>(() =>
            ContourEntrySelection.Select(Array.Empty<ContourEntryCandidate>(), _ => null!, -1));
    }

    [Fact]
    public void DuplicatePointsNeverConsumeTwoSlots()
    {
        var candidates = new[] { Cand(5, 5), Cand(5, 5), Cand(5, 5) };

        var result = ContourEntrySelection.Select(candidates, AllClear(), maxEntries: 2);

        Assert.Single(result.Choices);
        Assert.Equal(1, result.EvaluatedCount); // deduplicated before evaluation
    }

    [Fact]
    public void EmptyCatalogueIsExhaustedWithoutEvaluation()
    {
        var result = ContourEntrySelection.Select(Array.Empty<ContourEntryCandidate>(), _ => null!);

        Assert.Empty(result.Choices);
        Assert.Equal(0, result.EvaluatedCount);
        Assert.Equal(ContourSelectionShortfall.Exhausted, result.Shortfall);
    }

    [Fact]
    public void CancellationStopsBeforeAnyEvaluation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var candidates = new[] { Cand(0, 0) };

        Assert.ThrowsAny<OperationCanceledException>(() =>
            ContourEntrySelection.Select(candidates, _ => new(ContourFeasibilityStatus.Clear, null),
                token: cancelled.Token));
    }

    [Fact]
    public void CoverageReplacementDropsTheWorstAndResorts()
    {
        // Cap 5 fills on points covering left/bottom/top; the right side is chased to (6,6),
        // which must displace the worst selected point whose removal preserves the others
        // ((3,4), the top-only cover is re-covered by (6,6)) and settle at its true rank slot.
        var candidates = new List<ContourEntryCandidate>
        {
            Cand(0, 0), Cand(2, 0), Cand(0, 2), Cand(0, 4), Cand(3, 4), Cand(6, 6),
        };

        var result = ContourEntrySelection.Select(candidates, AllClear(), maxEntries: 5);

        Assert.Equal(5, result.Choices.Count);
        Assert.True(Selected(result, 6, 6));
        Assert.False(Selected(result, 3, 4));
        Assert.Equal(6, result.EvaluatedCount);
        // Evaluation order IS global rank order; the late chaser sorts to the end.
        Assert.Equal(6, result.Choices[^1].Point.X);
        Assert.Equal(0, result.Choices[0].Point.X);
        Assert.Equal(0, result.Choices[0].Point.Y);
    }

    [Fact]
    public void CoverageNeverDestroysTheOnlyCoverOfAnotherSide()
    {
        // (2,0) is the ONLY cover of bottom; chasing the right side must not remove it even
        // though it outranks nothing. The chaser replaces (2,4) (top, re-covered by (6,6)).
        var candidates = new List<ContourEntryCandidate>
        {
            Cand(0, 0), Cand(2, 0), Cand(0, 2), Cand(2, 4), Cand(6, 6),
        };

        var result = ContourEntrySelection.Select(candidates, AllClear(), maxEntries: 4);

        // Cap 4: phase 1 keeps the first four (right not covered: (2,4) left+top only).
        Assert.True(Selected(result, 2, 0));   // sole bottom cover survives
        Assert.True(Selected(result, 6, 6));   // right-side chaser placed
        Assert.False(Selected(result, 2, 4));  // safely displaced (top re-covered)
        Assert.Equal(ContourSelectionShortfall.None, result.Shortfall);
    }
}
