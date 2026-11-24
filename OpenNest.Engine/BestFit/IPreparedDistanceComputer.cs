using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    /// <summary>
    /// A distance computer that can prepare one pair of boundaries once for several batches
    /// of slides, as the pair search does: the grid of first slides, then the settling slides
    /// of the same two boundaries.
    /// </summary>
    public interface IPreparedDistanceComputer : IDistanceComputer
    {
        /// <summary>
        /// Prepares the boundaries. The lists must not change while the result is in use.
        /// </summary>
        IPreparedDistances Prepare(List<Entity> stationaryEntities, List<Entity> movingEntities);
    }

    /// <summary>One pair of boundaries prepared by an <see cref="IPreparedDistanceComputer"/>.</summary>
    public interface IPreparedDistances
    {
        /// <summary>
        /// What <see cref="IDistanceComputer.ComputeDistances(List{Entity}, List{Entity}, SlideOffset[])"/>
        /// returns for the prepared boundaries and these offsets. One batch at a time.
        /// </summary>
        double[] ComputeDistances(SlideOffset[] offsets);
    }
}
