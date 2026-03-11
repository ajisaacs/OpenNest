using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public readonly struct FillScore : System.IComparable<FillScore>
    {
        /// <summary>
        /// Minimum short-side dimension for a remnant to be considered usable.
        /// </summary>
        public const double MinRemnantDimension = 12.0;

        public int Count { get; }

        /// <summary>
        /// Area of the largest remnant whose short side >= MinRemnantDimension.
        /// Zero if no usable remnant exists.
        /// </summary>
        public double UsableRemnantArea { get; }

        /// <summary>
        /// Total part area / bounding box area of all placed parts.
        /// </summary>
        public double Density { get; }

        public FillScore(int count, double usableRemnantArea, double density)
        {
            Count = count;
            UsableRemnantArea = usableRemnantArea;
            Density = density;
        }

        /// <summary>
        /// Computes a fill score from placed parts and the work area they were placed in.
        /// </summary>
        public static FillScore Compute(List<Part> parts, Box workArea)
        {
            if (parts == null || parts.Count == 0)
                return default;

            var totalPartArea = 0.0;

            foreach (var part in parts)
                totalPartArea += part.BaseDrawing.Area;

            var bbox = ((IEnumerable<IBoundable>)parts).GetBoundingBox();
            var bboxArea = bbox.Area();
            var density = bboxArea > 0 ? totalPartArea / bboxArea : 0;

            var usableRemnantArea = ComputeUsableRemnantArea(parts, workArea);

            return new FillScore(parts.Count, usableRemnantArea, density);
        }

        /// <summary>
        /// Finds the largest usable remnant (short side >= MinRemnantDimension)
        /// by checking right and top edge strips between placed parts and the work area boundary.
        /// </summary>
        private static double ComputeUsableRemnantArea(List<Part> parts, Box workArea)
        {
            var maxRight = double.MinValue;
            var maxTop = double.MinValue;

            foreach (var part in parts)
            {
                var bb = part.BoundingBox;

                if (bb.Right > maxRight)
                    maxRight = bb.Right;

                if (bb.Top > maxTop)
                    maxTop = bb.Top;
            }

            var largest = 0.0;

            // Right strip
            if (maxRight < workArea.Right)
            {
                var width = workArea.Right - maxRight;
                var height = workArea.Height;

                if (System.Math.Min(width, height) >= MinRemnantDimension)
                    largest = System.Math.Max(largest, width * height);
            }

            // Top strip
            if (maxTop < workArea.Top)
            {
                var width = workArea.Width;
                var height = workArea.Top - maxTop;

                if (System.Math.Min(width, height) >= MinRemnantDimension)
                    largest = System.Math.Max(largest, width * height);
            }

            return largest;
        }

        /// <summary>
        /// Lexicographic comparison: count, then usable remnant area, then density.
        /// </summary>
        public int CompareTo(FillScore other)
        {
            var c = Count.CompareTo(other.Count);

            if (c != 0)
                return c;

            c = UsableRemnantArea.CompareTo(other.UsableRemnantArea);

            if (c != 0)
                return c;

            return Density.CompareTo(other.Density);
        }

        public static bool operator >(FillScore a, FillScore b) => a.CompareTo(b) > 0;
        public static bool operator <(FillScore a, FillScore b) => a.CompareTo(b) < 0;
        public static bool operator >=(FillScore a, FillScore b) => a.CompareTo(b) >= 0;
        public static bool operator <=(FillScore a, FillScore b) => a.CompareTo(b) <= 0;
    }
}
