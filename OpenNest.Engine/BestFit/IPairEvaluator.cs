using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public interface IPairEvaluator
    {
        List<BestFitResult> EvaluateAll(List<PairCandidate> candidates);
    }
}
