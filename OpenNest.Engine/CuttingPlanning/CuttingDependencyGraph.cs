using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.CuttingPlanning;

/// <summary>Whole-part cutting prerequisites shared by automatic sequencing and cutting planning.</summary>
internal static class CuttingDependencyGraph
{
    /// <summary>
    /// Whether a cutoff's nominal line crosses a part's bounds within the cutoff's span. Uses the
    /// nominal line, not its trimmed cutting segments (which deliberately skip the parts). Bounds
    /// conservatively include edge contacts and concave recesses; limits prevent unrelated
    /// dependencies beyond the cutoff's span. Negative coordinates need no special case.
    /// </summary>
    internal static bool CutOffCrosses(CutOffAxis axis, Vector cutOffPosition, double? startLimit,
        double? endLimit, Box part, Box plate)
    {
        var vertical = axis == CutOffAxis.Vertical;
        var position = vertical ? cutOffPosition.X : cutOffPosition.Y;
        var acrossMin = vertical ? part.Left : part.Bottom;
        var acrossMax = vertical ? part.Right : part.Top;
        var alongMin = vertical ? part.Bottom : part.Left;
        var alongMax = vertical ? part.Top : part.Right;
        var start = startLimit ?? (vertical ? plate.Bottom : plate.Left);
        var end = endLimit ?? (vertical ? plate.Top : plate.Right);
        return !(position < acrossMin - Tolerance.Epsilon
            || position > acrossMax + Tolerance.Epsilon
            || System.Math.Max(start, end) < alongMin - Tolerance.Epsilon
            || System.Math.Min(start, end) > alongMax + Tolerance.Epsilon);
    }
}
