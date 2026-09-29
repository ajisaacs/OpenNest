using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest.Diagnostics;

/// <summary>Local native line/arc queries, without changing the engine's geometry semantics.</summary>
internal static class PostVerificationGeometry
{
    internal const double Epsilon = 1e-8;
    private const double TwoPi = 2 * System.Math.PI;

    internal static void Validate(Vector point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)
            || System.Math.Abs(point.X) > 1e12 || System.Math.Abs(point.Y) > 1e12)
            throw new ArgumentException("Nonfinite or numerically unsupported program coordinates.");
    }

    internal static bool Closed(IReadOnlyList<Curve> curves) => curves.Count > 0
        && curves[0].Start.DistanceTo(curves[^1].End) <= Epsilon;

    internal static bool Crosses(Vector start, Vector end, IReadOnlyList<Curve> curves,
        CancellationToken token)
    {
        var delta = end - start;
        var length = start.DistanceTo(end);
        if (length <= Epsilon)
            return false;
        var direction = delta * (1 / length);
        foreach (var curve in curves)
        {
            token.ThrowIfCancellationRequested();
            if (curve.ContactAfterStart(start, direction, length))
                return true;
        }
        // If there are no contacts except possibly departure, all open-segment points
        // have the same inside/outside status. A midpoint catches travel entirely inside
        // and departure into the interior, without flagging start-only outward contact.
        var midpoint = start + delta * 0.5;
        var inside = false;
        foreach (var curve in curves)
        {
            token.ThrowIfCancellationRequested();
            if (curve.CrossesRay(midpoint))
                inside = !inside;
        }
        return inside;
    }

    private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;
    private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
    private static double Normalize(double angle)
    {
        angle %= TwoPi;
        return angle < 0 ? angle + TwoPi : angle;
    }

    internal sealed class Curve
    {
        private Curve(Vector start, Vector end, Vector? center, double radius, double sweep)
        {
            Start = start;
            End = end;
            Center = center;
            Radius = radius;
            Sweep = sweep;
        }

        internal Vector Start { get; }
        internal Vector End { get; }
        private Vector? Center { get; }
        private double Radius { get; }
        private double Sweep { get; }
        internal double Length => Center.HasValue ? Radius * System.Math.Abs(Sweep) : Start.DistanceTo(End);

        internal static Curve Create(Vector start, Vector end, Vector? center, bool clockwise)
        {
            if (center is not { } c)
                return new Curve(start, end, null, 0, 0);
            Validate(c);
            var radius = start.DistanceTo(c);
            if (radius <= Epsilon || !double.IsFinite(radius)
                || System.Math.Abs(radius - end.DistanceTo(c)) > Epsilon * System.Math.Max(1, radius))
                throw new ArgumentException("Arc has zero or inconsistent radius.");
            var a = System.Math.Atan2(start.Y - c.Y, start.X - c.X);
            var b = System.Math.Atan2(end.Y - c.Y, end.X - c.X);
            var sweep = start.DistanceTo(end) <= Epsilon ? TwoPi
                : Normalize(clockwise ? a - b : b - a);
            return new Curve(start, end, c, radius, clockwise ? -sweep : sweep);
        }

        private double StartAngle => System.Math.Atan2(Start.Y - Center.Value.Y, Start.X - Center.Value.X);
        private double Travel(double angle) => Normalize(Sweep < 0 ? StartAngle - angle : angle - StartAngle);
        private bool OnArc(Vector point) => Travel(System.Math.Atan2(point.Y - Center.Value.Y,
            point.X - Center.Value.X)) <= System.Math.Abs(Sweep) + Epsilon / Radius
            || point.DistanceTo(Start) <= Epsilon || point.DistanceTo(End) <= Epsilon;

        internal bool ContactAfterStart(Vector origin, Vector direction, double length)
        {
            if (Center is { } center)
            {
                // Intersect the actual circle, not an inscribed chord polygon: tangencies
                // and short arcs must not vanish between tessellation vertices.
                var relative = center - origin;
                var projection = Dot(relative, direction);
                var perpendicular = Cross(relative, direction);
                var square = Radius * Radius - perpendicular * perpendicular;
                if (square < -Epsilon * System.Math.Max(1, Radius * 2))
                    return false;
                var offset = System.Math.Sqrt(System.Math.Max(0, square));
                return Hit(projection - offset) || Hit(projection + offset);

                bool Hit(double distance) => distance > Epsilon && distance <= length + Epsilon
                    && OnArc(origin + direction * System.Math.Clamp(distance, 0, length));
            }
            var edge = End - Start;
            var relativeStart = Start - origin;
            var denominator = Cross(direction, edge);
            if (System.Math.Abs(denominator) <= 1e-12 * System.Math.Max(1, Length))
            {
                if (System.Math.Abs(Cross(relativeStart, direction)) > Epsilon)
                    return false;
                var a = Dot(relativeStart, direction);
                var b = Dot(End - origin, direction);
                var low = System.Math.Max(0, System.Math.Min(a, b));
                var high = System.Math.Min(length, System.Math.Max(a, b));
                return high > Epsilon && low <= high + Epsilon;
            }
            var distanceAlongRapid = Cross(relativeStart, edge) / denominator;
            var fractionAlongEdge = Cross(relativeStart, direction) / denominator;
            return distanceAlongRapid > Epsilon && distanceAlongRapid <= length + Epsilon
                && fractionAlongEdge >= -Epsilon / System.Math.Max(Length, Epsilon)
                && fractionAlongEdge <= 1 + Epsilon / System.Math.Max(Length, Epsilon);
        }

        internal bool CrossesRay(Vector point)
        {
            if (Center is not { } center)
                return (Start.Y > point.Y) != (End.Y > point.Y)
                    && Start.X + (point.Y - Start.Y) * (End.X - Start.X) / (End.Y - Start.Y) > point.X;

            // Split arcs at vertical extrema, giving monotone-Y pieces. Apply the same
            // half-open endpoint rule as a polygon ray test, solving X on the native
            // circle. This handles full circles, reversed arcs and shared vertices.
            var breaks = new List<double> { 0, System.Math.Abs(Sweep) };
            foreach (var angle in new[] { System.Math.PI / 2, 3 * System.Math.PI / 2 })
            {
                var travel = Travel(angle);
                if (travel > 0 && travel < System.Math.Abs(Sweep))
                    breaks.Add(travel);
            }
            breaks.Sort();
            var inside = false;
            for (var i = 1; i < breaks.Count; i++)
            {
                var a = StartAngle + System.Math.Sign(Sweep) * breaks[i - 1];
                var b = StartAngle + System.Math.Sign(Sweep) * breaks[i];
                var ya = i == 1 ? Start.Y : center.Y + Radius * System.Math.Sin(a);
                var yb = i == breaks.Count - 1 ? End.Y : center.Y + Radius * System.Math.Sin(b);
                if ((ya > point.Y) == (yb > point.Y))
                    continue;
                var dy = point.Y - center.Y;
                var dx = System.Math.Sqrt(System.Math.Max(0, Radius * Radius - dy * dy));
                var x = center.X + (System.Math.Cos((a + b) / 2) >= 0 ? dx : -dx);
                if (x > point.X)
                    inside = !inside;
            }
            return inside;
        }
    }
}
