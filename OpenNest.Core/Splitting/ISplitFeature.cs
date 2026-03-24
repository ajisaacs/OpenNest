using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest;

public class SplitFeatureResult
{
    public List<Entity> NegativeSideEdge { get; }
    public List<Entity> PositiveSideEdge { get; }

    public SplitFeatureResult(List<Entity> negativeSideEdge, List<Entity> positiveSideEdge)
    {
        NegativeSideEdge = negativeSideEdge;
        PositiveSideEdge = positiveSideEdge;
    }
}

public interface ISplitFeature
{
    string Name { get; }
    SplitFeatureResult GenerateFeatures(SplitLine line, double extentStart, double extentEnd, SplitParameters parameters);
}
