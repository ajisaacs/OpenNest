using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.RapidPlanning
{
    public interface IRapidPlanner
    {
        RapidPath Plan(Vector from, Vector to, IReadOnlyList<Shape> cutAreas);
    }
}
