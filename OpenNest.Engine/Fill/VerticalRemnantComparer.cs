using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Ranks fill results to minimize X-extent (preserve right-side vertical remnant).
    /// Tiebreak chain: count > smallest X-extent > highest density.
    /// </summary>
    public class VerticalRemnantComparer : IFillComparer
    {
        public bool IsBetter(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate == null || candidate.Count == 0)
                return false;

            if (current == null || current.Count == 0)
                return true;

            if (candidate.Count != current.Count)
                return candidate.Count > current.Count;

            var candExtent = XExtent(candidate);
            var currExtent = XExtent(current);

            if (!candExtent.IsEqualTo(currExtent))
                return candExtent < currExtent;

            return FillScore.Compute(candidate, workArea).Density
                 > FillScore.Compute(current, workArea).Density;
        }

        private static double XExtent(List<Part> parts)
        {
            var minX = double.MaxValue;
            var maxX = double.MinValue;

            foreach (var part in parts)
            {
                var bb = part.BoundingBox;
                if (bb.Left < minX) minX = bb.Left;
                if (bb.Right > maxX) maxX = bb.Right;
            }

            return maxX - minX;
        }
    }
}
