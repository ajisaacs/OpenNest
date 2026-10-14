using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// The machine's fixed clamp rail: identical rectangular clamps whose X centers are
    /// measured from the plate origin along the origin-side (operator) edge. Plates always
    /// start at the origin, so the same rail resolves onto every sheet size without
    /// re-scaling. A clamp whose footprint lies wholly beyond a sheet is skipped; one that
    /// still reaches onto it is kept whole: the zone is an avoidance area, so its full
    /// footprint applies even where it hangs past the sheet.
    /// </summary>
    public sealed class ClampRail : IEquatable<ClampRail>
    {
        /// <summary>
        /// Planner default minutes to remove one clamp and put it back after cutting (D9),
        /// used when a caller does not supply its own value.
        /// </summary>
        public const double DefaultRemovalMinutes = 5.0;

        public ClampRail(
            double width,
            double depth,
            IEnumerable<double> centers,
            double removalMinutes = DefaultRemovalMinutes
        )
        {
            ArgumentNullException.ThrowIfNull(centers);
            if (!double.IsFinite(width) || width <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "Clamp width must be finite and positive."
                );
            if (!double.IsFinite(depth) || depth <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(depth),
                    "Clamp depth must be finite and positive."
                );
            if (!double.IsFinite(removalMinutes) || removalMinutes < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(removalMinutes),
                    "Clamp removal time must be finite and zero or more."
                );

            var sorted = new SortedSet<double>();
            foreach (var center in centers)
            {
                if (!double.IsFinite(center) || center < 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(centers),
                        "Clamp centers must be finite and measured from the origin (zero or more)."
                    );
                sorted.Add(center);
            }

            Width = width;
            Depth = depth;
            RemovalMinutes = removalMinutes;
            Centers = sorted.ToArray();
        }

        /// <summary>Clamp extent along the edge (X).</summary>
        public double Width { get; }

        /// <summary>Clamp intrusion into the sheet from the origin-side edge (Y).</summary>
        public double Depth { get; }

        /// <summary>
        /// Minutes to remove one clamp and put it back after cutting (D9). Added to a sheet's
        /// cut-time estimate once per clamp <see cref="Plate.RemovedClamps"/> lists there.
        /// </summary>
        public double RemovalMinutes { get; }

        /// <summary>Distinct clamp centers along X from the origin, ascending.</summary>
        public IReadOnlyList<double> Centers { get; }

        /// <summary>
        /// The zones this rail places on a sheet, in plate coordinates (the frame of
        /// <see cref="Plate.ClampZones"/>: origin at the sheet's lower-left corner). The
        /// quadrant locates the plate origin, and with it the origin-side edge, on the sheet.
        /// Zones are whole clamp footprints, never clipped, so one reaching past a sheet end
        /// (or deeper than a narrow sheet) extends beyond the sheet. Ids are <c>clamp-N</c> by
        /// rail position, so one physical clamp keeps its id on every sheet even when others
        /// are skipped.
        /// </summary>
        public IReadOnlyList<ClampZone> ResolveFor(Size size, int quadrant)
        {
            var length = size.Length;
            var sheetWidth = size.Width;
            if (!(length > 0) || !(sheetWidth > 0) || !double.IsFinite(length) || !double.IsFinite(sheetWidth))
                return Array.Empty<ClampZone>();

            var extendsLeft = quadrant is 2 or 3;
            var extendsDown = quadrant is 3 or 4;
            // The origin-side edge is y = 0 of the sheet, or its top when it extends to -Y.
            var y = extendsDown ? sheetWidth - Depth : 0;
            var half = Width / 2;

            var zones = new List<ClampZone>(Centers.Count);
            for (var i = 0; i < Centers.Count; i++)
            {
                // Distances from the origin along the edge.
                var near = Centers[i] - half;
                var far = Centers[i] + half;
                if (near >= length)
                    continue; // wholly beyond this sheet

                // Plate coordinates are measured from the lower-left corner; a sheet extending
                // to -X from the origin has its origin edge at x = length.
                var x = extendsLeft ? length - far : near;
                zones.Add(new ClampZone($"clamp-{i + 1}", new Box(x, y, Width, Depth)));
            }

            return zones;
        }

        /// <summary>True when the plate's zones are exactly (ids and bits) this rail's for its sheet.</summary>
        public bool Matches(Plate plate)
        {
            ArgumentNullException.ThrowIfNull(plate);
            return SameZones(plate.ClampZones, ResolveFor(plate.Size, plate.Quadrant));
        }

        /// <summary>
        /// True when the plate's zones are exactly this rail's resolution for its sheet minus
        /// the clamps named in <paramref name="removedClamps"/> (D8 last-resort removal, C13).
        /// A null or empty set is the same check as <see cref="Matches(Plate)"/>.
        /// </summary>
        public bool Matches(Plate plate, IReadOnlyCollection<string> removedClamps)
        {
            ArgumentNullException.ThrowIfNull(plate);
            if (removedClamps == null || removedClamps.Count == 0)
                return Matches(plate);
            var expected = ResolveFor(plate.Size, plate.Quadrant)
                .Where(z => !removedClamps.Contains(z.Id))
                .ToList();
            return SameZones(plate.ClampZones, expected);
        }

        internal static bool SameZones(IList<ClampZone> current, IReadOnlyList<ClampZone> expected)
        {
            if (current.Count != expected.Count)
                return false;
            for (var i = 0; i < current.Count; i++)
            {
                var a = current[i];
                var b = expected[i];
                if (
                    !string.Equals(a.Id, b.Id, StringComparison.Ordinal)
                    || a.Bounds.X != b.Bounds.X
                    || a.Bounds.Y != b.Bounds.Y
                    || a.Bounds.Length != b.Bounds.Length
                    || a.Bounds.Width != b.Bounds.Width
                )
                    return false;
            }
            return true;
        }

        public bool Equals(ClampRail other) =>
            other is not null
            && Width.Equals(other.Width)
            && Depth.Equals(other.Depth)
            && RemovalMinutes.Equals(other.RemovalMinutes)
            && Centers.SequenceEqual(other.Centers);

        public override bool Equals(object obj) => Equals(obj as ClampRail);

        public override int GetHashCode()
        {
            var hash = HashCode.Combine(Width, Depth, RemovalMinutes, Centers.Count);
            foreach (var center in Centers)
                hash = HashCode.Combine(hash, center);
            return hash;
        }

        public override string ToString() =>
            $"ClampRail {Width:R} x {Depth:R} at [{string.Join(", ", Centers.Select(c => c.ToString("R")))}]";
    }
}
