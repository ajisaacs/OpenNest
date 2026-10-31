namespace OpenNest.Geometry
{
    /// <summary>
    /// Conservative perpendicular-extent tests for slides along an exact axis direction.
    /// A ray parallel to an axis can only meet an entity whose extent across that axis holds
    /// the ray's perpendicular coordinate. Each test answers "may emit" whenever the matching
    /// kernel could still report a hit: margins cover the kernel's own tolerances and its
    /// floating-point rounding. A pair with any coordinate or radius that is NaN, infinite
    /// or beyond <see cref="CoordinateLimit"/> is always kept, because the kernels' products
    /// can then become NaN and still emit.
    /// </summary>
    internal static class SlideBand
    {
        internal enum Axis
        {
            None,

            /// <summary>Push along ±X; the perpendicular coordinate is Y.</summary>
            PerpendicularY,

            /// <summary>Push along ±Y; the perpendicular coordinate is X.</summary>
            PerpendicularX,
        }

        /// <summary>
        /// Largest coordinate or radius magnitude the tests reason about. Below it every
        /// square and product in the kernels stays finite (the squares stay below 1e301).
        /// </summary>
        internal const double CoordinateLimit = 1e150;

        // RayLine accepts a segment parameter in [-Eps, 1 + Eps], which reaches Eps of the
        // segment's perpendicular span past either end. Twice that is kept: the computed
        // parameter differs from the exact one by a few ulps, far less than the extra Eps.
        // No absolute slack is needed: the kernels translate entities with the same
        // expressions as these tests, and a circle point c + r·sinθ rounds monotonically, so
        // it never lies outside fl(c ± r).
        private const double SpanSlack = 2 * Math.Tolerance.Epsilon;

        // SolveRayCircle accepts a nonnegative discriminant b² − 4ac. For an axis ray that is
        // 4(r² − oy²) up to rounding of terms of size ox² + oy² + r², so a hit is possible when
        // oy² ≤ r² + ε(1 + ox² + oy² + r²). This ε is hundreds of ulps; the absolute 1 is a
        // floor for squares that underflow.
        private const double QuadraticSlack = 1e-13;

        public static Axis For(double dirX, double dirY)
        {
            if (dirY == 0 && (dirX == 1 || dirX == -1))
                return Axis.PerpendicularY;
            if (dirX == 0 && (dirY == 1 || dirY == -1))
                return Axis.PerpendicularX;
            return Axis.None;
        }

        /// <summary>
        /// False only when <see cref="SlideEvents.Ray"/> from (vx, vy) along the axis cannot
        /// emit against <paramref name="entity"/> translated by (dx, dy).
        /// </summary>
        public static bool RayMayHit(
            Axis axis,
            double vx,
            double vy,
            Entity entity,
            double dx,
            double dy
        )
        {
            if (!Bounded(vx) || !Bounded(vy))
                return true;

            switch (entity)
            {
                case Line line:
                    {
                        var p1x = line.pt1.X + dx;
                        var p1y = line.pt1.Y + dy;
                        var p2x = line.pt2.X + dx;
                        var p2y = line.pt2.Y + dy;
                        if (!Bounded(p1x) || !Bounded(p1y) || !Bounded(p2x) || !Bounded(p2y))
                            return true;
                        return PointMayReachSegment(
                            Perp(axis, vx, vy),
                            Perp(axis, p1x, p1y),
                            Perp(axis, p2x, p2y)
                        );
                    }
                case Arc arc:
                    return PointMayReachCircle(
                        axis,
                        vx,
                        vy,
                        arc.Center.X + dx,
                        arc.Center.Y + dy,
                        arc.Radius
                    );
                case Circle circle:
                    return PointMayReachCircle(
                        axis,
                        vx,
                        vy,
                        circle.Center.X + dx,
                        circle.Center.Y + dy,
                        circle.Radius
                    );
                default:
                    return true;
            }
        }

        /// <summary>
        /// False only when no point of the circle (cx, cy, r) can reach the segment along the
        /// axis, which is what <see cref="SlideEvents.ArcToLine"/> tests for each arc/line pair.
        /// </summary>
        public static bool CircleMayReachLine(
            Axis axis,
            double cx,
            double cy,
            double r,
            double p1x,
            double p1y,
            double p2x,
            double p2y
        )
        {
            if (
                !Bounded(cx) || !Bounded(cy) || !Bounded(r)
                || !Bounded(p1x) || !Bounded(p1y) || !Bounded(p2x) || !Bounded(p2y)
            )
                return true;

            var c = Perp(axis, cx, cy);
            var a = Perp(axis, p1x, p1y);
            var b = Perp(axis, p2x, p2y);
            var lo = System.Math.Min(a, b);
            var hi = System.Math.Max(a, b);
            var radius = System.Math.Abs(r);
            var margin = SpanSlack * (hi - lo);
            if (c + radius < lo - margin || c - radius > hi + margin)
                return false;
            return true;
        }

        /// <summary>
        /// False only when two circles sliding along the axis can never touch, the condition
        /// <see cref="SlideEvents.CurveTangency"/> solves for both external and internal contact.
        /// </summary>
        public static bool CirclesMayTouch(
            Axis axis,
            double movingCx,
            double movingCy,
            double movingR,
            double stationaryCx,
            double stationaryCy,
            double stationaryR
        )
        {
            if (
                !Bounded(movingCx) || !Bounded(movingCy) || !Bounded(movingR)
                || !Bounded(stationaryCx) || !Bounded(stationaryCy) || !Bounded(stationaryR)
            )
                return true;

            var radius = System.Math.Abs(movingR) + System.Math.Abs(stationaryR);
            return CenterMayReach(
                Para(axis, movingCx, movingCy) - Para(axis, stationaryCx, stationaryCy),
                Perp(axis, movingCx, movingCy) - Perp(axis, stationaryCx, stationaryCy),
                radius
            );
        }

        private static bool PointMayReachSegment(double v, double a, double b)
        {
            var lo = System.Math.Min(a, b);
            var hi = System.Math.Max(a, b);
            var margin = SpanSlack * (hi - lo);
            if (v < lo - margin || v > hi + margin)
                return false;
            return true;
        }

        private static bool PointMayReachCircle(
            Axis axis,
            double vx,
            double vy,
            double cx,
            double cy,
            double r
        )
        {
            if (!Bounded(cx) || !Bounded(cy) || !Bounded(r))
                return true;
            return CenterMayReach(
                Para(axis, vx, vy) - Para(axis, cx, cy),
                Perp(axis, vx, vy) - Perp(axis, cx, cy),
                r
            );
        }

        private static bool CenterMayReach(double ox, double oy, double r)
        {
            var oy2 = oy * oy;
            var r2 = r * r;
            var slack = QuadraticSlack * (1 + ox * ox + oy2 + r2);
            if (oy2 > r2 + slack)
                return false;
            return true;
        }

        /// <summary>False for NaN, infinities and magnitudes beyond the coordinate limit.</summary>
        private static bool Bounded(double value) => System.Math.Abs(value) <= CoordinateLimit;

        private static double Perp(Axis axis, double x, double y) =>
            axis == Axis.PerpendicularY ? y : x;

        private static double Para(Axis axis, double x, double y) =>
            axis == Axis.PerpendicularY ? x : y;
    }
}
