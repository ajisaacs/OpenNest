using OpenNest.Geometry;

namespace OpenNest.Engine.Jobs.Cutouts;

/// <summary>The coordinate range the cutout proposals accept.</summary>
internal static class CutoutCoordinates
{
    /// <summary>Largest coordinate magnitude the proposals process.</summary>
    internal const double MaxCoordinate = 1e8;

    /// <summary>Whether every side of <paramref name="box"/> is finite and within range.</summary>
    internal static bool WithinRange(Box box) =>
        double.IsFinite(box.Left) && double.IsFinite(box.Right)
        && double.IsFinite(box.Bottom) && double.IsFinite(box.Top)
        && System.Math.Abs(box.Left) <= MaxCoordinate
        && System.Math.Abs(box.Right) <= MaxCoordinate
        && System.Math.Abs(box.Bottom) <= MaxCoordinate
        && System.Math.Abs(box.Top) <= MaxCoordinate;
}
