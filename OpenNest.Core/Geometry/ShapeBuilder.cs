using OpenNest.Math;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace OpenNest.Geometry
{
    public static class ShapeBuilder
    {
        public static List<Shape> GetShapes(IEnumerable<Entity> entities, double? weldTolerance = null)
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

            if (weldTolerance.HasValue)
                WeldEndpoints(entityList, weldTolerance.Value);

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

        public static void WeldEndpoints(List<Entity> entities, double tolerance)
        {
            var endpointGroups = new List<List<(Entity entity, bool isStart, Vector point)>>();

            foreach (var entity in entities)
            {
                var (start, end) = GetEndpoints(entity);
                if (!start.IsValid() || !end.IsValid())
                    continue;

                AddToGroup(endpointGroups, entity, true, start, tolerance);
                AddToGroup(endpointGroups, entity, false, end, tolerance);
            }

            foreach (var group in endpointGroups)
            {
                if (group.Count <= 1)
                    continue;

                var avgX = group.Average(g => g.point.X);
                var avgY = group.Average(g => g.point.Y);
                var weldedPoint = new Vector(avgX, avgY);

                foreach (var (entity, isStart, _) in group)
                    ApplyWeld(entity, isStart, weldedPoint);
            }
        }

        private static void AddToGroup(
            List<List<(Entity entity, bool isStart, Vector point)>> groups,
            Entity entity, bool isStart, Vector point, double tolerance)
        {
            foreach (var group in groups)
            {
                if (group[0].point.DistanceTo(point) <= tolerance)
                {
                    group.Add((entity, isStart, point));
                    return;
                }
            }

            groups.Add(new List<(Entity, bool, Vector)> { (entity, isStart, point) });
        }

        private static (Vector start, Vector end) GetEndpoints(Entity entity)
        {
            switch (entity.Type)
            {
                case EntityType.Arc:
                    var arc = (Arc)entity;
                    return (arc.StartPoint(), arc.EndPoint());

                case EntityType.Line:
                    var line = (Line)entity;
                    return (line.StartPoint, line.EndPoint);

                default:
                    return (Vector.Invalid, Vector.Invalid);
            }
        }

        private static void ApplyWeld(Entity entity, bool isStart, Vector weldedPoint)
        {
            switch (entity.Type)
            {
                case EntityType.Line:
                    var line = (Line)entity;
                    if (isStart)
                        line.StartPoint = weldedPoint;
                    else
                        line.EndPoint = weldedPoint;
                    break;

                case EntityType.Arc:
                    var arc = (Arc)entity;
                    var deltaX = weldedPoint.X - arc.Center.X;
                    var deltaY = weldedPoint.Y - arc.Center.Y;
                    var angle = System.Math.Atan2(deltaY, deltaX);

                    if (isStart)
                        arc.StartAngle = angle;
                    else
                        arc.EndAngle = angle;
                    break;
            }
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
