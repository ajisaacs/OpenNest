using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    public static class GeometryOptimizer
    {
        public static void Optimize(IList<Arc> arcs)
        {
            for (int i = 0; i < arcs.Count; ++i)
            {
                var arc = arcs[i];

                var coradialArcs = arcs.GetCoradialArs(arc, i);
                int index = 0;

                while (index < coradialArcs.Count)
                {
                    Arc arc2 = coradialArcs[index];
                    Arc joinArc;

                    if (!TryJoinArcs(arc, arc2, out joinArc))
                    {
                        index++;
                        continue;
                    }

                    coradialArcs.Remove(arc2);
                    arcs.Remove(arc2);

                    arc = joinArc;
                    index = 0;
                }

                arcs[i] = arc;
            }
        }

        public static void Optimize(IList<Line> lines)
        {
            for (int i = 0; i < lines.Count; ++i)
            {
                var line = lines[i];

                var collinearLines = lines.GetCollinearLines(line, i);
                var index = 0;

                while (index < collinearLines.Count)
                {
                    Line line2 = collinearLines[index];
                    Line joinLine;

                    if (!TryJoinLines(line, line2, out joinLine))
                    {
                        index++;
                        continue;
                    }

                    collinearLines.Remove(line2);
                    lines.Remove(line2);

                    line = joinLine;
                    index = 0;
                }

                lines[i] = line;
            }
        }

        public static bool TryJoinLines(Line line1, Line line2, out Line lineOut)
        {
            lineOut = null;

            if (line1 == line2)
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
                lineOut = new Line(new Vector(l, t), new Vector(r, b));
            else
                lineOut = new Line(new Vector(l, b), new Vector(r, t));

            return true;
        }

        public static bool TryJoinArcs(Arc arc1, Arc arc2, out Arc arcOut)
        {
            arcOut = null;

            if (arc1 == arc2)
                return false;

            if (arc1.Center != arc2.Center)
                return false;

            if (!arc1.Radius.IsEqualTo(arc2.Radius))
                return false;

            if (arc1.StartAngle > arc1.EndAngle)
                arc1.StartAngle -= Angle.TwoPI;

            if (arc2.StartAngle > arc2.EndAngle)
                arc2.StartAngle -= Angle.TwoPI;

            if (arc1.EndAngle < arc2.StartAngle || arc1.StartAngle > arc2.EndAngle)
                return false;

            var startAngle = arc1.StartAngle < arc2.StartAngle ? arc1.StartAngle : arc2.StartAngle;
            var endAngle = arc1.EndAngle > arc2.EndAngle ? arc1.EndAngle : arc2.EndAngle;

            if (startAngle < 0) startAngle += Angle.TwoPI;
            if (endAngle < 0) endAngle += Angle.TwoPI;

            arcOut = new Arc(arc1.Center, arc1.Radius, startAngle, endAngle);

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
