using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// Result of a nest optimization run.
    /// </summary>
    public class NestResult
    {
        /// <summary>
        /// The best sequence found: (drawingId, rotation, drawing) tuples in placement order.
        /// </summary>
        public List<(int drawingId, double rotation, Drawing drawing)> Sequence { get; set; }

        /// <summary>
        /// The score achieved by the best sequence.
        /// </summary>
        public FillScore Score { get; set; }

        /// <summary>
        /// Number of iterations performed.
        /// </summary>
        public int Iterations { get; set; }
    }

    /// <summary>
    /// Interface for nest optimization algorithms that search for the best
    /// part ordering and rotation to maximize plate utilization.
    /// </summary>
    public interface INestOptimizer
    {
        NestResult Optimize(List<NestItem> items, Box workArea, NfpCache cache,
            Dictionary<int, List<double>> candidateRotations,
            CancellationToken cancellation = default);
    }
}
