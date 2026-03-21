using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public interface IDistanceComputer
    {
        double[] ComputeDistances(
            List<Line> stationaryLines,
            List<Line> movingTemplateLines,
            SlideOffset[] offsets);
    }
}
