using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest;

public class StraightSplit : ISplitFeature
{
    public string Name => "Straight";

    public SplitFeatureResult GenerateFeatures(SplitLine line, double extentStart, double extentEnd, SplitParameters parameters)
    {
        var (negEdge, posEdge) = line.Axis == CutOffAxis.Vertical
            ? (new Line(new Vector(line.Position, extentStart), new Vector(line.Position, extentEnd)),
               new Line(new Vector(line.Position, extentEnd), new Vector(line.Position, extentStart)))
            : (new Line(new Vector(extentStart, line.Position), new Vector(extentEnd, line.Position)),
               new Line(new Vector(extentEnd, line.Position), new Vector(extentStart, line.Position)));

        return new SplitFeatureResult(
            new List<Entity> { negEdge },
            new List<Entity> { posEdge });
    }
}
