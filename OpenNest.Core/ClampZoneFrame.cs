using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest;

/// <summary>
/// Maps a live plate's <see cref="Plate.ClampZones"/>, stored in the sheet's lower-left frame
/// (<see cref="ClampRail.ResolveFor"/>), into the signed placement frame of its parts,
/// cut-offs and motions. The translation matches the sheet itself and the engine stock:
/// -Length on X in quadrants 2 and 3, -Width on Y in quadrants 3 and 4. Whole footprints are
/// kept, never clipped. Engine-internal plates built from a <c>NestPlateStock</c> already
/// carry placement-frame zones and must not be mapped again.
/// </summary>
public static class ClampZoneFrame
{
    /// <summary>
    /// Detached placement-frame copies in the plate's zone order; empty when there are none.
    /// Throws <see cref="ArgumentException"/> for a missing or malformed zone: zone bounds stay
    /// publicly mutable, so construction-time validation is not proof of current geometry.
    /// </summary>
    public static IReadOnlyList<ClampZone> ToPlacement(Plate plate)
    {
        ArgumentNullException.ThrowIfNull(plate);
        var zones = plate.ClampZones;
        if (zones == null || zones.Count == 0)
            return Array.Empty<ClampZone>();
        if (plate.Quadrant is < 1 or > 4)
            throw new ArgumentException("Quadrant must be 1 through 4.", nameof(plate));
        var left = plate.Quadrant is 2 or 3 ? -plate.Size.Length : 0;
        var bottom = plate.Quadrant is 3 or 4 ? -plate.Size.Width : 0;
        if (!double.IsFinite(left) || !double.IsFinite(bottom))
            throw new ArgumentException("Sheet size must be finite.", nameof(plate));

        var mapped = new ClampZone[zones.Count];
        for (var i = 0; i < mapped.Length; i++)
        {
            var zone = zones[i];
            if (zone?.Bounds == null)
                throw new ArgumentException($"Clamp zone {i + 1} is missing.", nameof(plate));
            var box = zone.Bounds;
            var x = box.X + left;
            var y = box.Y + bottom;
            if (!(box.Length > 0) || !(box.Width > 0) || !double.IsFinite(box.Length)
                || !double.IsFinite(box.Width) || !double.IsFinite(box.X) || !double.IsFinite(box.Y)
                || !double.IsFinite(x) || !double.IsFinite(y)
                || !double.IsFinite(x + box.Length) || !double.IsFinite(y + box.Width))
                throw new ArgumentException(
                    $"Clamp zone '{zone.Id}' must have finite bounds and a positive size.", nameof(plate));
            mapped[i] = new ClampZone(zone.Id, new Box(x, y, box.Length, box.Width));
        }
        return mapped;
    }
}
