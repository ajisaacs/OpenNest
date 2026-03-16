using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.RapidPlanning
{
    public interface IRapidPlanner
    {
        RapidPath Plan(Vector from, Vector to, IReadOnlyList<Shape> cutAreas);
    }
}
