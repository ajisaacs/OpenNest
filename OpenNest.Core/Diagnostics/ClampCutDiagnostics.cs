using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>A nominal manual cut-off line meeting an active, physical clamp footprint.</summary>
public sealed record CutOffClampFinding(int CutOffIndex, string ClampId, Vector Location);

/// <summary>
/// Read-only axis-aligned clamp contact checks. Nominal cut-off definitions are checked
/// even if their current regenerated program is stale or trimmed by nearby parts.
/// A removed clamp is absent from Plate.ClampZones and therefore cannot be reported.
/// Zones are compared in the plate's signed placement frame (<see cref="ClampZoneFrame"/>).
/// </summary>
public static class ClampCutDiagnostics
{
    public static IReadOnlyList<CutOffClampFinding> Find(Plate plate)
    {
        ArgumentNullException.ThrowIfNull(plate);
        var findings = new List<CutOffClampFinding>();
        if (plate.ClampZones.Count == 0 || plate.CutOffs.Count == 0)
            return findings.AsReadOnly();
        var zones = ClampZoneFrame.ToPlacement(plate);
        var sheet = plate.BoundingBox(false);
        for (var index = 0; index < plate.CutOffs.Count; index++)
        {
            var cut = plate.CutOffs[index];
            if (cut == null || !Enum.IsDefined(cut.Axis))
                throw new ArgumentException("Cut-off has an invalid axis.", nameof(plate));
            // A head-locked overcut runs on past the far edge, where an off-sheet clamp can be.
            var overcut = cut.HeadLockOvercut > 0 && double.IsFinite(cut.HeadLockOvercut) ? cut.HeadLockOvercut : 0;
            var farHigh = CutOff.FarEdgeIsHigh(cut.Axis, plate.Quadrant);
            var low = farHigh ? 0 : overcut;
            var high = farHigh ? overcut : 0;
            var start = cut.Axis == CutOffAxis.Vertical
                ? new Vector(cut.Position.X, cut.StartLimit ?? sheet.Bottom - low)
                : new Vector(cut.StartLimit ?? sheet.Left - low, cut.Position.Y);
            var end = cut.Axis == CutOffAxis.Vertical
                ? new Vector(cut.Position.X, cut.EndLimit ?? sheet.Top + high)
                : new Vector(cut.EndLimit ?? sheet.Right + high, cut.Position.Y);
            foreach (var zone in zones)
                if (Intersects(start, end, zone.Bounds, out var location))
                    findings.Add(new CutOffClampFinding(index, zone.Id, location));
        }
        return findings.AsReadOnly();
    }

    /// <summary>True for contact with the whole rectangle, including a boundary touch.</summary>
    public static bool Intersects(Vector from, Vector to, Box box, out Vector location)
    {
        location = default;
        if (box == null || !Finite(from) || !Finite(to) || !Finite(box.Location)
            || !double.IsFinite(box.Right) || !double.IsFinite(box.Top)
            || box.Length <= 0 || box.Width <= 0)
            throw new ArgumentException("Clamp contact geometry must be finite and nonempty.");
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)
            || !double.IsFinite(from.X - box.Left) || !double.IsFinite(box.Right - from.X)
            || !double.IsFinite(from.Y - box.Bottom) || !double.IsFinite(box.Top - from.Y))
            throw new ArgumentException("Clamp contact motion is too large.");
        var enter = 0.0;
        var exit = 1.0;
        if (!Clip(-dx, from.X - box.Left, ref enter, ref exit)
            || !Clip(dx, box.Right - from.X, ref enter, ref exit)
            || !Clip(-dy, from.Y - box.Bottom, ref enter, ref exit)
            || !Clip(dy, box.Top - from.Y, ref enter, ref exit))
            return false;
        var midpoint = (enter + exit) * 0.5;
        location = new Vector(from.X + dx * midpoint, from.Y + dy * midpoint);
        return true;
    }

    private static bool Clip(double p, double q, ref double enter, ref double exit)
    {
        if (p == 0)
            return q >= 0;
        var ratio = q / p;
        if (p < 0)
            enter = System.Math.Max(enter, ratio);
        else
            exit = System.Math.Min(exit, ratio);
        return enter <= exit;
    }

    private static bool Finite(Vector value) => double.IsFinite(value.X) && double.IsFinite(value.Y);
}
