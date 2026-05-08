using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OpenNest.Geometry
{
    public static class GeometryOptimizer
    {
        public static void Optimize(IList<Arc> arcs) =>
            MergePass(arcs,
                (list, item, i) => list.GetCoradialArs(item, i),
                (Arc a, Arc b, out Arc joined) => TryJoinArcs(a, b, out joined));

        public static void Optimize(IList<Line> lines) =>
            MergePass(lines,
                (list, item, i) => list.GetCollinearLines(item, i),
                (Line a, Line b, out Line joined) => TryJoinLines(a, b, out joined));

        public static void Deduplicate(IList<Circle> circles)
        {
            for (var i = circles.Count - 1; i >= 1; i--)
            {
                for (var j = i - 1; j >= 0; j--)
                {
                    if (circles[i].Center.DistanceTo(circles[j].Center) <= Tolerance.Epsilon
                        && circles[i].Radius.IsEqualTo(circles[j].Radius))
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
                    if (arcs[j].Center.DistanceTo(circles[i].Center) <= Tolerance.Epsilon
                        && arcs[j].Radius.IsEqualTo(circles[i].Radius)
                        && arcs[j].IsFullCircle())
                    {
                        arcs.RemoveAt(j);
                    }
                }
            }
        }

        private delegate bool TryJoin<T>(T a, T b, out T joined);

        private static void MergePass<T>(IList<T> items,
            Func<IList<T>, T, int, List<T>> findCandidates,
            TryJoin<T> tryJoin) where T : class
        {
            for (var i = 0; i < items.Count; ++i)
            {
                var item = items[i];
                var candidates = findCandidates(items, item, i);
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
                if (t1 < b2 - Tolerance.Epsilon) return false;
                if (b1 > t2 + Tolerance.Epsilon) return false;
                if (l1 > r2 + Tolerance.Epsilon) return false;
                if (r1 < l2 - Tolerance.Epsilon) return false;
            }

            var l = l1 < l2 ? l1 : l2;
            var r = r1 > r2 ? r1 : r2;
            var t = t1 > t2 ? t1 : t2;
            var b = b1 < b2 ? b1 : b2;

            if (!line1.IsVertical() && line1.Slope() < 0)
                lineOut = new Line(new Vector(l, t), new Vector(r, b)) { Layer = line1.Layer, Color = line1.Color };
            else
                lineOut = new Line(new Vector(l, b), new Vector(r, t)) { Layer = line1.Layer, Color = line1.Color };

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

            if (startAngle < 0) startAngle += Angle.TwoPI;
            if (endAngle < 0) endAngle += Angle.TwoPI;

            arcOut = new Arc(arc1.Center, arc1.Radius, startAngle, endAngle) { Layer = arc1.Layer, Color = arc1.Color };

            return true;
        }

        private static List<Line> GetCollinearLines(this IList<Line> lines, Line line, int startIndex)
        {
            var collinearLines = new List<Line>();

            Parallel.For(startIndex, lines.Count, index =>
            {
                var compareLine = lines[index];

                if (Object.ReferenceEquals(line, compareLine))
                    return;

                if (!line.IsCollinearTo(compareLine))
                    return;

                lock (collinearLines)
                {
                    collinearLines.Add(compareLine);
                }
            });

            return collinearLines;
        }

        private static List<Arc> GetCoradialArs(this IList<Arc> arcs, Arc arc, int startIndex)
        {
            var coradialArcs = new List<Arc>();

            Parallel.For(startIndex, arcs.Count, index =>
            {
                var compareArc = arcs[index];

                if (Object.ReferenceEquals(arc, compareArc))
                    return;

                if (!arc.IsCoradialTo(compareArc))
                    return;

                lock (coradialArcs)
                {
                    coradialArcs.Add(compareArc);
                }
            });

            return coradialArcs;
        }
    }
}
