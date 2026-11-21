using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Ranks fill results to keep one side of the work area free for a remnant.
    /// Tiebreak chain: count > smallest extent along the ranked axis > highest density.
    /// With a <see cref="Quantity"/>, layouts holding at least that many parts tie on count and
    /// are ranked by their extent as they will be kept (trimmed to the quantity along
    /// <see cref="TrimAxis"/>), then by their untrimmed count and density.
    /// </summary>
    public abstract class RemnantComparer : IFillComparer
    {
        /// <summary>
        /// The quantity the fill will be trimmed to; 0 ranks every layout as it stands.
        /// </summary>
        public int Quantity { get; init; }

        /// <summary>
        /// Whether the trim to <see cref="Quantity"/> keeps interlocked pairs whole; false for
        /// round parts, which never interlock.
        /// </summary>
        public bool KeepsPairs { get; init; } = true;

        /// <summary>The axis the filler trims surplus parts from (the ranked axis).</summary>
        protected abstract ShrinkAxis TrimAxis { get; }

        /// <summary>The lower edge of a bounding box along the ranked axis.</summary>
        protected abstract double Low(Box box);

        /// <summary>The upper edge of a bounding box along the ranked axis.</summary>
        protected abstract double High(Box box);

        public bool IsBetter(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate == null || candidate.Count == 0)
                return false;

            if (current == null || current.Count == 0)
                return true;

            var candKept = Kept(candidate);
            var currKept = Kept(current);

            if (candKept.Count != currKept.Count)
                return candKept.Count > currKept.Count;

            var candExtent = Extent(candKept);
            var currExtent = Extent(currKept);

            if (!candExtent.IsEqualTo(currExtent))
                return candExtent < currExtent;

            // Kept layouts tying on extent fall back to the untrimmed ranking: the pattern
            // holding more parts packs tighter.
            if (candidate.Count != current.Count)
                return candidate.Count > current.Count;

            return FillScore.Compute(candidate, workArea).Density
                > FillScore.Compute(current, workArea).Density;
        }

        private List<Part> Kept(List<Part> parts) =>
            Quantity > 0 && parts.Count > Quantity
                ? ShrinkFiller.TrimToCount(parts, Quantity, TrimAxis, KeepsPairs)
                : parts;

        private double Extent(List<Part> parts)
        {
            var min = double.MaxValue;
            var max = double.MinValue;

            foreach (var part in parts)
            {
                var bb = part.BoundingBox;
                var low = Low(bb);
                var high = High(bb);
                if (low < min)
                    min = low;
                if (high > max)
                    max = high;
            }

            return max - min;
        }
    }
}
