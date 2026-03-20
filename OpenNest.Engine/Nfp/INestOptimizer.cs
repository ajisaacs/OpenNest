using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Nfp
{
    /// <summary>
    /// Result of a nest optimization run.
    /// </summary>
    public class OptimizationResult
    {
        /// <summary>
        /// The best placement sequence found.
        /// </summary>
        public List<SequenceEntry> Sequence { get; set; }

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
            IProgress<NestProgress> progress = null,
            CancellationToken cancellation = default);
    }
}
