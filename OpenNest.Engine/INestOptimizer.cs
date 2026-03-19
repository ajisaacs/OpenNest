using System.Collections.Generic;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// Result of a nest optimization run.
    /// </summary>
    public class OptimizationResult
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
        OptimizationResult Optimize(List<NestItem> items, Box workArea, NfpCache cache,
            Dictionary<int, List<double>> candidateRotations,
            CancellationToken cancellation = default);
    }
}
