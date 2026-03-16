using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.RapidPlanning
{
    public class SafeHeightRapidPlanner : IRapidPlanner
    {
        public RapidPath Plan(Vector from, Vector to, IReadOnlyList<Shape> cutAreas)
        {
            return new RapidPath
            {
                HeadUp = true,
                Waypoints = new List<Vector>()
            };
        }
    }
}
