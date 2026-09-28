using System.Collections.Generic;
using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>
    /// Candidate contact events of directional slides. Each emitter reports every forward
    /// hit its distance kernel considers, with the distance snapped exactly as that kernel
    /// snaps it, so the nearest event equals the kernel's historical minimum.
    /// </summary>
    internal static class SlideEvents
    {
        private const double Eps = Tolerance.Epsilon;

        private static double Snap(double t) => t > Eps ? t : 0;

        /// <summary>
        /// Ray from a vertex against one entity. When <paramref name="vertexMoves"/> is true
        /// the vertex belongs to the moving boundary and the ray follows the push direction;
        /// otherwise it is a stationary vertex and the ray runs opposite to the push.
        /// </summary>
        public static void Ray<TSink>(
            ref TSink sink,
            double vx,
            double vy,
            Entity entity,
            double entityDx,
            double entityDy,
            double rayX,
            double rayY,
            bool vertexMoves
        )
            where TSink : struct, ISlideEventSink
        {
            switch (entity)
            {
                case Line line:
                    RayLine(
                        ref sink,
                        vx,
                        vy,
                        line.pt1.X + entityDx,
                        line.pt1.Y + entityDy,
                        line.pt2.X + entityDx,
                        line.pt2.Y + entityDy,
                        rayX,
                        rayY,
                        vertexMoves
                    );
                    break;

                case Arc arc:
                    {
                        var cx = arc.Center.X + entityDx;
                        var cy = arc.Center.Y + entityDy;
                        if (!SolveRayCircle(vx, vy, cx, cy, arc.Radius, rayX, rayY, out var t1, out var t2))
                            return;

                        for (var k = 0; k < 2; k++)
                        {
                            var t = k == 0 ? t1 : t2;
                            if (t <= -Eps)
                                continue;

                            var hitAngle = Angle.NormalizeRad(
                                System.Math.Atan2(vy + t * rayY - cy, vx + t * rayX - cx)
                            );
                            if (!Angle.IsBetweenRad(hitAngle, arc.StartAngle, arc.EndAngle, arc.IsReversed))
                                continue;

                            Emit(ref sink, vx, vy, t, rayX, rayY, vertexMoves);
                            if (sink.IsDone)
                                return;
                        }
                        break;
                    }

                case Circle circle:
                    {
                        if (
                            !SolveRayCircle(
                                vx,
                                vy,
                                circle.Center.X + entityDx,
                                circle.Center.Y + entityDy,
                                circle.Radius,
                                rayX,
                                rayY,
                                out var t1,
                                out var t2
                            )
                        )
                            return;

                        for (var k = 0; k < 2; k++)
                        {
                            var t = k == 0 ? t1 : t2;
                            if (t < -Eps)
                                continue;

                            Emit(ref sink, vx, vy, t, rayX, rayY, vertexMoves);
                            if (sink.IsDone)
                                return;
                        }
                        break;
                    }
            }
        }

        /// <summary>Same hit rule as <see cref="SpatialQuery.RayEdgeDistance(double, double, double, double, double, double, double, double)"/>.</summary>
        public static void RayLine<TSink>(
            ref TSink sink,
            double vx,
            double vy,
            double p1x,
            double p1y,
            double p2x,
            double p2y,
            double rayX,
            double rayY,
            bool vertexMoves
        )
            where TSink : struct, ISlideEventSink
        {
            var ex = p2x - p1x;
            var ey = p2y - p1y;

            var det = ex * rayY - ey * rayX;
            if (System.Math.Abs(det) < Eps)
                return;

            var dvx = p1x - vx;
            var dvy = p1y - vy;

            var t = (ex * dvy - ey * dvx) / det;
            if (t < -Eps)
                return;

            var s = (rayX * dvy - rayY * dvx) / det;
            if (s < -Eps || s > 1.0 + Eps)
                return;

            Emit(ref sink, vx, vy, t, rayX, rayY, vertexMoves);
        }

        /// <summary>
        /// Axis-aligned ray against a segment, with the same hit rule as the
        /// <see cref="PushDirection"/> kernel.
        /// </summary>
        public static void AxisRayLine<TSink>(
            ref TSink sink,
            double vx,
            double vy,
            double p1x,
            double p1y,
            double p2x,
            double p2y,
            PushDirection rayDirection,
            bool vertexMoves
        )
            where TSink : struct, ISlideEventSink
        {
            double dist,
                hx,
                hy;

            switch (rayDirection)
            {
                case PushDirection.Left:
                case PushDirection.Right:
                    {
                        var dy = p2y - p1y;
                        if (System.Math.Abs(dy) < Eps)
                            return;

                        var t = (vy - p1y) / dy;
                        if (t < -Eps || t > 1.0 + Eps)
                            return;

                        hx = p1x + t * (p2x - p1x);
                        hy = vy;
                        dist = rayDirection == PushDirection.Left ? vx - hx : hx - vx;
                        break;
                    }

                case PushDirection.Down:
                case PushDirection.Up:
                    {
                        var dx = p2x - p1x;
                        if (System.Math.Abs(dx) < Eps)
                            return;

                        var t = (vx - p1x) / dx;
                        if (t < -Eps || t > 1.0 + Eps)
                            return;

                        hx = vx;
                        hy = p1y + t * (p2y - p1y);
                        dist = rayDirection == PushDirection.Down ? vy - hy : hy - vy;
                        break;
                    }

                default:
                    return;
            }

            if (dist < -Eps)
                return;

            var vertex = new Vector(vx, vy);
            var hit = new Vector(hx, hy);
            if (vertexMoves)
                sink.Add(Snap(dist), vertex, hit);
            else
                sink.Add(Snap(dist), hit, vertex);
        }

        /// <summary>
        /// Closest-approach points of arcs against lines, which vertex sampling can miss.
        /// </summary>
        public static void ArcToLine<TSink>(
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
                if (!TryGetCurve(arcEntities[i], out var localCx, out var localCy, out var r))
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
                    if (System.Math.Abs(det) < Eps)
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

                        RayLine(ref sink, qx, qy, p1x, p1y, p2x, p2y, rayX, rayY, arcMoves);
                        if (sink.IsDone)
                            return;
                    }
                }
            }
        }

        /// <summary>
        /// External and internal tangencies of two curves along a unit direction. Radii must
        /// be nonnegative; a null arc is a full circle.
        /// </summary>
        public static void CurveTangency<TSink>(
            ref TSink sink,
            double movingCx,
            double movingCy,
            double movingRadius,
            Arc movingArc,
            double stationaryCx,
            double stationaryCy,
            double stationaryRadius,
            Arc stationaryArc,
            double dirX,
            double dirY
        )
            where TSink : struct, ISlideEventSink
        {
            for (var kind = 0; kind < 2; kind++)
            {
                var internalContact = kind == 1;
                var radius = internalContact
                    ? System.Math.Abs(movingRadius - stationaryRadius)
                    : movingRadius + stationaryRadius;

                // Equal-radius internal contact has coincident centers, not a unique
                // tangent point. Endpoints detect any overlap of those angular spans.
                if (radius == 0)
                    continue;

                if (
                    !SolveRayCircle(
                        movingCx,
                        movingCy,
                        stationaryCx,
                        stationaryCy,
                        radius,
                        dirX,
                        dirY,
                        out var t1,
                        out var t2
                    )
                )
                    continue;

                // The nearer center-circle root can be outside an arc while the farther
                // root is its first contact. Check the actual tangent point at BOTH roots.
                for (var root = 0; root < 2; root++)
                {
                    var t = root == 0 ? t1 : t2;
                    if (t < -Eps)
                        continue;

                    var toX = stationaryCx - (movingCx + t * dirX);
                    var toY = stationaryCy - (movingCy + t * dirY);
                    var movingSign = internalContact && movingRadius < stationaryRadius ? -1 : 1;
                    var stationarySign = internalContact ? movingSign : -1;
                    if (
                        !ContainsContactAngle(
                            movingArc,
                            movingRadius,
                            movingSign * toX,
                            movingSign * toY
                        )
                        || !ContainsContactAngle(
                            stationaryArc,
                            stationaryRadius,
                            stationarySign * toX,
                            stationarySign * toY
                        )
                    )
                        continue;

                    var length = System.Math.Sqrt(toX * toX + toY * toY);
                    var ux = length > 0 ? toX / length : 0;
                    var uy = length > 0 ? toY / length : 0;
                    var movingPoint = new Vector(
                        movingCx + movingSign * movingRadius * ux,
                        movingCy + movingSign * movingRadius * uy
                    );
                    var stationaryPoint = new Vector(
                        stationaryCx + stationarySign * stationaryRadius * ux,
                        stationaryCy + stationarySign * stationaryRadius * uy
                    );

                    sink.Add(Snap(t), movingPoint, stationaryPoint);
                    if (sink.IsDone)
                        return;
                }
            }
        }

        public static bool TryGetCurve(Entity entity, out double cx, out double cy, out double r)
        {
            switch (entity)
            {
                case Circle circle:
                    cx = circle.Center.X;
                    cy = circle.Center.Y;
                    r = circle.Radius;
                    return true;
                case Arc arc:
                    cx = arc.Center.X;
                    cy = arc.Center.Y;
                    r = arc.Radius;
                    return true;
                default:
                    cx = cy = r = 0;
                    return false;
            }
        }

        private static void Emit<TSink>(
            ref TSink sink,
            double vx,
            double vy,
            double t,
            double rayX,
            double rayY,
            bool vertexMoves
        )
            where TSink : struct, ISlideEventSink
        {
            var vertex = new Vector(vx, vy);
            var hit = new Vector(vx + t * rayX, vy + t * rayY);
            if (vertexMoves)
                sink.Add(Snap(t), vertex, hit);
            else
                sink.Add(Snap(t), hit, vertex);
        }

        private static bool ContainsContactAngle(Arc arc, double radius, double x, double y)
        {
            // A zero-radius curve is a point: its angular range has no geometric meaning.
            if (arc == null || radius == 0)
                return true;
            var angle = Angle.NormalizeRad(System.Math.Atan2(y, x));
            return Angle.IsBetweenRad(angle, arc.StartAngle, arc.EndAngle, arc.IsReversed);
        }

        internal static bool SolveRayCircle(
            double vx,
            double vy,
            double cx,
            double cy,
            double r,
            double dirX,
            double dirY,
            out double t1,
            out double t2
        )
        {
            var ox = vx - cx;
            var oy = vy - cy;

            var a = dirX * dirX + dirY * dirY;
            var b = 2.0 * (ox * dirX + oy * dirY);
            var c = ox * ox + oy * oy - r * r;

            var discriminant = b * b - 4.0 * a * c;
            if (discriminant < 0)
            {
                t1 = t2 = double.MaxValue;
                return false;
            }

            var sqrtD = System.Math.Sqrt(discriminant);
            var inv2a = 1.0 / (2.0 * a);
            t1 = (-b - sqrtD) * inv2a;
            t2 = (-b + sqrtD) * inv2a;
            return true;
        }
    }

    /// <summary>
    /// Slide events between native Line/Arc/Circle boundaries. The moving entities and
    /// vertices are translated by (movingDx, movingDy); vertex arrays may be subsets.
    /// </summary>
    public struct EntitySlideEvents : ISlideEventSource
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

        public EntitySlideEvents(
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
                SlideEvents.ArcToLine(
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
                SlideEvents.ArcToLine(
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

    /// <summary>
    /// Slide events between line boundaries along an arbitrary unit direction. The moving
    /// lines and vertices are translated by (movingDx, movingDy); vertex arrays may be subsets.
    /// </summary>
    public struct LineSlideEvents : ISlideEventSource
    {
        private readonly List<Line> moving;
        private readonly Vector[] movingVertices;
        private readonly double movingDx;
        private readonly double movingDy;
        private readonly List<Line> stationary;
        private readonly Vector[] stationaryVertices;
        private readonly double dirX;
        private readonly double dirY;

        public LineSlideEvents(
            List<Line> moving,
            Vector[] movingVertices,
            double movingDx,
            double movingDy,
            List<Line> stationary,
            Vector[] stationaryVertices,
            double dirX,
            double dirY
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
        }

        public void Enumerate<TSink>(ref TSink sink)
            where TSink : struct, ISlideEventSink
        {
            for (var v = 0; v < movingVertices.Length; v++)
            {
                var vx = movingVertices[v].X + movingDx;
                var vy = movingVertices[v].Y + movingDy;

                for (var j = 0; j < stationary.Count; j++)
                {
                    var e = stationary[j];
                    SlideEvents.RayLine(
                        ref sink,
                        vx,
                        vy,
                        e.pt1.X,
                        e.pt1.Y,
                        e.pt2.X,
                        e.pt2.Y,
                        dirX,
                        dirY,
                        true
                    );
                    if (sink.IsDone)
                        return;
                }
            }

            for (var v = 0; v < stationaryVertices.Length; v++)
            {
                var vx = stationaryVertices[v].X;
                var vy = stationaryVertices[v].Y;

                for (var j = 0; j < moving.Count; j++)
                {
                    var e = moving[j];
                    SlideEvents.RayLine(
                        ref sink,
                        vx,
                        vy,
                        e.pt1.X + movingDx,
                        e.pt1.Y + movingDy,
                        e.pt2.X + movingDx,
                        e.pt2.Y + movingDy,
                        -dirX,
                        -dirY,
                        false
                    );
                    if (sink.IsDone)
                        return;
                }
            }
        }
    }

    /// <summary>
    /// Axis-aligned slide events between edge arrays sorted for pruning, as used by the
    /// <see cref="PushDirection"/> kernel. Offsets translate each side into world space.
    /// </summary>
    public struct AxisSlideEvents : ISlideEventSource
    {
        private readonly (Vector start, Vector end)[] movingEdges;
        private readonly Vector movingOffset;
        private readonly Vector[] movingVertices;
        private readonly (Vector start, Vector end)[] stationaryEdges;
        private readonly Vector stationaryOffset;
        private readonly Vector[] stationaryVertices;
        private readonly PushDirection direction;

        /// <param name="movingVertices">World-space moving vertices.</param>
        /// <param name="stationaryVertices">World-space stationary vertices.</param>
        public AxisSlideEvents(
            (Vector start, Vector end)[] movingEdges,
            Vector movingOffset,
            Vector[] movingVertices,
            (Vector start, Vector end)[] stationaryEdges,
            Vector stationaryOffset,
            Vector[] stationaryVertices,
            PushDirection direction
        )
        {
            this.movingEdges = movingEdges;
            this.movingOffset = movingOffset;
            this.movingVertices = movingVertices;
            this.stationaryEdges = stationaryEdges;
            this.stationaryOffset = stationaryOffset;
            this.stationaryVertices = stationaryVertices;
            this.direction = direction;
        }

        public void Enumerate<TSink>(ref TSink sink)
            where TSink : struct, ISlideEventSink
        {
            for (var v = 0; v < movingVertices.Length; v++)
            {
                OneWay(ref sink, movingVertices[v], stationaryEdges, stationaryOffset, direction, true);
                if (sink.IsDone)
                    return;
            }

            var opposite = SpatialQuery.OppositeDirection(direction);
            for (var v = 0; v < stationaryVertices.Length; v++)
            {
                OneWay(ref sink, stationaryVertices[v], movingEdges, movingOffset, opposite, false);
                if (sink.IsDone)
                    return;
            }
        }

        private static void OneWay<TSink>(
            ref TSink sink,
            Vector vertex,
            (Vector start, Vector end)[] edges,
            Vector edgeOffset,
            PushDirection rayDirection,
            bool vertexMoves
        )
            where TSink : struct, ISlideEventSink
        {
            var vx = vertex.X;
            var vy = vertex.Y;
            var horizontal = SpatialQuery.IsHorizontalDirection(rayDirection);

            // Edges are sorted by their perpendicular min-coordinate.
            for (var i = 0; i < edges.Length; i++)
            {
                var e1 = edges[i].start + edgeOffset;
                var e2 = edges[i].end + edgeOffset;

                double perpValue,
                    edgeMin,
                    edgeMax;
                if (horizontal)
                {
                    perpValue = vy;
                    edgeMin = e1.Y < e2.Y ? e1.Y : e2.Y;
                    edgeMax = e1.Y > e2.Y ? e1.Y : e2.Y;
                }
                else
                {
                    perpValue = vx;
                    edgeMin = e1.X < e2.X ? e1.X : e2.X;
                    edgeMax = e1.X > e2.X ? e1.X : e2.X;
                }

                if (perpValue < edgeMin - Tolerance.Epsilon)
                    break;

                if (perpValue > edgeMax + Tolerance.Epsilon)
                    continue;

                SlideEvents.AxisRayLine(
                    ref sink,
                    vx,
                    vy,
                    e1.X,
                    e1.Y,
                    e2.X,
                    e2.Y,
                    rayDirection,
                    vertexMoves
                );
                if (sink.IsDone)
                    return;
            }
        }
    }
}
