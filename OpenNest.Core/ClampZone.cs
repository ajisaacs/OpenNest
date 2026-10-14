using System;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// A fixed rectangular keep-out region in plate coordinates (the sheet's own frame:
    /// origin at the plate corner, X along the edge, Y depth from it). Machine edge clamps
    /// are the first users; a future "don't cut here" tag is another entry of this shape.
    /// Zones are exclusion and display only - never material, quantity, utilization,
    /// cut sequence or CNC output. Rectangles only: axis-aligned by construction.
    /// </summary>
    public sealed class ClampZone
    {
        public ClampZone(string id, Box bounds)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(bounds);
            if (
                !double.IsFinite(bounds.X)
                || !double.IsFinite(bounds.Y)
                || !double.IsFinite(bounds.Length)
                || !double.IsFinite(bounds.Width)
            )
                throw new ArgumentException(
                    $"Clamp zone '{id}' bounds must have finite coordinates and size.",
                    nameof(bounds)
                );
            if (bounds.Length <= 0 || bounds.Width <= 0)
                throw new ArgumentException(
                    $"Clamp zone '{id}' must have a positive length (X) and width (Y).",
                    nameof(bounds)
                );

            Id = id;
            // Detached copy: the zone owns its rectangle, callers may keep mutating theirs.
            Bounds = new Box(bounds.X, bounds.Y, bounds.Length, bounds.Width);
        }

        /// <summary>Stable identifier used by persistence and diagnostics.</summary>
        public string Id { get; }

        /// <summary>
        /// The rectangle in plate coordinates: Length is the extent along X, Width the
        /// extent along Y (depth from the y = 0 operator-side edge for machine clamps).
        /// </summary>
        public Box Bounds { get; }

        public override string ToString() =>
            $"ClampZone '{Id}' [{Bounds.Left:R}, {Bounds.Bottom:R}] x {Bounds.Length:R} x {Bounds.Width:R}";
    }
}
