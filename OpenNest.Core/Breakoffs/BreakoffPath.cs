using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Breakoffs
{
    /// <summary>
    /// Distance-based operations on a chain of lines and arcs, each oriented in travel order
    /// (one's end is the next one's start). Splits and copies keep the source entity's style.
    /// </summary>
    internal static class BreakoffPath
    {
        public static Vector Start(Entity e) => e is Line l ? l.StartPoint : ((Arc)e).StartPoint();

        public static Vector End(Entity e) => e is Line l ? l.EndPoint : ((Arc)e).EndPoint();

        public static double Length(IReadOnlyList<Entity> path) => path.Sum(e => e.Length);

        public static Vector PointAt(IReadOnlyList<Entity> path, double distance) =>
            Locate(path, distance, out var entity, out var local) ? PointOn(entity, local) : End(path[^1]);

        public static Vector TangentAt(IReadOnlyList<Entity> path, double distance)
        {
            Locate(path, distance, out var entity, out var local);
            return TangentOn(entity, local);
        }

        /// <summary>The distance along the path of the path point closest to <paramref name="point"/>.</summary>
        public static double DistanceOf(IReadOnlyList<Entity> path, Vector point)
        {
            var best = double.MaxValue;
            var bestDistance = 0.0;
            var walked = 0.0;
            foreach (var e in path)
            {
                var closest = e.ClosestPointTo(point);
                var gap = closest.DistanceTo(point);
                if (gap < best)
                {
                    best = gap;
                    bestDistance = walked + LocalDistance(e, closest);
                }
                walked += e.Length;
            }
            return bestDistance;
        }

        /// <summary>Copies of the part of <paramref name="path"/> from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static List<Entity> Sub(IReadOnlyList<Entity> path, double from, double to)
        {
            var result = new List<Entity>();
            var walked = 0.0;
            foreach (var e in path)
            {
                var length = e.Length;
                var a = System.Math.Max(from - walked, 0);
                var b = System.Math.Min(to - walked, length);
                walked += length;
                if (b - a <= Tolerance.Epsilon)
                    continue;
                result.Add(Piece(e, a, b));
            }
            return result;
        }

        /// <summary>Copies of <paramref name="path"/> in reverse travel order.</summary>
        public static List<Entity> Reversed(IEnumerable<Entity> path)
        {
            var result = path.Select(Copy).ToList();
            result.Reverse();
            foreach (var e in result)
                e.Reverse();
            return result;
        }

        public static Entity Copy(Entity e)
        {
            var copy = e.Clone();
            copy.Id = e.Id;
            return copy;
        }

        /// <summary>Points along the path, arcs subdivided so no chord deviates more than <paramref name="tolerance"/>.</summary>
        public static List<Vector> ToPoints(IEnumerable<Entity> path, double tolerance)
        {
            var points = new List<Vector>();
            foreach (var e in path)
            {
                if (points.Count == 0)
                    points.Add(Start(e));
                if (e is Arc arc)
                    points.AddRange(arc.ToPoints(arc.SegmentsForTolerance(tolerance)).Skip(1));
                else
                    points.Add(End(e));
            }
            return points;
        }

        /// <summary>Signed area (counterclockwise positive) of a closed point loop.</summary>
        public static double SignedArea(IReadOnlyList<Vector> loop)
        {
            var sum = 0.0;
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }

        public static bool Contains(IReadOnlyList<Vector> loop, Vector point)
        {
            var inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
            {
                var a = loop[i];
                var b = loop[j];
                if ((a.Y > point.Y) != (b.Y > point.Y)
                    && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        private static bool Locate(IReadOnlyList<Entity> path, double distance, out Entity entity, out double local)
        {
            var walked = 0.0;
            foreach (var e in path)
            {
                var length = e.Length;
                if (distance <= walked + length)
                {
                    entity = e;
                    local = System.Math.Clamp(distance - walked, 0, length);
                    return true;
                }
                walked += length;
            }
            entity = path[^1];
            local = entity.Length;
            return false;
        }

        private static Vector PointOn(Entity e, double local)
        {
            if (e is Line line)
            {
                var t = line.Length <= 0 ? 0 : local / line.Length;
                return line.StartPoint + (line.EndPoint - line.StartPoint) * t;
            }
            var arc = (Arc)e;
            var angle = ArcAngleAt(arc, local);
            return new Vector(arc.Center.X + arc.Radius * System.Math.Cos(angle),
                arc.Center.Y + arc.Radius * System.Math.Sin(angle));
        }

        private static Vector TangentOn(Entity e, double local)
        {
            if (e is Line line)
            {
                var d = line.EndPoint - line.StartPoint;
                var length = line.Length;
                return length <= 0 ? new Vector(1, 0) : new Vector(d.X / length, d.Y / length);
            }
            var arc = (Arc)e;
            var angle = ArcAngleAt(arc, local);
            return arc.IsReversed
                ? new Vector(System.Math.Sin(angle), -System.Math.Cos(angle))
                : new Vector(-System.Math.Sin(angle), System.Math.Cos(angle));
        }

        private static double ArcAngleAt(Arc arc, double local) =>
            arc.StartAngle + (arc.IsReversed ? -1 : 1) * local / arc.Radius;

        private static double LocalDistance(Entity e, Vector point)
        {
            if (e is Line line)
                return line.StartPoint.DistanceTo(point);
            var arc = (Arc)e;
            var swept = Angle.NormalizeRad(arc.IsReversed
                ? arc.StartAngle - arc.Center.AngleTo(point)
                : arc.Center.AngleTo(point) - arc.StartAngle);
            var distance = swept * arc.Radius;
            // A point a hair before the start normalizes to a full turn; it belongs at an end.
            if (distance > arc.Length)
                return point.DistanceTo(arc.StartPoint()) <= point.DistanceTo(arc.EndPoint()) ? 0 : arc.Length;
            return distance;
        }

        private static Entity Piece(Entity e, double a, double b)
        {
            Entity piece;
            if (e is Line line)
            {
                piece = new Line(PointOn(line, a), PointOn(line, b));
            }
            else
            {
                var arc = (Arc)e;
                piece = new Arc(arc.Center, arc.Radius, Angle.NormalizeRad(ArcAngleAt(arc, a)),
                    Angle.NormalizeRad(ArcAngleAt(arc, b)), arc.IsReversed);
            }
            piece.Layer = e.Layer;
            piece.Color = e.Color;
            piece.LineTypeName = e.LineTypeName;
            return piece;
        }

        /// <summary>
        /// Orders <paramref name="edges"/> into one open chain, copied and oriented in travel
        /// order, or returns null when they do not form exactly one open chain.
        /// </summary>
        public static List<Entity> ChainOpen(IReadOnlyList<Entity> edges, double tolerance)
        {
            if (edges.Count == 0 || edges.Any(e => e is not (Line or Arc)))
                return null;

            int Degree(Vector p) => edges.Count(e => Start(e).DistanceTo(p) <= tolerance)
                + edges.Count(e => End(e).DistanceTo(p) <= tolerance);

            var first = edges.FirstOrDefault(e => Degree(Start(e)) == 1 || Degree(End(e)) == 1);
            if (first == null)
                return null;

            var remaining = edges.ToList();
            var chain = new List<Entity>();
            var current = Copy(first);
            if (Degree(Start(first)) != 1)
                current.Reverse();
            remaining.Remove(first);
            chain.Add(current);

            while (remaining.Count > 0)
            {
                var tip = End(chain[^1]);
                var next = remaining.FirstOrDefault(e => Start(e).DistanceTo(tip) <= tolerance
                    || End(e).DistanceTo(tip) <= tolerance);
                if (next == null)
                    return null;
                var copy = Copy(next);
                if (Start(next).DistanceTo(tip) > tolerance)
                    copy.Reverse();
                remaining.Remove(next);
                chain.Add(copy);
            }

            return Start(chain[0]).DistanceTo(End(chain[^1])) <= tolerance ? null : chain;
        }
    }
}
