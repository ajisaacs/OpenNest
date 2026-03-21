using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine
{
    /// <summary>
    /// Determines whether a candidate fill result is better than the current best.
    /// Implementations must be stateless and thread-safe.
    /// </summary>
    public interface IFillComparer
    {
        bool IsBetter(List<Part> candidate, List<Part> current, Box workArea);
    }
}
