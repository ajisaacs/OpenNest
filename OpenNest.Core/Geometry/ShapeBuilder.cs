using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    public static class ShapeBuilder
    {
        public static List<Shape> GetShapes(
            IEnumerable<Entity> entities,
            double? weldTolerance = null
        )
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

            // Chain in list order: each shape starts at the first unused entity and takes the
            // first unused entity, in list order, with an end at its current end. An endpoint
            // grid finds those candidates without rescanning the whole list at every step.
            var index = new EndpointIndex(entityList);
            var used = new bool[entityList.Count];
            for (var first = 0; first < entityList.Count; first++)
            {
                if (used[first])
                    continue;

                var next = entityList[first];
                used[first] = true;
                var shape = new Shape();
                shape.Entities.Add(next);

                var startPoint = EndOf(next);
                Entity connected;

                while ((connected = index.TakeConnected(startPoint, entityList, used)) != null)
                {
                    shape.Entities.Add(connected);
                    startPoint = EndOf(connected);
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
            Entity entity,
            bool isStart,
            Vector point,
            double tolerance
        )
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

        private static Vector EndOf(Entity entity) => entity.Type switch
        {
            EntityType.Arc => ((Arc)entity).EndPoint(),
            EntityType.Line => ((Line)entity).EndPoint,
            _ => new Vector(),
        };

        // Matches GetConnected entity by entity: true and reversed when only its end matches.
        private static bool Connects(Entity entity, Vector pt, double tol)
        {
            switch (entity.Type)
            {
                case EntityType.Arc:
                    var arc = (Arc)entity;
                    if (arc.StartPoint().DistanceTo(pt) <= tol)
                        return true;
                    if (arc.EndPoint().DistanceTo(pt) <= tol)
                    {
                        arc.Reverse();
                        return true;
                    }
                    return false;

                case EntityType.Line:
                    var line = (Line)entity;
                    if (line.StartPoint.DistanceTo(pt) <= tol)
                        return true;
                    if (line.EndPoint.DistanceTo(pt) <= tol)
                    {
                        line.Reverse();
                        return true;
                    }
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Entity positions by the grid cells of their endpoints, cells twice the chain
        /// tolerance wide, so every endpoint within tolerance of a point lies in its cell or a
        /// neighbor, rounding included. Reversing an entity swaps its endpoints, so the index stays valid.
        /// Endpoints too far out or not finite to place are checked on every query.
        /// </summary>
        private sealed class EndpointIndex
        {
            private const double CellSize = 2 * Tolerance.ChainTolerance;
            private const double MaxCell = 1e15;
            private readonly Dictionary<(long, long), List<int>> cells = new();
            private readonly List<int> unplaced = new();
            private readonly List<int> candidates = new();

            public EndpointIndex(List<Entity> entities)
            {
                for (var i = 0; i < entities.Count; i++)
                {
                    var (start, end) = GetEndpoints(entities[i]);
                    if (!Place(i, start) | !Place(i, end))
                        unplaced.Add(i);
                }
            }

            private bool Place(int i, Vector point)
            {
                if (!TryCell(point, out var cell))
                    return false;
                if (!cells.TryGetValue(cell, out var list))
                    cells[cell] = list = new List<int>();
                if (list.Count == 0 || list[^1] != i)
                    list.Add(i);
                return true;
            }

            private static bool TryCell(Vector point, out (long, long) cell)
            {
                var x = System.Math.Floor(point.X / CellSize);
                var y = System.Math.Floor(point.Y / CellSize);
                cell = default;
                if (!(System.Math.Abs(x) < MaxCell) || !(System.Math.Abs(y) < MaxCell))
                    return false;
                cell = ((long)x, (long)y);
                return true;
            }

            /// <summary>The first unused entity, in list order, that connects to <paramref name="pt"/>; marked used.</summary>
            public Entity TakeConnected(Vector pt, List<Entity> entities, bool[] used)
            {
                candidates.Clear();
                if (TryCell(pt, out var cell))
                {
                    for (var dx = -1L; dx <= 1; dx++)
                    {
                        for (var dy = -1L; dy <= 1; dy++)
                        {
                            if (cells.TryGetValue((cell.Item1 + dx, cell.Item2 + dy), out var list))
                                candidates.AddRange(list);
                        }
                    }
                    candidates.AddRange(unplaced);
                }
                else
                {
                    // A point that cannot be placed is compared with every entity, as before.
                    for (var i = 0; i < entities.Count; i++)
                        candidates.Add(i);
                }

                candidates.Sort();
                var previous = -1;
                foreach (var i in candidates)
                {
                    if (i == previous || used[i])
                        continue;
                    previous = i;
                    if (Connects(entities[i], pt, Tolerance.ChainTolerance))
                    {
                        used[i] = true;
                        return entities[i];
                    }
                }
                return null;
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
