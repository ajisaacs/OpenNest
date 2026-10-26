using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public interface IPairEvaluator
    {
        /// <summary>
        /// Evaluates every candidate, returning results in candidate order: the finder's stable
        /// sort relies on it to break equal scores the same way on every run.
        /// </summary>
        List<BestFitResult> EvaluateAll(List<PairCandidate> candidates);
    }
}
