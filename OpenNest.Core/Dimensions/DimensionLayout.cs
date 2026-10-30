using System.Globalization;
using OpenNest.Geometry;

namespace OpenNest.Dimensions
{
    /// <summary>
    /// A dimension resolved against a part's geometry. Extension lines run from
    /// <see cref="ExtensionStart"/> to <see cref="LineStart"/> and from <see cref="ExtensionEnd"/>
    /// to <see cref="LineEnd"/>; the dimension line joins the last two.
    /// </summary>
    public sealed class DimensionLayout
    {
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

        public string ValueText => Value.ToString("0.000", CultureInfo.InvariantCulture);

        public Vector MidPoint => new((LineStart.X + LineEnd.X) / 2, (LineStart.Y + LineEnd.Y) / 2);

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
