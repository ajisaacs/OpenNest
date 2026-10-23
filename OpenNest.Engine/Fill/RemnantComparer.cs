using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Ranks fill results to keep one side of the work area free for a remnant.
    /// Tiebreak chain: count > smallest extent along the ranked axis > highest density.
    /// </summary>
    public abstract class RemnantComparer : IFillComparer
    {
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

            if (candidate.Count != current.Count)
                return candidate.Count > current.Count;

            var candExtent = Extent(candidate);
            var currExtent = Extent(current);

            if (!candExtent.IsEqualTo(currExtent))
                return candExtent < currExtent;

            return FillScore.Compute(candidate, workArea).Density
                > FillScore.Compute(current, workArea).Density;
        }

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
