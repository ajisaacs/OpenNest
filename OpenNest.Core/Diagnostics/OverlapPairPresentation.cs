using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>Read-only pair presentation; no geometry preparation or collision queries.</summary>
public static class OverlapPairPresentation
{
    public static string Label(PlateOverlapPair pair) => $"{pair.PartAId + 1}/{pair.PartBId + 1}";

    public static string Details(PlateOverlapPair pair, Units capturedUnits, IFormatProvider provider = null)
    {
        var units = UnitsHelper.GetShortString(capturedUnits);
        return $"Pair {Label(pair)}: {pair.PartAName} / {pair.PartBName}\n"
            + $"Shared area ≈ {Number(pair.Area, provider)} {units}²\n"
            + $"Centroid: ({Number(pair.Centroid.X, provider)}, {Number(pair.Centroid.Y, provider)}) {units}";
    }

    // Significant figures adapt to scale: a positive sliver must never read as zero.
    public static string Number(double value, IFormatProvider provider = null) =>
        value.ToString("G6", provider ?? CultureInfo.CurrentCulture);

    public static double MarkerHalfSize(int deviceDpi) => 6.0 * deviceDpi / 96;
    public static double HitRadius(int deviceDpi) => 10.0 * deviceDpi / 96;

    /// <summary>
    /// Projects cached centroids into a screen-pixel coordinate space. The pointer must
    /// use that same space; view zoom never scales the DPI-adjusted hit radius.
    /// Coincident or nearby markers return every matching pair in stable ID order.
    /// </summary>
    public static IReadOnlyList<PlateOverlapPair> HitTest(IEnumerable<PlateOverlapPair> pairs,
        Func<Vector, Vector> worldToScreen, Vector pointer, int deviceDpi)
    {
        var radius = HitRadius(deviceDpi);
        return pairs.Where(pair =>
        {
            var center = worldToScreen(pair.Centroid);
            var dx = pointer.X - center.X;
            var dy = pointer.Y - center.Y;
            return dx * dx + dy * dy <= radius * radius;
        }).OrderBy(pair => pair.PartAId).ThenBy(pair => pair.PartBId).ToArray();
    }
}
