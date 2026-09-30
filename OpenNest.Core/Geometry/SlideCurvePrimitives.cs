using OpenNest.Math;

namespace OpenNest.Geometry
{
    /// <summary>Shared curve primitives for raw distance queries and slide contact events.</summary>
    internal static class SlideCurvePrimitives
    {
        internal static bool ContainsContactAngle(Arc arc, double radius, double x, double y)
        {
            // A zero-radius curve is a point: its angular range has no geometric meaning.
            if (arc == null || radius == 0)
                return true;
            var angle = Angle.NormalizeRad(System.Math.Atan2(y, x));
            return Angle.IsBetweenRad(angle, arc.StartAngle, arc.EndAngle, arc.IsReversed);
        }

        /// <summary>Returns both ray-circle parameters, before forward filtering or epsilon snapping.</summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining
        )]
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
}
