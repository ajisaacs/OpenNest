using OpenNest.Math;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenNest.Geometry
{
    public static class ShapeBuilder
    {
        public static List<Shape> GetShapes(IEnumerable<Entity> entities)
        {
            var lines = new List<Line>();
            var arcs = new List<Arc>();
            var circles = new List<Circle>();
            var shapes = new List<Shape>();

            var entities2 = new Queue<Entity>(entities);

            while (entities2.Count > 0)
            {
                var entity = entities2.Dequeue();

                switch (entity.Type)
                {
                    case EntityType.Arc:
                        arcs.Add((Arc)entity);
                        break;

                    case EntityType.Circle:
                        circles.Add((Circle)entity);
                        break;

                    case EntityType.Line:
                        lines.Add((Line)entity);
                        break;

                    case EntityType.Shape:
                        var shape = (Shape)entity;
                        shape.Entities.ForEach(e => entities2.Enqueue(e));
                        break;

                    default:
                        Debug.Fail("Unhandled geometry type");
                        break;
                }
            }

            foreach (var circle in circles)
            {
                var shape = new Shape();
                shape.Entities.Add(circle);
                shape.UpdateBounds();
                shapes.Add(shape);
            }

            var entityList = new List<Entity>();

            entityList.AddRange(lines);
            entityList.AddRange(arcs);

            while (entityList.Count > 0)
            {
                var next = entityList[0];
                var shape = new Shape();
                shape.Entities.Add(next);

                entityList.RemoveAt(0);

                Vector startPoint = new Vector();
                Entity connected;

                switch (next.Type)
                {
                    case EntityType.Arc:
                        var arc = (Arc)next;
                        startPoint = arc.EndPoint();
                        break;

                    case EntityType.Line:
                        var line = (Line)next;
                        startPoint = line.EndPoint;
                        break;
                }

                while ((connected = GetConnected(startPoint, entityList)) != null)
                {
                    shape.Entities.Add(connected);
                    entityList.Remove(connected);

                    switch (connected.Type)
                    {
                        case EntityType.Arc:
                            var arc = (Arc)connected;
                            startPoint = arc.EndPoint();
                            break;

                        case EntityType.Line:
                            var line = (Line)connected;
                            startPoint = line.EndPoint;
                            break;
                    }
                }

                shape.UpdateBounds();
                shapes.Add(shape);
            }

            return shapes;
        }

        internal static Entity GetConnected(Vector pt, IEnumerable<Entity> geometry)
        {
            var tol = Tolerance.ChainTolerance;

            foreach (var geo in geometry)
            {
                switch (geo.Type)
                {
                    case EntityType.Arc:
                        var arc = (Arc)geo;

                        if (arc.StartPoint().DistanceTo(pt) <= tol)
                            return arc;

                        if (arc.EndPoint().DistanceTo(pt) <= tol)
                        {
                            arc.Reverse();
                            return arc;
                        }

                        break;

                    case EntityType.Line:
                        var line = (Line)geo;

                        if (line.StartPoint.DistanceTo(pt) <= tol)
                            return line;

                        if (line.EndPoint.DistanceTo(pt) <= tol)
                        {
                            line.Reverse();
                            return line;
                        }
                        break;
                }
            }

            return null;
        }
    }
}
