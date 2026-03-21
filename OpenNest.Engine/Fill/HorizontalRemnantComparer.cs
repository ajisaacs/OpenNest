using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Ranks fill results to minimize Y-extent (preserve top-side horizontal remnant).
    /// Tiebreak chain: count > smallest Y-extent > highest density.
    /// </summary>
    public class HorizontalRemnantComparer : IFillComparer
    {
        public bool IsBetter(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate == null || candidate.Count == 0)
                return false;

            if (current == null || current.Count == 0)
                return true;

            if (candidate.Count != current.Count)
                return candidate.Count > current.Count;

            var candExtent = YExtent(candidate);
            var currExtent = YExtent(current);

            if (!candExtent.IsEqualTo(currExtent))
                return candExtent < currExtent;

            return FillScore.Compute(candidate, workArea).Density
                 > FillScore.Compute(current, workArea).Density;
        }

        private static double YExtent(List<Part> parts)
        {
            var minY = double.MaxValue;
            var maxY = double.MinValue;

            foreach (var part in parts)
            {
                var bb = part.BoundingBox;
                if (bb.Bottom < minY) minY = bb.Bottom;
                if (bb.Top > maxY) maxY = bb.Top;
            }

            return maxY - minY;
        }
    }
}
