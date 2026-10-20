using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Support
{
    /// <summary>
    /// A laser bed whose parts rest on a regular grid of contact points: one point every
    /// <see cref="PitchX"/> along X and every <see cref="PitchY"/> along Y, the first at
    /// (<see cref="OffsetX"/>, <see cref="OffsetY"/>) measured from the plate origin into the
    /// sheet. A sheet in another quadrant sees the same grid mirrored, since it lies on the
    /// other side of the origin. Lengths are in the nest's units.
    /// </summary>
    public sealed class SupportGrid : SupportBed
    {
        public SupportGrid(double pitchX, double pitchY, double offsetX = 0, double offsetY = 0,
            double registrationTolerance = 0, double stabilityMargin = 0)
            : base(registrationTolerance, stabilityMargin)
        {
            PitchX = Positive(pitchX, nameof(pitchX));
            PitchY = Positive(pitchY, nameof(pitchY));
            OffsetX = Finite(offsetX, nameof(offsetX));
            OffsetY = Finite(offsetY, nameof(offsetY));
        }

        public double PitchX { get; }
        public double PitchY { get; }
        public double OffsetX { get; }
        public double OffsetY { get; }

        public override IReadOnlyList<Vector> Contacts(SupportMaterial material, int quadrant, Vector shift,
            bool extremesOnly = false)
        {
            ArgumentNullException.ThrowIfNull(material);
            var contacts = new List<Vector>();
            if (!material.IsComplete)
                return contacts;

            // The grid's world lattice: OffsetX into the sheet from the origin, then every PitchX.
            var baseX = SignX(quadrant) * OffsetX + shift.X;
            var baseY = SignY(quadrant) * OffsetY + shift.Y;
            var bounds = material.Bounds;
            var firstRow = (long)System.Math.Ceiling((bounds.Bottom - baseY) / PitchY);
            var lastRow = (long)System.Math.Floor((bounds.Top - baseY) / PitchY);
            for (var row = firstRow; row <= lastRow; row++)
            {
                var y = baseY + row * PitchY;
                foreach (var (start, end) in material.SpansAtY(y))
                {
                    var first = (long)System.Math.Ceiling((start - baseX) / PitchX);
                    var last = (long)System.Math.Floor((end - baseX) / PitchX);
                    if (first > last)
                        continue;
                    // Within one span only its end points can be corners of the contacts' hull.
                    if (extremesOnly)
                    {
                        contacts.Add(new Vector(baseX + first * PitchX, y));
                        if (last != first)
                            contacts.Add(new Vector(baseX + last * PitchX, y));
                        continue;
                    }
                    for (var column = first; column <= last; column++)
                        contacts.Add(new Vector(baseX + column * PitchX, y));
                }
            }
            return contacts;
        }

        /// <summary>
        /// The grid points within <paramref name="region"/> (world coordinates) on a sheet in
        /// <paramref name="quadrant"/>, for display; empty when there would be more than
        /// <paramref name="limit"/>.
        /// </summary>
        public IReadOnlyList<Vector> PointsIn(Box region, int quadrant, int limit = 20000)
        {
            ArgumentNullException.ThrowIfNull(region);
            var baseX = SignX(quadrant) * OffsetX;
            var baseY = SignY(quadrant) * OffsetY;
            var firstColumn = System.Math.Ceiling((region.Left - baseX) / PitchX);
            var lastColumn = System.Math.Floor((region.Right - baseX) / PitchX);
            var firstRow = System.Math.Ceiling((region.Bottom - baseY) / PitchY);
            var lastRow = System.Math.Floor((region.Top - baseY) / PitchY);
            var count = (lastColumn - firstColumn + 1) * (lastRow - firstRow + 1);
            if (!double.IsFinite(count) || count <= 0 || count > limit)
                return Array.Empty<Vector>();

            var points = new List<Vector>((int)count);
            for (var row = firstRow; row <= lastRow; row++)
                for (var column = firstColumn; column <= lastColumn; column++)
                    points.Add(new Vector(baseX + column * PitchX, baseY + row * PitchY));
            return points;
        }

        public override SupportBed Scaled(double factor) =>
            new SupportGrid(PitchX * factor, PitchY * factor, OffsetX * factor, OffsetY * factor,
                RegistrationTolerance * factor, StabilityMargin * factor);

        public override bool Equals(SupportBed other) =>
            other is SupportGrid grid
            && grid.PitchX == PitchX && grid.PitchY == PitchY
            && grid.OffsetX == OffsetX && grid.OffsetY == OffsetY
            && grid.RegistrationTolerance == RegistrationTolerance
            && grid.StabilityMargin == StabilityMargin;

        public override int GetHashCode() =>
            HashCode.Combine(PitchX, PitchY, OffsetX, OffsetY, RegistrationTolerance, StabilityMargin);

        private static double Positive(double value, string name) =>
            double.IsFinite(value) && value > 0 ? value
                : throw new ArgumentOutOfRangeException(name, "Grid pitch must be finite and positive.");

        private static double Finite(double value, string name) =>
            double.IsFinite(value) ? value
                : throw new ArgumentOutOfRangeException(name, "Grid offset must be finite.");
    }
}
