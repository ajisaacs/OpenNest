using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Sequencing
{
    /// <summary>
    /// Sequences a plate along lines the user draws over it, in the order they were drawn.
    /// Each part belongs to the line passing closest to its center, and is cut in the order
    /// that nearest point falls along the line; where a line passes a part more than once,
    /// the closest pass decides. The jump from one line to the next is a rapid and claims
    /// nothing. Cut-first parts stay ahead of the rest and the cutoff rules still apply.
    /// </summary>
    public static class DrawnPathSequencing
    {
        /// <summary>Reorders the plate's parts; false when no line has length or nothing moves.</summary>
        public static bool Apply(Plate plate, IReadOnlyList<IReadOnlyList<Vector>> lines)
        {
            ArgumentNullException.ThrowIfNull(plate);
            ArgumentNullException.ThrowIfNull(lines);
            if (plate.Parts.Count < 2 || !lines.Any(HasLength))
                return false;

            var first = plate.Parts.Where(p => p.BaseDrawing.CutFirst);
            var rest = plate.Parts.Where(p => !p.BaseDrawing.CutFirst);
            var ordered = Order(first, lines).Concat(Order(rest, lines)).ToList();
            var cutOrder = PlateSequencing.OrderCutOffsFirst(ordered, plate);
            if (cutOrder.SequenceEqual(plate.Parts, ReferenceEqualityComparer.Instance))
                return false;

            plate.Parts.Reorder(cutOrder);
            return true;
        }

        /// <summary>The parts in line order; ties keep their current order.</summary>
        public static List<Part> Order(IEnumerable<Part> parts, IReadOnlyList<IReadOnlyList<Vector>> lines)
        {
            return parts
                .Select(part => (Part: part, Station: Nearest(part.BoundingBox.Center, lines)))
                .OrderBy(item => item.Station.Line)
                .ThenBy(item => item.Station.Along)
                .ThenBy(item => item.Station.Distance)
                .Select(item => item.Part)
                .ToList();
        }

        /// <summary>
        /// The distance along the path to the point nearest <paramref name="point"/>, and how
        /// far away that point is. The earliest of equally near points wins.
        /// </summary>
        public static (double Along, double Distance) Project(Vector point, IReadOnlyList<Vector> path)
        {
            if (path.Count == 0)
                return (0, double.PositiveInfinity);

            var best = (Along: 0.0, Distance: point.DistanceTo(path[0]));
            var travelled = 0.0;
            for (var i = 1; i < path.Count; i++)
            {
                var start = path[i - 1];
                var end = path[i];
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var lengthSquared = dx * dx + dy * dy;
                var length = System.Math.Sqrt(lengthSquared);
                if (length <= Tolerance.Epsilon)
                    continue;

                var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / lengthSquared;
                t = System.Math.Clamp(t, 0.0, 1.0);
                var nearest = new Vector(start.X + dx * t, start.Y + dy * t);
                var distance = point.DistanceTo(nearest);
                if (distance < best.Distance - Tolerance.Epsilon)
                    best = (travelled + length * t, distance);
                travelled += length;
            }
            return best;
        }

        /// <summary>Whether the line is long enough to sequence along.</summary>
        public static bool HasLength(IReadOnlyList<Vector> path)
        {
            var length = 0.0;
            for (var i = 1; i < path.Count; i++)
                length += path[i - 1].DistanceTo(path[i]);
            return length > Tolerance.Epsilon;
        }

        // The earliest of equally near lines wins.
        private static (int Line, double Along, double Distance) Nearest(Vector point, IReadOnlyList<IReadOnlyList<Vector>> lines)
        {
            var best = (Line: int.MaxValue, Along: 0.0, Distance: double.PositiveInfinity);
            for (var i = 0; i < lines.Count; i++)
            {
                if (!HasLength(lines[i]))
                    continue;
                var (along, distance) = Project(point, lines[i]);
                if (distance < best.Distance - Tolerance.Epsilon)
                    best = (i, along, distance);
            }
            return best;
        }
    }
}
