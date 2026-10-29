using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// The machine's fixed clamp rail: identical rectangular clamps, each on one sheet edge
    /// (<see cref="ClampEdge"/>) with its center measured from the plate origin along that
    /// edge. Plates always start at the origin, so the same rail resolves onto every sheet
    /// size without re-scaling. A clamp whose footprint lies wholly beyond a sheet is skipped;
    /// one that still reaches onto it is kept whole: the zone is an avoidance area, so its
    /// full footprint applies even where it hangs past the sheet.
    /// </summary>
    public sealed class ClampRail : IEquatable<ClampRail>
    {
        /// <summary>
        /// Planner default minutes to remove one clamp and put it back after cutting (D9),
        /// used when a caller does not supply its own value.
        /// </summary>
        public const double DefaultRemovalMinutes = 5.0;

        /// <summary>A rail whose clamps all sit on the origin-side edge.</summary>
        public ClampRail(
            double width,
            double depth,
            IEnumerable<double> centers,
            double removalMinutes = DefaultRemovalMinutes,
            double? clearance = null
        )
            : this(
                width,
                depth,
                (centers ?? throw new ArgumentNullException(nameof(centers)))
                    .Select(c => new RailClamp(ClampEdge.OriginSide, c)),
                removalMinutes,
                clearance
            )
        { }

        public ClampRail(
            double width,
            double depth,
            IEnumerable<RailClamp> clamps,
            double removalMinutes = DefaultRemovalMinutes,
            double? clearance = null
        )
        {
            ArgumentNullException.ThrowIfNull(clamps);
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
            if (clearance is double c && (!double.IsFinite(c) || c < 0))
                throw new ArgumentOutOfRangeException(
                    nameof(clearance),
                    "Clamp clearance must be finite and zero or more."
                );

            var sorted = new SortedSet<RailClamp>();
            foreach (var clamp in clamps)
            {
                if (!double.IsFinite(clamp.Center) || clamp.Center < 0)
                    throw new ArgumentOutOfRangeException(
                        "centers",
                        "Clamp centers must be finite and measured from the origin (zero or more)."
                    );
                if (!ClampEdges.IsDefined(clamp.Edge))
                    throw new ArgumentOutOfRangeException(nameof(clamps), "Unknown clamp edge.");
                sorted.Add(clamp);
            }

            Width = width;
            Depth = depth;
            RemovalMinutes = removalMinutes;
            Clearance = clearance;
            Clamps = sorted.ToArray();
            Centers = Clamps.Where(c => c.Edge == ClampEdge.OriginSide).Select(c => c.Center).ToArray();
        }

        /// <summary>Clamp extent along its edge.</summary>
        public double Width { get; }

        /// <summary>Clamp intrusion into the sheet from its edge.</summary>
        public double Depth { get; }

        /// <summary>
        /// Minutes to remove one clamp and put it back after cutting (D9). Added to a sheet's
        /// cut-time estimate once per clamp <see cref="Plate.RemovedClamps"/> lists there.
        /// </summary>
        public double RemovalMinutes { get; }

        /// <summary>
        /// The gap parts keep from every clamp footprint, independent of part spacing; null
        /// keeps the sheet's bottom edge spacing (D6), the clearance before clamps had their
        /// own. A clamp drawn as a support block under the sheet can use close to zero, so the
        /// laser only stays off it. See <see cref="Plate.ClampClearance"/>.
        /// </summary>
        public double? Clearance { get; }

        /// <summary>
        /// Distinct clamps ordered by edge, then center. A clamp's position in this list
        /// gives its <c>clamp-N</c> id, so a rail with only origin-side clamps keeps the ids
        /// it had before clamps could sit on other edges.
        /// </summary>
        public IReadOnlyList<RailClamp> Clamps { get; }

        /// <summary>Centers of the origin-side clamps, ascending; see <see cref="Clamps"/> for all.</summary>
        public IReadOnlyList<double> Centers { get; }

        /// <summary>A copy with the same size, removal time and clearance and these clamps.</summary>
        public ClampRail WithClamps(IEnumerable<RailClamp> clamps) =>
            new(Width, Depth, clamps, RemovalMinutes, Clearance);

        /// <summary>The <c>clamp-N</c> id of the clamp at <paramref name="index"/> in <see cref="Clamps"/>.</summary>
        public static string IdOf(int index) => $"clamp-{index + 1}";

        /// <summary>
        /// The footprint <paramref name="clamp"/> would have on a sheet, in plate coordinates
        /// (lower-left corner origin), or null when it lies wholly beyond the sheet. The clamp
        /// need not belong to this rail; it takes this rail's width and depth.
        /// </summary>
        public Box Footprint(RailClamp clamp, Size size, int quadrant) =>
            Footprint(clamp, Width, Depth, size, quadrant);

        /// <summary>
        /// The footprint of a <paramref name="width"/> by <paramref name="depth"/> clamp at
        /// <paramref name="clamp"/>; see <see cref="Footprint(RailClamp, Size, int)"/>.
        /// </summary>
        public static Box Footprint(RailClamp clamp, double width, double depth, Size size, int quadrant)
        {
            var length = size.Length;
            var sheetWidth = size.Width;
            if (!(length > 0) || !(sheetWidth > 0) || !double.IsFinite(length) || !double.IsFinite(sheetWidth))
                return null;

            var extendsLeft = quadrant is 2 or 3;
            var extendsDown = quadrant is 3 or 4;
            var half = width / 2;
            // Distances from the origin along the edge.
            var near = clamp.Center - half;
            var far = clamp.Center + half;

            if (clamp.Edge.RunsAlongX())
            {
                if (near >= length)
                    return null; // wholly beyond this sheet
                // The origin-side edge is y = 0 of the sheet, or its top when it extends to -Y.
                var atTop = (clamp.Edge == ClampEdge.FarSide) != extendsDown;
                // Plate coordinates are measured from the lower-left corner; a sheet extending
                // to -X from the origin has its origin edge at x = length.
                var x = extendsLeft ? length - far : near;
                return new Box(x, atTop ? sheetWidth - depth : 0, width, depth);
            }

            if (near >= sheetWidth)
                return null;
            var atRight = (clamp.Edge == ClampEdge.FarEnd) != extendsLeft;
            var y = extendsDown ? sheetWidth - far : near;
            return new Box(atRight ? length - depth : 0, y, depth, width);
        }

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
            var zones = new List<ClampZone>(Clamps.Count);
            for (var i = 0; i < Clamps.Count; i++)
            {
                var box = Footprint(Clamps[i], size, quadrant);
                if (box != null)
                    zones.Add(new ClampZone(IdOf(i), box));
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
            && Nullable.Equals(Clearance, other.Clearance)
            && Clamps.SequenceEqual(other.Clamps);

        public override bool Equals(object obj) => Equals(obj as ClampRail);

        public override int GetHashCode()
        {
            var hash = HashCode.Combine(Width, Depth, RemovalMinutes, Clearance, Clamps.Count);
            foreach (var clamp in Clamps)
                hash = HashCode.Combine(hash, clamp);
            return hash;
        }

        public override string ToString() =>
            $"ClampRail {Width:R} x {Depth:R} at [{string.Join(", ", Clamps.Select(Describe))}]"
            + (Clearance is double clearance ? $" clearance {clearance:R}" : "");

        private static string Describe(RailClamp clamp) =>
            clamp.Edge == ClampEdge.OriginSide
                ? clamp.Center.ToString("R")
                : $"{clamp.Edge} {clamp.Center:R}";
    }
}
