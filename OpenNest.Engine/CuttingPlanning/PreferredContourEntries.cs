#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Geometry;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>
/// One preferred sequence of owned contour choices for a holed part: every remaining hole
/// gets exactly one lead-feasible entry, resolved BACKWARD from the already-chosen
/// perimeter entry — the last hole faces the perimeter's actual emitted pierce, each earlier
/// hole faces the next hole's actual emitted pierce — so the cut chain flows toward the
/// outside start. This is a deterministic recommended v1 proposal, not an optimal joint
/// tour and not an installed program: the search still certifies every rapid, lead and
/// crossing. A blocked preferred rapid is the search's problem (S12), never ignored here.
/// </summary>
internal static class PreferredContourEntries
{
    /// <summary>Proposed hole choices in cut order, or an explicit no-preference verdict.</summary>
    internal sealed record Proposal(
        IReadOnlyList<ContourChoice> HoleChoices,
        ContourSelectionShortfall Shortfall,
        int? BlockedContour,
        string? Reason)
    {
        public bool IsPreferred => Shortfall != ContourSelectionShortfall.Incomplete
            && HoleChoices.Count > 0 && BlockedContour is null;
    }

    /// <summary>
    /// Resolves one preferred entry per hole on <paramref name="holeRoute"/> (cut order,
    /// from <see cref="CuttingHoleOrder"/>), ending at <paramref name="perimeterChoice"/>.
    /// Arrival proxies avoid circularity: the actual (local) part arrival for the first
    /// hole, otherwise the previous hole's centre. Facing target (the downstream actual
    /// pierce) and tier still rank first. <paramref name="evaluate"/> is the S07 adapter
    /// verdict; candidates it refuses or leaves uncertain are not preferred.
    /// </summary>
    internal static Proposal TryPlan(
        PreparedContours prepared,
        ContourChoice perimeterChoice,
        IReadOnlyList<int> holeRoute,
        IReadOnlyList<Vector> centresByOrdinal,
        Vector arrival,
        Func<ContourEntryCandidate, ContourFeasibilityVerdict> evaluate,
        CancellationToken token = default)
    {
        if (prepared == null)
            throw new ArgumentException("Prepared contours are required.", nameof(prepared));
        if (perimeterChoice == null || !ReferenceEquals(perimeterChoice.Owner, prepared))
            throw new ArgumentException("The perimeter choice must belong to this preparation.", nameof(perimeterChoice));
        if (holeRoute == null || centresByOrdinal == null || evaluate == null)
            throw new ArgumentException("Hole route, centres and evaluation are required.");
        token.ThrowIfCancellationRequested();
        if (holeRoute.Count == 0)
            return new(Array.Empty<ContourChoice>(), ContourSelectionShortfall.Exhausted, null, null);

        // The downstream target is where the tool ACTUALLY arrives next: the emitted
        // contour's first cut/lead motion start (native rounding/clamping included).
        var target = Pierce(prepared, perimeterChoice, token);
        var choices = new ContourChoice[holeRoute.Count];
        for (var index = holeRoute.Count - 1; index >= 0; index--)
        {
            token.ThrowIfCancellationRequested();
            var contour = holeRoute[index];
            var arrivalProxy = index == 0 ? arrival : centresByOrdinal[holeRoute[index - 1]];
            // The S04 merged catalogue: preferred kinds when present, fallback kinds
            // (compass points, arc midpoints, ...) otherwise — pure circles included.
            var catalogue = prepared.AutomaticEntryCandidatesWithFallbacks(contour, target, token);
            var ranked = catalogue.RankTowardNextCut(target, arrivalProxy);
            var selection = ContourEntrySelection.Select(ranked, evaluate, 1, token);
            if (selection.Choices.Count == 0)
                return new(Array.Empty<ContourChoice>(), selection.Shortfall, contour,
                    $"Hole contour {contour} has no preferred lead-feasible entry: {selection.Reason}");
            choices[index] = selection.Choices[0];
            target = Pierce(prepared, choices[index], token);
        }

        if (choices.Any(c => c == null!))
            throw new InvalidOperationException("Preferred hole resolution left a gap.");
        return new(choices, ContourSelectionShortfall.None, null, null);
    }

    /// <summary>
    /// The actual emitted pierce of one owned choice in local coordinates: the start of the
    /// first non-rapid motion (lead-in when present — that is where the next rapid must
    /// arrive), which reflects native rounding and clamping of a nominal entry.
    /// </summary>
    private static Vector Pierce(PreparedContours prepared, ContourChoice choice, CancellationToken token)
    {
        var program = prepared.EmitCandidateForValidation(choice);
        var execution = ExecutionMotionReader.Read(program, Vector.Zero, null, token);
        var first = execution.Motions.First(m => !m.Rapid);
        return first.Start ?? first.End;
    }
}
