using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CommonLines;
using OpenNest.Converters;

namespace OpenNest.Engine.Jobs.CommonLines;

/// <summary>
/// Decides whether a common-line layout replaces the best layout without common lines (or an
/// earlier common-line trial), by the job's <see cref="CommonLineUsage"/>. Both must be valid,
/// keepable proposals of the same job; an invalid incumbent always loses to a valid candidate.
/// The engines' own ranking (and <c>FillScore</c>) is untouched: this only chooses between the
/// layouts they returned.
/// </summary>
internal static class CommonLineRanking
{
    /// <summary>Differences below this are rounding, never a reason to prefer either layout.</summary>
    private const double Epsilon = 1e-6;

    internal static bool Prefers(CommonLineUsage usage, NestPipelineResult candidate, NestPipelineResult incumbent)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(incumbent);
        if (usage == CommonLineUsage.Off || !Keepable(candidate))
            return false;
        if (!Keepable(incumbent))
            return true;
        var placed = Placed(candidate).CompareTo(Placed(incumbent));
        if (placed != 0)
            return placed > 0;
        var sheets = Sheets(candidate).CompareTo(Sheets(incumbent));
        if (sheets != 0)
            return sheets < 0;
        // Equal parts and sheets: "when it adds parts" keeps the incumbent.
        if (usage != CommonLineUsage.Whenever)
            return false;
        var material = Material(candidate, incumbent);
        var cut = Compare(CutLength(candidate), CutLength(incumbent));
        // Better on one measure and no worse on the other.
        return (material > 0 && cut <= 0) || (cut < 0 && material >= 0);
    }

    private static bool Keepable(NestPipelineResult result) => result.CanKeep && result.IsValid;

    private static int Placed(NestPipelineResult result) => result.Raw.Plates.Sum(p => p.Placements.Count);

    private static int Sheets(NestPipelineResult result) => result.Raw.Plates.Count;

    /// <summary>Positive when <paramref name="a"/> uses less material than <paramref name="b"/>:
    /// a lower net cost (a larger salvage-credited remnant), then the larger compactness offcut
    /// (a shorter used length).</summary>
    internal static int Material(NestPipelineResult a, NestPipelineResult b)
    {
        var cost = Compare(NestJobCost.Evaluate(a.Job, a.Raw), NestJobCost.Evaluate(b.Job, b.Raw));
        if (cost != 0)
            return -cost;
        return Compare(NestJobCost.CompactnessOffcut(a.Job, a.Raw), NestJobCost.CompactnessOffcut(b.Job, b.Raw));
    }

    /// <summary>
    /// Total cut length: every part's cut contours, less each shared span once (a common line is
    /// cut once). Repeated sheets count each copy.
    /// </summary>
    internal static double CutLength(NestPipelineResult result)
    {
        var lengths = new Dictionary<Drawing, double>(ReferenceEqualityComparer.Instance);
        var total = 0.0;
        foreach (var plate in result.Plates)
        {
            var length = plate.Parts.Sum(part => ContourLength(part.BaseDrawing, lengths));
            foreach (var group in plate.CommonLines)
                length -= CommonLineGeometry.Check(group).Spans.Sum(span => span.Length);
            total += plate.Quantity * length;
        }
        return total;
    }

    private static double ContourLength(Drawing drawing, Dictionary<Drawing, double> lengths)
    {
        if (!lengths.TryGetValue(drawing, out var length))
        {
            length = ConvertProgram.ToGeometry(drawing.Program).ForNesting().Sum(entity => entity.Length);
            lengths.Add(drawing, length);
        }
        return length;
    }

    private static int Compare(double a, double b) =>
        System.Math.Abs(a - b) <= Epsilon * System.Math.Max(1, System.Math.Max(System.Math.Abs(a), System.Math.Abs(b)))
            ? 0 : a.CompareTo(b);
}
