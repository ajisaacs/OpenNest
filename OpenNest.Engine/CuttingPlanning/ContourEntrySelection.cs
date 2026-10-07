#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>Why a selection ended the way it did. Honest metadata, never a geometric overclaim.</summary>
internal enum ContourSelectionShortfall
{
    /// <summary>Selection ended at the cap with the side scan satisfied: a complete verdict.</summary>
    None,

    /// <summary>
    /// The finite catalogue was fully evaluated and the selection is all there is. With an
    /// empty selection this is exactly the sentence "no tested lead-in fits on part N,
    /// contour M" — and ONLY here may that sentence be spoken.
    /// </summary>
    Exhausted,

    /// <summary>
    /// At least one check could not complete (incomplete material or emission). Never
    /// present this as geometric impossibility and never as "nothing fits".
    /// </summary>
    Incomplete,
}

/// <summary>
/// Outcome of one bounded selection: selected choices in global rank order, how many
/// distinct candidates were evaluated, which ones (in catalogue order, for cost tests),
/// which evaluated uncertain (never refused — the caller's full check stays the authority
/// on those), and why the selection stopped.
/// </summary>
internal sealed record ContourSelectionResult(
    IReadOnlyList<ContourChoice> Choices,
    int EvaluatedCount,
    IReadOnlyList<(long, long)> EvaluatedKeys,
    IReadOnlyList<ContourChoice> UncertainChoices,
    ContourSelectionShortfall Shortfall,
    string? Reason);

/// <summary>
/// Bounded, lazy selection of lead-feasible entry candidates for ONE contour. Greedy global
/// rank order until the cap (default 16) fills or the finite catalogue ends — rejected and
/// incomplete candidates never consume a slot. When the cap can afford it (5+), one
/// corrective scan makes sure every side of the candidate bounding rectangle that HAS a
/// feasible candidate is represented, replacing the worst selected candidate only when
/// every other covered side survives; one corner may cover two sides. The cap is never
/// exceeded and points are never manufactured on infeasible sides — a capped selection may
/// truthfully omit a feasible side. Sides are exactly the ranker's side geometry (the
/// candidate-set bounding rectangle). Verdicts are memoized per attempt (the adapter
/// memoizes too): no candidate is ever evaluated twice, and once selection settles the
/// untouched tail is never evaluated. No search/DFS changes and no larger search budget.
/// </summary>
internal static class ContourEntrySelection
{
    internal const int DefaultMaxEntries = 16;

    /// <summary>Below this cap all-side coverage is not promised; the cap and ranking bind first (caps 1-3).</summary>
    internal const int SideCoverageMinCap = 4;

    /// <summary>
    /// Selects up to <paramref name="maxEntries"/> feasible choices from one contour's
    /// globally ranked catalogue. <paramref name="evaluate"/> is the S07 adapter verdict,
    /// called lazily at most once per distinct candidate.
    /// </summary>
    internal static ContourSelectionResult Select(
        IReadOnlyList<ContourEntryCandidate> rankedCandidates,
        Func<ContourEntryCandidate, ContourFeasibilityVerdict> evaluate,
        int maxEntries = DefaultMaxEntries,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (rankedCandidates == null)
            throw new ArgumentException("Ranked candidates are required.", nameof(rankedCandidates));
        if (evaluate == null)
            throw new ArgumentException("A feasibility evaluation is required.", nameof(evaluate));
        if (maxEntries <= 0)
            throw new ArgumentException("The cap must be positive.", nameof(maxEntries));

        // Distinct points only — the cap must not double-count a geometric duplicate.
        var candidates = new List<ContourEntryCandidate>();
        var seen = new HashSet<(long, long)>();
        foreach (var candidate in rankedCandidates)
            if (seen.Add(candidate.GeometryKey))
                candidates.Add(candidate);

        var evaluated = new List<ContourEntryCandidate>();
        var verdicts = new Dictionary<(long, long), ContourFeasibilityVerdict>();
        var box = Box(candidates);

        var selected = new List<ContourEntryCandidate>();
        var uncertain = new List<ContourEntryCandidate>();
        var sawIncomplete = false;
        string? incompleteReason = null;
        var index = 0;

        ContourFeasibilityVerdict Verdict(ContourEntryCandidate candidate)
        {
            if (!verdicts.TryGetValue(candidate.GeometryKey, out var known))
            {
                token.ThrowIfCancellationRequested();
                known = evaluate(candidate);
                verdicts[candidate.GeometryKey] = known;
                evaluated.Add(candidate);
            }
            return known;
        }

        // Phase 1: greedy global order until the cap fills or the catalogue ends.
        for (; index < candidates.Count && selected.Count < maxEntries; index++)
        {
            var verdict = Verdict(candidates[index]);
            if (verdict.Status == ContourFeasibilityStatus.Incomplete)
            {
                // Uncertain is not refused: it never takes a selected slot, but the scan
                // continues and the caller's full check stays the authority on it.
                sawIncomplete = true;
                incompleteReason ??= verdict.Reason;
                uncertain.Add(candidates[index]);
                continue;
            }
            if (verdict.IsClear)
                selected.Add(candidates[index]);
        }

        // Phase 2: side coverage when the cap affords it. Every missing side with an
        // unexamined tail is chased lazily; a clear candidate on that side is appended when
        // a slot remains, otherwise it replaces the worst selected candidate whose removal
        // keeps every other covered side covered.
        var coverage = maxEntries >= SideCoverageMinCap && selected.Count > 0;
        if (coverage)
            for (var side = 0; side < 4 && !sawIncomplete; side++)
            {
                if (selected.Any(c => Sides(c, box).Contains(side)))
                    continue;
                for (; index < candidates.Count; index++)
                {
                    var verdict = Verdict(candidates[index]);
                    if (verdict.Status == ContourFeasibilityStatus.Incomplete)
                    {
                        sawIncomplete = true;
                        incompleteReason ??= verdict.Reason;
                        uncertain.Add(candidates[index]);
                        continue;
                    }
                    if (!verdict.IsClear || !Sides(candidates[index], box).Contains(side))
                        continue;
                    TryPlace(selected, candidates[index], box, maxEntries);
                    break;
                }
            }

        var exhausted = index >= candidates.Count;
        ContourSelectionShortfall shortfall;
        string? reason;
        if (sawIncomplete)
        {
            shortfall = ContourSelectionShortfall.Incomplete;
            reason = "At least one lead check could not complete; this is not a geometric verdict and nothing is proven impossible."
                + (incompleteReason == null ? "" : $" First reason: {incompleteReason}");
        }
        else if (selected.Count >= maxEntries)
        {
            shortfall = ContourSelectionShortfall.None;
            reason = null;
        }
        else if (exhausted)
        {
            shortfall = ContourSelectionShortfall.Exhausted;
            reason = selected.Count == 0
                ? "No tested lead-in fits on this contour."
                : null;
        }
        else
        {
            // Unreachable: phase 1 ends at cap or catalogue end and phase 2 chases every
            // side to the end; a cancellation throws before this point.
            shortfall = ContourSelectionShortfall.Incomplete;
            reason = "Selection stopped before the catalogue ended.";
        }

        var ordered = selected
            .OrderBy(c => evaluated.FindIndex(x => x.GeometryKey == c.GeometryKey) is var e && e >= 0
                ? e : candidates.FindIndex(x => x.GeometryKey == c.GeometryKey))
            .Select(c => c.Choice)
            .ToList();
        return new(ordered, evaluated.Count,
            evaluated.Select(c => c.GeometryKey).ToList(),
            uncertain.Select(c => c.Choice).ToList(), shortfall, reason);
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) Box(
        List<ContourEntryCandidate> candidates)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity,
            maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var c in candidates)
        {
            var p = c.Choice.Point;
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
        if (!double.IsFinite(minX))
            return (0, 0, 0, 0);
        return (minX, minY, maxX, maxY);
    }

    /// <summary>Sides 0 left, 1 right, 2 bottom, 3 top of the candidate bounding rectangle that the point sits on.</summary>
    private static List<int> Sides(ContourEntryCandidate candidate,
        (double MinX, double MinY, double MaxX, double MaxY) box)
    {
        var p = candidate.Choice.Point;
        var left = p.X - box.MinX;
        var right = box.MaxX - p.X;
        var bottom = p.Y - box.MinY;
        var top = box.MaxY - p.Y;
        var min = System.Math.Min(System.Math.Min(left, right), System.Math.Min(bottom, top));
        var sides = new List<int>(2);
        if (left <= min + PostVerificationGeometry.Epsilon) sides.Add(0);
        if (right <= min + PostVerificationGeometry.Epsilon) sides.Add(1);
        if (bottom <= min + PostVerificationGeometry.Epsilon) sides.Add(2);
        if (top <= min + PostVerificationGeometry.Epsilon) sides.Add(3);
        return sides;
    }

    /// <summary>Appends when a slot remains, else replaces the worst (latest-ranked) candidate whose removal preserves every other covered side.</summary>
    private static void TryPlace(List<ContourEntryCandidate> selected, ContourEntryCandidate candidate,
        (double MinX, double MinY, double MaxX, double MaxY) box, int maxEntries)
    {
        if (selected.Count < maxEntries)
        {
            selected.Add(candidate);
            return;
        }
        var incoming = Sides(candidate, box);
        for (var i = selected.Count - 1; i >= 0; i--)
        {
            var removalSafe = true;
            for (var side = 0; side < 4 && removalSafe; side++)
            {
                if (incoming.Contains(side))
                    continue; // the replacement covers it
                var covers = selected.Where((c, at) => at != i && Sides(c, box).Contains(side)).Any();
                var wasCovered = selected.Any(c => Sides(c, box).Contains(side));
                if (wasCovered && !covers)
                    removalSafe = false;
            }
            if (removalSafe)
            {
                selected[i] = candidate;
                return;
            }
        }
        // No safe victim: the side stays truthfully unrepresented at this cap.
    }
}
