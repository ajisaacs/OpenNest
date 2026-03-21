using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Ranks fill results by count first, then density.
    /// This is the original scoring logic used by DefaultNestEngine.
    /// </summary>
    public class DefaultFillComparer : IFillComparer
    {
        public bool IsBetter(List<Part> candidate, List<Part> current, Box workArea)
        {
            if (candidate == null || candidate.Count == 0)
                return false;

            if (current == null || current.Count == 0)
                return true;

            return FillScore.Compute(candidate, workArea) > FillScore.Compute(current, workArea);
        }
    }
}
