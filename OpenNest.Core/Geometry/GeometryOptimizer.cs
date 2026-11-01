using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    public static class GeometryOptimizer
    {
        public static void Optimize(IList<Arc> arcs) =>
            MergePass(
                arcs,
                (a, b) => a.IsCoradialTo(b),
                (Arc a, Arc b, out Arc joined) => TryJoinArcs(a, b, out joined)
            );

        public static void Optimize(IList<Line> lines) =>
            MergePass(
                lines,
                (a, b) => a.IsCollinearTo(b),
                (Line a, Line b, out Line joined) => TryJoinLines(a, b, out joined)
            );

        public static void Deduplicate(IList<Circle> circles)
        {
            for (var i = circles.Count - 1; i >= 1; i--)
            {
                for (var j = i - 1; j >= 0; j--)
                {
                    if (
                        circles[i].Center.DistanceTo(circles[j].Center) <= Tolerance.Epsilon
                        && circles[i].Radius.IsEqualTo(circles[j].Radius)
                    )
                    {
                        circles.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        public static void Deduplicate(IList<Circle> circles, IList<Arc> arcs)
        {
            for (var i = circles.Count - 1; i >= 0; i--)
            {
                for (var j = arcs.Count - 1; j >= 0; j--)
                {
                    if (
                        arcs[j].Center.DistanceTo(circles[i].Center) <= Tolerance.Epsilon
                        && arcs[j].Radius.IsEqualTo(circles[i].Radius)
                        && arcs[j].IsFullCircle()
                    )
                    {
                        arcs.RemoveAt(j);
                    }
                }
            }
        }

        /// <summary>
        /// Replaces arcs of one center, radius and layer that together cover the whole circle with
        /// a <see cref="Circle"/>. CAM exports often overshoot the seam of a hole and return along
        /// a short chord; such a line, lying wholly on the overlap, goes with the arcs. Arcs that
        /// leave a gap, or overlap by more than <see cref="MaxSeamOverlap"/>, are left alone.
        /// </summary>
        public static void JoinFullCircles(List<Entity> entities)
        {
            var groups = new List<List<Arc>>();
            foreach (var arc in entities.OfType<Arc>())
            {
                if (!SpecialLayers.IsMaterial(arc.Layer))
                    continue;

                var group = groups.Find(g => IsSameCircle(g[0], arc));
                if (group == null)
                    groups.Add(new List<Arc> { arc });
                else
                    group.Add(arc);
            }

            foreach (var group in groups)
            {
                var longest = group.OrderByDescending(a => a.SweepAngle()).First();
                var tolerance = Tolerance.ChainTolerance / longest.Radius;
                var spans = group.Select(ToSpan).OrderBy(s => s.Start).ToList();
                var overlap = spans.Sum(s => s.Sweep) - Angle.TwoPI;

                if (group.Count < 2 || overlap > MaxSeamOverlap || !CoversCircle(spans, tolerance))
                    continue;

                var chords = entities
                    .OfType<Line>()
                    .Where(line => IsSeamChord(line, longest, spans, tolerance))
                    .ToList();

                var index = entities.FindIndex(e => e is Arc arc && group.Contains(arc));
                entities.Insert(
                    index,
                    new Circle(longest.Center, longest.Radius)
                    {
                        Layer = longest.Layer,
                        Color = longest.Color,
                    }
                );
                entities.RemoveAll(e => e is Arc arc && group.Contains(arc));
                entities.RemoveAll(e => e is Line line && chords.Contains(line));
            }
        }

        /// <summary>The most the arcs of a joined circle may overlap, in radians: 30 degrees.</summary>
        public const double MaxSeamOverlap = System.Math.PI / 6;

        private static bool IsSameCircle(Arc a, Arc b) =>
            a.Layer?.Name == b.Layer?.Name
            && a.Center.DistanceTo(b.Center) <= Tolerance.ChainTolerance
            && System.Math.Abs(a.Radius - b.Radius) <= Tolerance.ChainTolerance;

        private static (double Start, double Sweep) ToSpan(Arc arc) =>
            (Angle.NormalizeRad(arc.IsReversed ? arc.EndAngle : arc.StartAngle), arc.SweepAngle());

        // Walks the spans from angle zero, first crediting whatever part of them wraps past it.
        private static bool CoversCircle(List<(double Start, double Sweep)> spans, double tolerance)
        {
            var reach = spans.Max(s => s.Start + s.Sweep - Angle.TwoPI);
            foreach (var (start, sweep) in spans)
            {
                if (start > reach + tolerance)
                    return false;
                reach = System.Math.Max(reach, start + sweep);
            }

            return reach >= Angle.TwoPI - tolerance;
        }

        private static bool IsSeamChord(
            Line line,
            Arc circle,
            List<(double Start, double Sweep)> spans,
            double tolerance
        )
        {
            if (line.Layer?.Name != circle.Layer?.Name)
                return false;

            var center = circle.Center;
            bool OnCircle(Vector point) =>
                System.Math.Abs(point.DistanceTo(center) - circle.Radius) <= Tolerance.ChainTolerance;
            bool OnOverlap(Vector point)
            {
                var angle = System.Math.Atan2(point.Y - center.Y, point.X - center.X);
                return spans.Count(s =>
                    {
                        var offset = Angle.NormalizeRad(angle - s.Start);
                        return offset <= s.Sweep + tolerance || offset >= Angle.TwoPI - tolerance;
                    }) >= 2;
            }

            var middle = new Vector(
                (line.StartPoint.X + line.EndPoint.X) / 2,
                (line.StartPoint.Y + line.EndPoint.Y) / 2
            );
            return OnCircle(line.StartPoint)
                && OnCircle(line.EndPoint)
                && OnOverlap(line.StartPoint)
                && OnOverlap(line.EndPoint)
                && OnOverlap(middle);
        }

        private delegate bool TryJoin<T>(T a, T b, out T joined);

        private static void MergePass<T>(
            IList<T> items,
            Func<T, T, bool> related,
            TryJoin<T> tryJoin
        )
            where T : class
        {
            for (var i = 0; i < items.Count; ++i)
            {
                var item = items[i];
                var candidates = FindRelated(items, item, i, related);
                var index = 0;

                while (index < candidates.Count)
                {
                    var candidate = candidates[index];

                    if (!tryJoin(item, candidate, out var joined))
                    {
                        index++;
                        continue;
                    }

                    candidates.Remove(candidate);
                    items.Remove(candidate);

                    item = joined;
                    index = 0;
                }

                items[i] = item;
            }
        }

        public static bool TryJoinLines(Line line1, Line line2, out Line lineOut)
        {
            lineOut = null;

            if (line1 == line2)
                return false;

            if (line1.Layer?.Name != line2.Layer?.Name)
                return false;

            if (!line1.IsCollinearTo(line2))
                return false;

            bool onPoint = false;

            if (line1.StartPoint == line2.StartPoint)
                onPoint = true;
            else if (line1.StartPoint == line2.EndPoint)
                onPoint = true;
            else if (line1.EndPoint == line2.StartPoint)
                onPoint = true;
            else if (line1.EndPoint == line2.EndPoint)
                onPoint = true;

            var t1 = line1.StartPoint.Y > line1.EndPoint.Y ? line1.StartPoint.Y : line1.EndPoint.Y;
            var t2 = line2.StartPoint.Y > line2.EndPoint.Y ? line2.StartPoint.Y : line2.EndPoint.Y;
            var b1 = line1.StartPoint.Y < line1.EndPoint.Y ? line1.StartPoint.Y : line1.EndPoint.Y;
            var b2 = line2.StartPoint.Y < line2.EndPoint.Y ? line2.StartPoint.Y : line2.EndPoint.Y;
            var l1 = line1.StartPoint.X < line1.EndPoint.X ? line1.StartPoint.X : line1.EndPoint.X;
            var l2 = line2.StartPoint.X < line2.EndPoint.X ? line2.StartPoint.X : line2.EndPoint.X;
            var r1 = line1.StartPoint.X > line1.EndPoint.X ? line1.StartPoint.X : line1.EndPoint.X;
            var r2 = line2.StartPoint.X > line2.EndPoint.X ? line2.StartPoint.X : line2.EndPoint.X;

            if (!onPoint)
            {
                if (t1 < b2 - Tolerance.Epsilon)
                    return false;
                if (b1 > t2 + Tolerance.Epsilon)
                    return false;
                if (l1 > r2 + Tolerance.Epsilon)
                    return false;
                if (r1 < l2 - Tolerance.Epsilon)
                    return false;
            }

            var l = l1 < l2 ? l1 : l2;
            var r = r1 > r2 ? r1 : r2;
            var t = t1 > t2 ? t1 : t2;
            var b = b1 < b2 ? b1 : b2;

            if (!line1.IsVertical() && line1.Slope() < 0)
                lineOut = new Line(new Vector(l, t), new Vector(r, b))
                {
                    Layer = line1.Layer,
                    Color = line1.Color,
                };
            else
                lineOut = new Line(new Vector(l, b), new Vector(r, t))
                {
                    Layer = line1.Layer,
                    Color = line1.Color,
                };

            return true;
        }

        public static bool TryJoinArcs(Arc arc1, Arc arc2, out Arc arcOut)
        {
            arcOut = null;

            if (arc1 == arc2)
                return false;

            if (arc1.Layer?.Name != arc2.Layer?.Name)
                return false;

            if (arc1.Center != arc2.Center)
                return false;

            if (!arc1.Radius.IsEqualTo(arc2.Radius))
                return false;

            var start1 = arc1.StartAngle;
            var end1 = arc1.EndAngle;
            var start2 = arc2.StartAngle;
            var end2 = arc2.EndAngle;

            if (start1 > end1)
                start1 -= Angle.TwoPI;

            if (start2 > end2)
                start2 -= Angle.TwoPI;

            // Check that arcs are adjacent (endpoints touch), not overlapping
            var touch1 = end1.IsEqualTo(start2) || (end1 + Angle.TwoPI).IsEqualTo(start2);
            var touch2 = end2.IsEqualTo(start1) || (end2 + Angle.TwoPI).IsEqualTo(start1);
            if (!touch1 && !touch2)
                return false;

            var startAngle = start1 < start2 ? start1 : start2;
            var endAngle = end1 > end2 ? end1 : end2;

            // Don't merge if the result would be a full circle (start == end)
            var sweep = endAngle - startAngle;
            if (sweep >= Angle.TwoPI - Tolerance.Epsilon)
                return false;

            if (startAngle < 0)
                startAngle += Angle.TwoPI;
            if (endAngle < 0)
                endAngle += Angle.TwoPI;

            arcOut = new Arc(arc1.Center, arc1.Radius, startAngle, endAngle)
            {
                Layer = arc1.Layer,
                Color = arc1.Color,
            };

            return true;
        }

        /// <summary>
        /// The items from <paramref name="startIndex"/> on, other than <paramref name="item"/>
        /// itself, that are <paramref name="related"/> to it.
        /// </summary>
        private static List<T> FindRelated<T>(
            IList<T> items,
            T item,
            int startIndex,
            Func<T, T, bool> related
        )
            where T : class
        {
            var matches = new List<T>();

            Parallel.For(
                startIndex,
                items.Count,
                index =>
                {
                    var candidate = items[index];

                    if (Object.ReferenceEquals(item, candidate))
                        return;

                    if (!related(item, candidate))
                        return;

                    lock (matches)
                    {
                        matches.Add(candidate);
                    }
                }
            );

            return matches;
        }
    }
}
