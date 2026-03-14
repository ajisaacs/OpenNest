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
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            foreach (var part in parts)
            {
                totalPartArea += part.BaseDrawing.Area;
                var bb = part.BoundingBox;

                if (bb.Left < minX) minX = bb.Left;
                if (bb.Bottom < minY) minY = bb.Bottom;
                if (bb.Right > maxX) maxX = bb.Right;
                if (bb.Top > maxY) maxY = bb.Top;
            }

            var bboxArea = (maxX - minX) * (maxY - minY);
            var density = bboxArea > 0 ? totalPartArea / bboxArea : 0;

            var usableRemnantArea = ComputeUsableRemnantArea(maxX, maxY, workArea);

            return new FillScore(parts.Count, usableRemnantArea, density);
        }

        private static double ComputeUsableRemnantArea(double maxRight, double maxTop, Box workArea)
        {
            var largest = 0.0;

            // Right strip
            if (maxRight < workArea.Right)
            {
                var width = workArea.Right - maxRight;
                var height = workArea.Length;

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
