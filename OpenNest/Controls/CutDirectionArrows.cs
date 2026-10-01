using System.Collections.Generic;
using System.Drawing;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Controls
{
    /// <summary>
    /// Draws the cut-direction arrows for a CNC program. The position and tangent
    /// math lives in <see cref="ContourSampler"/> (OpenNest.Core); this class owns
    /// the display side only: screen conversion, arrowheads, and the per-move
    /// spacing policy the sampler's arrow schedule implements.
    /// </summary>
    internal static class CutDirectionArrows
    {
        public static void DrawProgram(
            Graphics g,
            DrawControl view,
            Program pgm,
            ref Vector pos,
            Pen pen,
            double spacing,
            float arrowSize
        )
        {
            DrawProgram(g, view, pgm, pos, ref pos, pen, spacing, arrowSize);
        }

        private static void DrawProgram(
            Graphics g,
            DrawControl view,
            Program pgm,
            Vector basePos,
            ref Vector pos,
            Pen pen,
            double spacing,
            float arrowSize
        )
        {
            var samples = new List<ContourSample>();
            var end = ContourSampler.ProgramMoves(pgm, basePos, pos, spacing, samples);

            foreach (var sample in samples)
            {
                var screenPt = view.PointWorldToGraph(sample.Position);
                // Screen space flips Y, so the screen angle mirrors the world tangent.
                var screenAngle = System.Math.Atan2(-sample.Direction.Y, sample.Direction.X);
                DrawArrowHead(g, pen, screenPt, screenAngle, arrowSize);
            }

            pos = end;
        }

        private static void DrawArrowHead(Graphics g, Pen pen, PointF tip, double angle, float size)
        {
            var leftAngle = angle + System.Math.PI + 0.5;
            var rightAngle = angle + System.Math.PI - 0.5;

            var left = new PointF(
                tip.X + size * (float)System.Math.Cos(leftAngle),
                tip.Y + size * (float)System.Math.Sin(leftAngle)
            );
            var right = new PointF(
                tip.X + size * (float)System.Math.Cos(rightAngle),
                tip.Y + size * (float)System.Math.Sin(rightAngle)
            );

            g.DrawLine(pen, left, tip);
            g.DrawLine(pen, right, tip);
        }
    }
}
