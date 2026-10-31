using System;
using System.Collections.Generic;
using System.Globalization;
using OpenNest.Geometry;

namespace OpenNest.Dimensions
{
    /// <summary>
    /// A dimension resolved against a part's geometry. Extension lines run along the extents, on
    /// the lines through <see cref="ExtensionStart"/> and <see cref="LineStart"/> and through
    /// <see cref="ExtensionEnd"/> and <see cref="LineEnd"/>, out from the part to
    /// <see cref="ExtensionTip"/>; the dimension line joins the last two.
    /// </summary>
    public sealed class DimensionLayout
    {
        private const int MaxExtensionSteps = 64;
        private const double ExtensionPrecision = 0.05;

        public DimensionKind Kind { get; init; }

        /// <summary>Why the dimension could not be resolved; null when it was.</summary>
        public string Error { get; init; }

        public bool IsValid => Error == null;

        public double Value { get; init; }
        public Vector ExtensionStart { get; init; }
        public Vector ExtensionEnd { get; init; }
        public Vector LineStart { get; init; }
        public Vector LineEnd { get; init; }

        /// <summary>The part's extents the dimension was measured from.</summary>
        public Box Extents { get; init; }

        /// <summary>The geometry behind <see cref="Extents"/>.</summary>
        public IReadOnlyList<Entity> Profile { get; init; } = Array.Empty<Entity>();

        public string ValueText => Value.ToString("0.000", CultureInfo.InvariantCulture);

        public Vector MidPoint => new((LineStart.X + LineEnd.X) / 2, (LineStart.Y + LineEnd.Y) / 2);

        /// <summary>
        /// Where an extension line leaving <paramref name="foot"/> on the dimension line first
        /// meets the profile offset by <paramref name="clearance"/>, walking toward the part by the
        /// distance known to be free at each step. <paramref name="foot"/> itself when it is
        /// already that close.
        /// </summary>
        public Vector ExtensionTip(Vector foot, double clearance)
        {
            var horizontal = Kind == DimensionKind.OverallHorizontal;
            var position = horizontal ? foot.Y : foot.X;
            var center = horizontal
                ? (Extents.Bottom + Extents.Top) / 2
                : (Extents.Left + Extents.Right) / 2;
            var sign = System.Math.Sign(center - position);
            var direction = horizontal ? new Vector(0, sign) : new Vector(sign, 0);

            // The walk ends at the far side of the part at the latest: the line touches the part.
            var limit = System.Math.Abs(center - position) + (horizontal ? Extents.Width : Extents.Length) / 2;
            var travelled = 0.0;
            for (var i = 0; i < MaxExtensionSteps && travelled < limit; i++)
            {
                var free = DistanceToProfile(foot + direction * travelled) - clearance;
                if (free <= clearance * ExtensionPrecision)
                    break;
                travelled += free;
            }
            return foot + direction * System.Math.Min(travelled, limit);
        }

        private double DistanceToProfile(Vector point)
        {
            var nearest = double.MaxValue;
            foreach (var entity in Profile)
                nearest = System.Math.Min(nearest, point.DistanceTo(entity.ClosestPointTo(point)));
            return nearest;
        }

        /// <summary>
        /// The offset that puts the dimension line through <paramref name="point"/>, on the side of
        /// the part's center the point is on and at least <paramref name="minimum"/> clear of it.
        /// </summary>
        public double OffsetAt(Vector point, double minimum)
        {
            var horizontal = Kind == DimensionKind.OverallHorizontal;
            var position = horizontal ? point.Y : point.X;
            var low = horizontal ? Extents.Bottom : Extents.Left;
            var high = horizontal ? Extents.Top : Extents.Right;

            return position >= (low + high) / 2
                ? System.Math.Max(minimum, position - high)
                : System.Math.Min(-minimum, position - low);
        }
    }
}
