using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest
{
    public readonly struct FillScore : System.IComparable<FillScore>
    {
        public int Count { get; }

        /// <summary>
        /// Total part area / bounding box area of all placed parts.
        /// </summary>
        public double Density { get; }

        public FillScore(int count, double density)
        {
            Count = count;
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

            return new FillScore(parts.Count, density);
        }

        /// <summary>
        /// Lexicographic comparison: count, then density.
        /// </summary>
        public int CompareTo(FillScore other)
        {
            var c = Count.CompareTo(other.Count);

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
