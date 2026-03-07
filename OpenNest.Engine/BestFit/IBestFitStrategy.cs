using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public interface IBestFitStrategy
    {
        int Type { get; }
        string Description { get; }
        List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize);
    }
}
