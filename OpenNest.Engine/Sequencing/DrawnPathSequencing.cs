using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Sequencing
{
    /// <summary>
    /// Sequences a plate along lines the user draws over it, in the order they were drawn.
    /// Each part belongs to the crossing line passing closest to its center, and is cut in the
    /// order that nearest point falls along the line; where a line passes a part more than once,
    /// the closest pass decides. A single point is a click, and crosses the parts whose boxes
    /// hold it. Parts no line crosses are cut after the rest, ordered the same way by their
    /// nearest line. The jump from one line to the next is a rapid and claims
    /// nothing. Cut-first parts stay ahead of the rest and the cutoff rules still apply.
    /// </summary>
    public static class DrawnPathSequencing
    {
        /// <summary>Reorders the plate's parts; false when no line can sequence or nothing moves.</summary>
        public static bool Apply(Plate plate, IReadOnlyList<IReadOnlyList<Vector>> lines)
        {
            var cutOrder = CutOrder(plate, lines);
            if (cutOrder == null || cutOrder.SequenceEqual(plate.Parts, ReferenceEqualityComparer.Instance))
                return false;

            plate.Parts.Reorder(cutOrder);
            return true;
        }

        /// <summary>
        /// The order <see cref="Apply"/> would give the plate, without changing it; null when
        /// no line can sequence or there is nothing to order.
        /// </summary>
        public static List<Part> CutOrder(Plate plate, IReadOnlyList<IReadOnlyList<Vector>> lines)
        {
            ArgumentNullException.ThrowIfNull(plate);
            ArgumentNullException.ThrowIfNull(lines);
            if (plate.Parts.Count < 2 || !lines.Any(CanSequence))
                return null;

            var first = plate.Parts.Where(p => p.BaseDrawing.CutFirst);
            var rest = plate.Parts.Where(p => !p.BaseDrawing.CutFirst);
            var ordered = Order(first, lines).Concat(Order(rest, lines)).ToList();
            return PlateSequencing.OrderCutOffsFirst(ordered, plate);
        }

        /// <summary>
        /// The parts in line order, then the parts no line crosses, in the order they would
        /// follow their nearest line; ties keep their current order.
        /// </summary>
        public static List<Part> Order(IEnumerable<Part> parts, IReadOnlyList<IReadOnlyList<Vector>> lines)
        {
            return parts
                .Select(part =>
                {
                    var box = part.BoundingBox;
                    var crossing = Nearest(box.Center, lines, line => Crosses(line, box));
                    var missed = crossing.Line == int.MaxValue;
                    return (Part: part, Missed: missed, Station: missed ? Nearest(box.Center, lines, _ => true) : crossing);
                })
                .OrderBy(item => item.Missed)
                .ThenBy(item => item.Station.Line)
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

        /// <summary>Whether a line crosses the part, so it is cut in that line's order rather than after the rest.</summary>
        public static bool Reaches(IReadOnlyList<IReadOnlyList<Vector>> lines, Part part)
        {
            var box = part.BoundingBox;
            return lines.Any(line => CanSequence(line) && Crosses(line, box));
        }

        /// <summary>Whether the path is a click or a line long enough to sequence along.</summary>
        public static bool CanSequence(IReadOnlyList<Vector> path) => path.Count == 1 || HasLength(path);

        /// <summary>Whether the line is long enough to sequence along.</summary>
        public static bool HasLength(IReadOnlyList<Vector> path)
        {
            var length = 0.0;
            for (var i = 1; i < path.Count; i++)
                length += path[i - 1].DistanceTo(path[i]);
            return length > Tolerance.Epsilon;
        }

        /// <summary>Whether any segment of the path touches the box, or a click lands in it.</summary>
        public static bool Crosses(IReadOnlyList<Vector> path, Box box)
        {
            if (path.Count == 1)
                return box.Contains(path[0]);
            for (var i = 1; i < path.Count; i++)
            {
                if (SegmentTouchesBox(path[i - 1], path[i], box))
                    return true;
            }
            return false;
        }

        // Liang-Barsky clip of the segment against the box, edges included.
        private static bool SegmentTouchesBox(Vector start, Vector end, Box box)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var enter = 0.0;
            var exit = 1.0;
            return Clip(-dx, start.X - box.Left, ref enter, ref exit)
                && Clip(dx, box.Right - start.X, ref enter, ref exit)
                && Clip(-dy, start.Y - box.Bottom, ref enter, ref exit)
                && Clip(dy, box.Top - start.Y, ref enter, ref exit);
        }

        private static bool Clip(double p, double q, ref double enter, ref double exit)
        {
            if (System.Math.Abs(p) <= Tolerance.Epsilon)
                return q >= -Tolerance.Epsilon;

            var t = q / p;
            if (p < 0)
                enter = System.Math.Max(enter, t);
            else
                exit = System.Math.Min(exit, t);
            return enter <= exit;
        }

        // The earliest of equally near lines wins; int.MaxValue when no line qualifies.
        private static (int Line, double Along, double Distance) Nearest(
            Vector point, IReadOnlyList<IReadOnlyList<Vector>> lines, Func<IReadOnlyList<Vector>, bool> qualifies)
        {
            var best = (Line: int.MaxValue, Along: 0.0, Distance: double.PositiveInfinity);
            for (var i = 0; i < lines.Count; i++)
            {
                if (!CanSequence(lines[i]) || !qualifies(lines[i]))
                    continue;
                var (along, distance) = Project(point, lines[i]);
                if (distance < best.Distance - Tolerance.Epsilon)
                    best = (i, along, distance);
            }
            return best;
        }
    }
}
