using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.RapidPlanning
{
    public class DirectRapidPlanner : IRapidPlanner
    {
        public RapidPath Plan(Vector from, Vector to, IReadOnlyList<Shape> cutAreas)
        {
            var travelLine = new Line(from, to);

            foreach (var cutArea in cutAreas)
            {
                if (TravelLineIntersectsShape(travelLine, cutArea))
                {
                    return new RapidPath
                    {
                        HeadUp = true,
                        Waypoints = new List<Vector>()
                    };
                }
            }

            return new RapidPath
            {
                HeadUp = false,
                Waypoints = new List<Vector>()
            };
        }

        private static bool TravelLineIntersectsShape(Line travelLine, Shape shape)
        {
            foreach (var entity in shape.Entities)
            {
                if (entity is Line edge)
                {
                    if (travelLine.Intersects(edge, out _))
                        return true;
                }
            }
            return false;
        }
    }
}
