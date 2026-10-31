// Frozen verbatim copy of EntitySlideEvents.Enumerate and SlideEvents.ArcToLine from
// commit ad5f2a9041ee14d0a85aab00e0c8a9f5082d5b6b, before the axis band filter, used only as
// the differential oracle in SlideBandTests. Mechanical edits only: renamed types, internal
// visibility, calls to unchanged SlideEvents members qualified, the private Eps constant
// spelled Tolerance.Epsilon, and the four-space indentation of the original kept.
#nullable disable
using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry
{
    internal static class LegacySlideEvents
    {
        internal static void ArcToLine<TSink>(
            ref TSink sink,
            List<Entity> arcEntities,
            double arcDx,
            double arcDy,
            List<Entity> lineEntities,
            double lineDx,
            double lineDy,
            double rayX,
            double rayY,
            bool arcMoves
        )
            where TSink : struct, ISlideEventSink
        {
            for (var i = 0; i < arcEntities.Count; i++)
            {
                if (!SlideEvents.TryGetCurve(arcEntities[i], out var localCx, out var localCy, out var r))
                    continue;

                var arc = arcEntities[i] as Arc;
                var cx = localCx + arcDx;
                var cy = localCy + arcDy;

                for (var j = 0; j < lineEntities.Count; j++)
                {
                    if (lineEntities[j] is not Line line)
                        continue;

                    var p1x = line.pt1.X + lineDx;
                    var p1y = line.pt1.Y + lineDy;
                    var p2x = line.pt2.X + lineDx;
                    var p2y = line.pt2.Y + lineDy;
                    var ex = p2x - p1x;
                    var ey = p2y - p1y;

                    var det = ex * rayY - ey * rayX;
                    if (System.Math.Abs(det) < Tolerance.Epsilon)
                        continue;

                    // The directional distance from an arc point at angle θ to the
                    // line is t(θ) = [A + r·(ey·cosθ − ex·sinθ)] / det.
                    // dt/dθ = 0 at θ = atan2(−ex, ey) and θ + π.
                    var theta1 = Angle.NormalizeRad(System.Math.Atan2(-ex, ey));
                    var theta2 = Angle.NormalizeRad(theta1 + System.Math.PI);

                    for (var k = 0; k < 2; k++)
                    {
                        var theta = k == 0 ? theta1 : theta2;

                        if (arc != null && !Angle.IsBetweenRad(theta, arc.StartAngle, arc.EndAngle, arc.IsReversed))
                            continue;

                        var qx = cx + r * System.Math.Cos(theta);
                        var qy = cy + r * System.Math.Sin(theta);

                        SlideEvents.RayLine(ref sink, qx, qy, p1x, p1y, p2x, p2y, rayX, rayY, arcMoves);
                        if (sink.IsDone)
                            return;
                    }
                }
            }
        }
    }

    internal struct LegacyEntitySlideEvents : ISlideEventSource
    {
        private readonly List<Entity> moving;
        private readonly Vector[] movingVertices;
        private readonly double movingDx;
        private readonly double movingDy;
        private readonly List<Entity> stationary;
        private readonly Vector[] stationaryVertices;
        private readonly double dirX;
        private readonly double dirY;
        private readonly bool arcToLine;

        public LegacyEntitySlideEvents(
            List<Entity> moving,
            Vector[] movingVertices,
            double movingDx,
            double movingDy,
            List<Entity> stationary,
            Vector[] stationaryVertices,
            double dirX,
            double dirY,
            bool arcToLine
        )
        {
            this.moving = moving;
            this.movingVertices = movingVertices;
            this.movingDx = movingDx;
            this.movingDy = movingDy;
            this.stationary = stationary;
            this.stationaryVertices = stationaryVertices;
            this.dirX = dirX;
            this.dirY = dirY;
            this.arcToLine = arcToLine;
        }

        public void Enumerate<TSink>(ref TSink sink)
            where TSink : struct, ISlideEventSink
        {
            // Phase 1: moving vertices along the push against stationary entities.
            for (var v = 0; v < movingVertices.Length; v++)
            {
                var vx = movingVertices[v].X + movingDx;
                var vy = movingVertices[v].Y + movingDy;

                for (var j = 0; j < stationary.Count; j++)
                {
                    SlideEvents.Ray(ref sink, vx, vy, stationary[j], 0, 0, dirX, dirY, true);
                    if (sink.IsDone)
                        return;
                }
            }

            // Phase 2: stationary vertices against the push onto moving entities.
            for (var v = 0; v < stationaryVertices.Length; v++)
            {
                var vx = stationaryVertices[v].X;
                var vy = stationaryVertices[v].Y;

                for (var j = 0; j < moving.Count; j++)
                {
                    SlideEvents.Ray(
                        ref sink,
                        vx,
                        vy,
                        moving[j],
                        movingDx,
                        movingDy,
                        -dirX,
                        -dirY,
                        false
                    );
                    if (sink.IsDone)
                        return;
                }
            }

            // Phase 3: arc-to-line closest points, which vertex sampling can miss.
            if (arcToLine)
            {
                LegacySlideEvents.ArcToLine(
                    ref sink,
                    moving,
                    movingDx,
                    movingDy,
                    stationary,
                    0,
                    0,
                    dirX,
                    dirY,
                    true
                );
                if (sink.IsDone)
                    return;
                LegacySlideEvents.ArcToLine(
                    ref sink,
                    stationary,
                    0,
                    0,
                    moving,
                    movingDx,
                    movingDy,
                    -dirX,
                    -dirY,
                    false
                );
                if (sink.IsDone)
                    return;
            }

            // Phase 4: native curve tangency, including a convex corner inside a concave arc.
            for (var i = 0; i < moving.Count; i++)
            {
                if (!SlideEvents.TryGetCurve(moving[i], out var mcx, out var mcy, out var mr))
                    continue;

                for (var j = 0; j < stationary.Count; j++)
                {
                    if (!SlideEvents.TryGetCurve(stationary[j], out var scx, out var scy, out var sr))
                        continue;

                    SlideEvents.CurveTangency(
                        ref sink,
                        mcx + movingDx,
                        mcy + movingDy,
                        mr,
                        moving[i] as Arc,
                        scx,
                        scy,
                        sr,
                        stationary[j] as Arc,
                        dirX,
                        dirY
                    );
                    if (sink.IsDone)
                        return;
                }
            }
        }
    }
}
