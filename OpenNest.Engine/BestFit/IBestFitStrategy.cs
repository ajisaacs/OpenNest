using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public interface IBestFitStrategy
    {
        int StrategyIndex { get; }
        string Description { get; }
        List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize);
    }
}
