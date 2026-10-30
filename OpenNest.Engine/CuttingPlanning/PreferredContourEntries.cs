#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.CNC;
using OpenNest.CNC.CuttingPlanning;
using OpenNest.Diagnostics;
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
            var ranked = catalogue.RankTowardNextCut(target, arrivalProxy).DepartingClear(prepared, target, token);
            var selection = ContourEntrySelection.Select(ranked, evaluate, 1, token);
            if (selection.Choices.Count == 0
                && selection.Shortfall is ContourSelectionShortfall.Exhausted or ContourSelectionShortfall.Incomplete)
            {
                var eroded = ContourEntrySelection.Select(ErodedPierces(prepared, contour, ranked, 1, token), evaluate, 1, token);
                if (eroded.Choices.Count != 0)
                    selection = eroded;
            }
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
    /// The fallback for a cutout none of whose <paramref name="ranked"/> entries has a configured
    /// lead-in that fits: up to <paramref name="max"/> entries pierced where eroding the cutout
    /// leaves room, nearest each ranked entry in turn (see
    /// <see cref="PreparedContours.ErodedPierceEntries"/>). Proposals only: the lead checks decide.
    /// </summary>
    internal static IReadOnlyList<ContourEntryCandidate> ErodedPierces(PreparedContours prepared, int contour,
        IReadOnlyList<ContourEntryCandidate> ranked, int max, CancellationToken token) =>
        prepared.ErodedPierceEntries(contour, ranked.Select(c => c.Choice).ToArray(), max, token)
            .Select(choice => new ContourEntryCandidate(choice, AutomaticEntryKind.ErodedPierce)).ToArray();

    /// <summary>
    /// Stable reordering of one hole's ranked entries: those whose straight rapid to
    /// <paramref name="target"/> (the next cut's pierce, local) clears the hole itself come
    /// first. With no lead-out the tool leaves a hole where it entered it, so from an entry
    /// whose way out turns back into the hole (a tiny corner arc's midpoint, whose tangent
    /// leaves at 45 degrees, ranks as a two-side corner) the rapid onward re-enters the hole
    /// just cut and is refused. Order only: every entry stays and every rapid is still checked.
    /// </summary>
    internal static IReadOnlyList<ContourEntryCandidate> DepartingClear(this IReadOnlyList<ContourEntryCandidate> ranked,
        PreparedContours prepared, Vector target, CancellationToken token)
    {
        if (ranked.Count < 2)
            return ranked;
        IReadOnlyList<PostVerificationGeometry.Curve> contour;
        try
        {
            // Any entry's probe cuts the same contour (a tab gap aside); scribes and leads are not part of it.
            var execution = ExecutionMotionReader.Read(prepared.EmitCandidateForValidation(ranked[0].Choice),
                Vector.Zero, null, token);
            contour = execution.Motions
                .Where(m => !m.Rapid && m.Layer is LayerType.Cut or LayerType.Display && m.Curve != null)
                .Select(m => m.Curve).ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArithmeticException or NotSupportedException)
        {
            return ranked; // An unemittable probe is the lead checks' finding, not the ranker's.
        }
        return ranked.OrderBy(c => PostVerificationGeometry.Crosses(c.Choice.Point, target, contour, token) ? 1 : 0).ToList();
    }

    /// <summary>
    /// The actual emitted pierce of one owned choice in local coordinates: the start of the
    /// first non-rapid motion (lead-in when present — that is where the next rapid must
    /// arrive), which reflects native rounding and clamping of a nominal entry.
    /// </summary>
    internal static Vector Pierce(PreparedContours prepared, ContourChoice choice, CancellationToken token)
    {
        var program = prepared.EmitCandidateForValidation(choice);
        var execution = ExecutionMotionReader.Read(program, Vector.Zero, null, token);
        // The diagnostic emission includes the part's scribes before the selected contour.
        // Those marks are not the downstream cut's pierce.
        var first = execution.Motions.First(m => !m.Rapid && m.Layer != LayerType.Scribe);
        return first.Start ?? first.End;
    }
}
