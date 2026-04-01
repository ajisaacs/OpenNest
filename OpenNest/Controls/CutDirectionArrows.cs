using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Drawing;

namespace OpenNest.Controls
{
    internal static class CutDirectionArrows
    {
        public static void DrawProgram(Graphics g, DrawControl view, Program pgm, ref Vector pos,
            Pen pen, double spacing, float arrowSize)
        {
            for (var i = 0; i < pgm.Length; ++i)
            {
                var code = pgm[i];

                if (code.Type == CodeType.SubProgramCall)
                {
                    var subpgm = (SubProgramCall)code;
                    if (subpgm.Program != null)
                        DrawProgram(g, view, subpgm.Program, ref pos, pen, spacing, arrowSize);
                    continue;
                }

                if (code is not Motion motion) continue;

                var endpt = pgm.Mode == Mode.Incremental
                    ? motion.EndPoint + pos
                    : motion.EndPoint;

                if (code.Type == CodeType.LinearMove)
                {
                    var line = (LinearMove)code;
                    if (!line.Suppressed)
                        DrawLineArrows(g, view, pos, endpt, pen, spacing, arrowSize);
                }
                else if (code.Type == CodeType.ArcMove)
                {
                    var arc = (ArcMove)code;
                    if (!arc.Suppressed)
                    {
                        var center = pgm.Mode == Mode.Incremental
                            ? arc.CenterPoint + pos
                            : arc.CenterPoint;
                        DrawArcArrows(g, view, pos, endpt, center, arc.Rotation, pen, spacing, arrowSize);
                    }
                }

                pos = endpt;
            }
        }

        private static void DrawLineArrows(Graphics g, DrawControl view, Vector start, Vector end,
            Pen pen, double spacing, float arrowSize)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var length = System.Math.Sqrt(dx * dx + dy * dy);
            if (length < spacing * 0.5) return;

            var dirX = dx / length;
            var dirY = dy / length;

            var count = System.Math.Max(1, (int)(length / spacing));
            var step = length / (count + 1);

            for (var i = 1; i <= count; i++)
            {
                var t = step * i;
                var pt = new Vector(start.X + dirX * t, start.Y + dirY * t);
                var screenPt = view.PointWorldToGraph(pt);
                var angle = System.Math.Atan2(-dirY, dirX);
                DrawArrowHead(g, pen, screenPt, angle, arrowSize);
            }
        }

        private static void DrawArcArrows(Graphics g, DrawControl view, Vector start, Vector end, Vector center,
            RotationType rotation, Pen pen, double spacing, float arrowSize)
        {
            var radius = center.DistanceTo(start);
            if (radius < Tolerance.Epsilon) return;

            var startAngle = System.Math.Atan2(start.Y - center.Y, start.X - center.X);
            var endAngle = System.Math.Atan2(end.Y - center.Y, end.X - center.X);

            double sweep;
            if (rotation == RotationType.CCW)
            {
                sweep = endAngle - startAngle;
                if (sweep <= 0) sweep += 2 * System.Math.PI;
            }
            else
            {
                sweep = startAngle - endAngle;
                if (sweep <= 0) sweep += 2 * System.Math.PI;
            }

            var arcLength = radius * System.Math.Abs(sweep);
            if (arcLength < spacing * 0.5) return;

            var count = System.Math.Max(1, (int)(arcLength / spacing));
            var stepAngle = sweep / (count + 1);

            for (var i = 1; i <= count; i++)
            {
                double angle;
                if (rotation == RotationType.CCW)
                    angle = startAngle + stepAngle * i;
                else
                    angle = startAngle - stepAngle * i;

                var pt = new Vector(
                    center.X + radius * System.Math.Cos(angle),
                    center.Y + radius * System.Math.Sin(angle));
                var screenPt = view.PointWorldToGraph(pt);

                double tangent;
                if (rotation == RotationType.CCW)
                    tangent = angle + System.Math.PI / 2;
                else
                    tangent = angle - System.Math.PI / 2;

                var screenAngle = System.Math.Atan2(-System.Math.Sin(tangent), System.Math.Cos(tangent));
                DrawArrowHead(g, pen, screenPt, screenAngle, arrowSize);
            }
        }

        private static void DrawArrowHead(Graphics g, Pen pen, PointF tip, double angle, float size)
        {
            var leftAngle = angle + System.Math.PI + 0.5;
            var rightAngle = angle + System.Math.PI - 0.5;

            var left = new PointF(
                tip.X + size * (float)System.Math.Cos(leftAngle),
                tip.Y + size * (float)System.Math.Sin(leftAngle));
            var right = new PointF(
                tip.X + size * (float)System.Math.Cos(rightAngle),
                tip.Y + size * (float)System.Math.Sin(rightAngle));

            g.DrawLine(pen, left, tip);
            g.DrawLine(pen, right, tip);
        }
    }
}
