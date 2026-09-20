using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    public interface IDistanceComputer
    {
        double[] ComputeDistances(
            List<Line> stationaryLines,
            List<Line> movingTemplateLines,
            SlideOffset[] offsets
        );

        double[] ComputeDistances(
            List<Entity> stationaryEntities,
            List<Entity> movingEntities,
            SlideOffset[] offsets
        );
    }
}
