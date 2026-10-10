using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingPlanning;

/// <summary>Closure of source contours during planner material capture and preparation.</summary>
internal static class NominalContourClosure
{
    internal const double Tolerance = 0.000001;

    internal static bool IsClosed(Vector start, Vector end)
    {
        // Subtracting translated/rotated coordinates can put an exact boundary gap
        // a few floating-point ulps above the limit. Bound that allowance to 1e-12.
        var scale = System.Math.Max(1, System.Math.Max(
            System.Math.Max(System.Math.Abs(start.X), System.Math.Abs(start.Y)),
            System.Math.Max(System.Math.Abs(end.X), System.Math.Abs(end.Y))));
        var roundoff = System.Math.Min(1e-12, 4 * 2.2204460492503131e-16 * scale);
        return start.DistanceTo(end) <= Tolerance + roundoff;
    }
}
