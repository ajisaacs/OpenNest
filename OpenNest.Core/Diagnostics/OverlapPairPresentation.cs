using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>Read-only pair presentation; no geometry preparation or collision queries.</summary>
public static class OverlapPairPresentation
{
    /// <summary>True for part-vs-clamp findings carried on the report (negative clamp slot).</summary>
    public static bool IsClampFinding(PlateOverlapPair pair) => pair.PartBId < 0;

    public static string Label(PlateOverlapPair pair) =>
        IsClampFinding(pair) ? $"{pair.PartAId + 1}/clamp" : $"{pair.PartAId + 1}/{pair.PartBId + 1}";

    public static string Details(PlateOverlapPair pair, Units capturedUnits, IFormatProvider provider = null)
    {
        var units = UnitsHelper.GetShortString(capturedUnits);
        var head = IsClampFinding(pair)
            ? $"Clamp overlap: {pair.PartAName} / {pair.PartBName}"
            : $"Pair {Label(pair)}: {pair.PartAName} / {pair.PartBName}";
        return head + "\n"
            + $"Shared area ≈ {Number(pair.Area, provider)} {units}²\n"
            + $"Marker: ({Number(pair.Marker.X, provider)}, {Number(pair.Marker.Y, provider)}) {units}";
    }

    public static string Label(PlateSpacingPair pair, IFormatProvider provider = null) =>
        $"{pair.PartAId + 1}/{pair.PartBId + 1} gap {Number(pair.Gap, provider)}";

    public static string Details(PlateSpacingPair pair, Units capturedUnits, IFormatProvider provider = null)
    {
        var units = UnitsHelper.GetShortString(capturedUnits);
        return $"Part spacing {pair.PartAId + 1}/{pair.PartBId + 1}: {pair.PartAName} / {pair.PartBName}\n"
            + $"Gap ≈ {Number(pair.Gap, provider)} {units}\n"
            + $"Marker: ({Number(pair.Marker.X, provider)}, {Number(pair.Marker.Y, provider)}) {units}";
    }

    /// <summary>A short canvas label: the part's sequence number and what is wrong.</summary>
    public static string Label(PlateEdgeFinding finding) =>
        $"{finding.PartId + 1} " + (finding.OffPlate ? "off plate" : "outside work area");

    public static string Details(PlateEdgeFinding finding, Units capturedUnits, IFormatProvider provider = null)
    {
        var units = UnitsHelper.GetShortString(capturedUnits);
        return (finding.OffPlate ? "Off the plate" : "Outside work area") + $": part {finding.PartId + 1}\n"
            + finding.Message + "\n"
            + $"Marker: ({Number(finding.Marker.X, provider)}, {Number(finding.Marker.Y, provider)}) {units}";
    }

    // Significant figures adapt to scale: a positive sliver must never read as zero.
    public static string Number(double value, IFormatProvider provider = null) =>
        value.ToString("G6", provider ?? CultureInfo.CurrentCulture);

    public static double MarkerHalfSize(int deviceDpi) => 6.0 * deviceDpi / 96;
    public static double HitRadius(int deviceDpi) => 10.0 * deviceDpi / 96;

    /// <summary>
    /// Projects cached markers into a screen-pixel coordinate space. The pointer must
    /// use that same space; view zoom never scales the DPI-adjusted hit radius.
    /// Coincident or nearby markers return every matching pair in stable ID order.
    /// </summary>
    public static IReadOnlyList<PlateOverlapPair> HitTest(IEnumerable<PlateOverlapPair> pairs,
        Func<Vector, Vector> worldToScreen, Vector pointer, int deviceDpi) =>
        pairs.Where(pair => Hits(worldToScreen(pair.Marker), pointer, deviceDpi))
            .OrderBy(pair => pair.PartAId).ThenBy(pair => pair.PartBId).ToArray();

    /// <inheritdoc cref="HitTest(IEnumerable{PlateOverlapPair}, Func{Vector, Vector}, Vector, int)"/>
    public static IReadOnlyList<PlateSpacingPair> HitTest(IEnumerable<PlateSpacingPair> pairs,
        Func<Vector, Vector> worldToScreen, Vector pointer, int deviceDpi) =>
        pairs.Where(pair => Hits(worldToScreen(pair.Marker), pointer, deviceDpi))
            .OrderBy(pair => pair.PartAId).ThenBy(pair => pair.PartBId).ToArray();

    /// <inheritdoc cref="HitTest(IEnumerable{PlateOverlapPair}, Func{Vector, Vector}, Vector, int)"/>
    public static IReadOnlyList<PlateEdgeFinding> HitTest(IEnumerable<PlateEdgeFinding> findings,
        Func<Vector, Vector> worldToScreen, Vector pointer, int deviceDpi) =>
        findings.Where(finding => Hits(worldToScreen(finding.Marker), pointer, deviceDpi))
            .OrderBy(finding => finding.PartId).ToArray();

    private static bool Hits(Vector center, Vector pointer, int deviceDpi)
    {
        var radius = HitRadius(deviceDpi);
        var dx = pointer.X - center.X;
        var dy = pointer.Y - center.Y;
        return dx * dx + dy * dy <= radius * radius;
    }
}
