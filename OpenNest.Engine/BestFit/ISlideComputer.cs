using System;

namespace OpenNest.Engine.BestFit
{
    /// <summary>
    /// Batches directional-distance computations for multiple offset positions.
    /// GPU implementations can process all offsets in a single kernel launch.
    /// </summary>
    public interface ISlideComputer : IDisposable
    {
        /// <summary>
        /// Computes the first blocking contact distance for each offset position.
        /// Separating/tangential contacts on closed boundaries do not block.
        /// </summary>
        /// <param name="stationarySegments">Flat array [x1,y1,x2,y2, ...] for stationary edges.</param>
        /// <param name="stationaryCount">Number of line segments in stationarySegments.</param>
        /// <param name="movingTemplateSegments">Flat array [x1,y1,x2,y2, ...] for moving edges at origin.</param>
        /// <param name="movingCount">Number of line segments in movingTemplateSegments.</param>
        /// <param name="offsets">Flat array [dx,dy, dx,dy, ...] of translation offsets.</param>
        /// <param name="offsetCount">Number of offset positions.</param>
        /// <param name="direction">Push direction.</param>
        /// <returns>Array of minimum distances, one per offset position.</returns>
        double[] ComputeBatch(
            double[] stationarySegments,
            int stationaryCount,
            double[] movingTemplateSegments,
            int movingCount,
            double[] offsets,
            int offsetCount,
            PushDirection direction
        );

        /// <summary>
        /// Computes first blocking contact distances with per-offset directions.
        /// Uploads segment data once for all offsets, reducing GPU round-trips.
        /// </summary>
        double[] ComputeBatchMultiDir(
            double[] stationarySegments,
            int stationaryCount,
            double[] movingTemplateSegments,
            int movingCount,
            double[] offsets,
            int offsetCount,
            int[] directions
        );
    }
}
