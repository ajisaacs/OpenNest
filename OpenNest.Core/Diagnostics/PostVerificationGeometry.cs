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

    /// <summary>An axis-aligned extent used only to skip queries that cannot meet.</summary>
    internal readonly record struct Extent(double MinX, double MinY, double MaxX, double MaxY)
    {
        /// <summary>No extent: unions with anything give the other extent.</summary>
        internal static Extent None => new(double.PositiveInfinity, double.PositiveInfinity,
            double.NegativeInfinity, double.NegativeInfinity);

        internal Extent Union(Extent other) => new(System.Math.Min(MinX, other.MinX),
            System.Math.Min(MinY, other.MinY), System.Math.Max(MaxX, other.MaxX), System.Math.Max(MaxY, other.MaxY));

        /// <summary>
        /// The gap two extents need before their checks may be skipped: ten times the widest
        /// absolute band any native contact query allows beyond an extent (the 0.00001 bounding-box
        /// allowance of native intersections, the 0.0001 contact reach of tiny arcs).
        /// </summary>
        internal const double ClearMargin = 1e-3;

        /// <summary>
        /// Coordinates up to which extents may be skipped at all. Within it rounding stays far below
        /// <see cref="ClearMargin"/>; beyond it, where rounding of large supports can exceed any fixed
        /// margin, every check runs.
        /// </summary>
        internal const double WellConditionedLimit = 1e6;

        /// <summary>
        /// True only when both extents are finite, lie within <see cref="WellConditionedLimit"/> and
        /// are more than <see cref="ClearMargin"/> apart on some axis, so no native query can count
        /// anything in one as touching or entering the other. Everything else must be checked.
        /// </summary>
        internal bool IsClearOf(Extent other) => IsWellConditioned && other.IsWellConditioned
            && (MaxX + ClearMargin < other.MinX || other.MaxX + ClearMargin < MinX
                || MaxY + ClearMargin < other.MinY || other.MaxY + ClearMargin < MinY);

        // Math.Max propagates NaN and no comparison with NaN holds, so NaN and infinite bounds
        // (including the empty extent's) fail this test too.
        private bool IsWellConditioned =>
            System.Math.Max(System.Math.Max(System.Math.Abs(MinX), System.Math.Abs(MaxX)),
                System.Math.Max(System.Math.Abs(MinY), System.Math.Abs(MaxY))) <= WellConditionedLimit;
    }

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

        // Native entities are freshly allocated; the immutable curve never exposes state.
        internal Entity ToEntity() => Center is { } center
            ? System.Math.Abs(Sweep) >= TwoPi
                ? new Circle(center, Radius)
                : new Arc(center, Radius, Normalize(StartAngle), Normalize(StartAngle + Sweep), Sweep < 0)
            : new Line(Start, End);

        /// <summary>
        /// A conservative axis-aligned extent of everything a native query may count as touching
        /// this curve: for an arc, its whole supporting circle widened by the contact band of
        /// <see cref="ContactAfterStart"/>; for a line, its endpoints. Nonfinite geometry yields NaN
        /// bounds, which no separation test can pass.
        /// </summary>
        internal Extent Extent => Center is { } center
            ? new(center.X - ContactReach, center.Y - ContactReach, center.X + ContactReach, center.Y + ContactReach)
            : new(System.Math.Min(Start.X, End.X), System.Math.Min(Start.Y, End.Y),
                System.Math.Max(Start.X, End.X), System.Math.Max(Start.Y, End.Y));

        internal Vector Midpoint => Center is { } center
            ? new Vector(center.X + Radius * System.Math.Cos(StartAngle + Sweep / 2),
                center.Y + Radius * System.Math.Sin(StartAngle + Sweep / 2))
            : (Start + End) * 0.5;

        internal bool SameSupport(Curve other)
        {
            if (Center is { } center)
                return other.Center is { } c && center.DistanceTo(c) <= Epsilon
                    && System.Math.Abs(Radius - other.Radius) <= Epsilon;
            if (other.Center.HasValue || Length <= Epsilon || other.Length <= Epsilon)
                return false;
            var direction = (End - Start) * (1 / Length);
            return System.Math.Abs(Cross(other.Start - Start, direction)) <= Epsilon
                && System.Math.Abs(Cross(other.End - Start, direction)) <= Epsilon;
        }

        // Directed native arc-length coordinates for contour replay, not collision queries.
        internal bool SameDirection(Curve other) => SameSupport(other)
            && (Center.HasValue ? System.Math.Sign(Sweep) == System.Math.Sign(other.Sweep)
                : Dot(End - Start, other.End - other.Start) > 0);

        internal double DistanceAlong(Vector point) => Center is { } center
            ? point.DistanceTo(Start) <= Epsilon ? 0
                : Travel(System.Math.Atan2(point.Y - center.Y, point.X - center.X)) * Radius
            : Dot(point - Start, (End - Start) * (1 / Length));

        internal Vector PointAtLength(double distance)
        {
            if (distance <= 0) return Start;
            if (distance >= Length) return End;
            return Center is { } center
                ? center + new Vector(System.Math.Cos(StartAngle + System.Math.Sign(Sweep) * distance / Radius),
                    System.Math.Sin(StartAngle + System.Math.Sign(Sweep) * distance / Radius)) * Radius
                : Start + (End - Start) * (distance / Length);
        }

        internal bool Contains(Vector point) => ToEntity().ClosestPointTo(point).DistanceTo(point) <= Epsilon;

        internal IReadOnlyList<Vector> Contacts(Curve other, out bool overlap)
        {
            var entity = ToEntity();
            var candidate = other.ToEntity();
            // Native arc filters discard NaN supporting-circle intersections. Inspect
            // both unfiltered queries first: roundoff can differ by operand direction.
            // Coincident supports have separate overlap handling below.
            if (Center is { } center && other.Center is { } otherCenter && !SameSupport(other))
            {
                var support = new Circle(center, Radius);
                var otherSupport = new Circle(otherCenter, other.Radius);
                support.Intersects(otherSupport, out var forward);
                otherSupport.Intersects(support, out var reverse);
                foreach (var point in forward)
                    Validate(point);
                foreach (var point in reverse)
                    Validate(point);
            }
            List<Vector> points;
            bool intersects;
            switch (candidate)
            {
                case Line line: intersects = entity.Intersects(line, out points); break;
                case Arc arc: intersects = entity.Intersects(arc, out points); break;
                case Circle circle: intersects = entity.Intersects(circle, out points); break;
                default: throw new NotSupportedException("Unsupported native boundary.");
            }
            if (!intersects)
                points.Clear();
            // Coincident circles produce NaNs in the native discrete-contact query.
            // Their support overlap is handled separately, without inventing crossings.
            overlap = SameSupport(other) && (InteriorWitness(Midpoint, other)
                || InteriorWitness(other.Midpoint, this)
                || InteriorWitness(Start, other) || InteriorWitness(End, other)
                || InteriorWitness(other.Start, this) || InteriorWitness(other.End, this));
            if (SameSupport(other))
                points.Clear();
            foreach (var point in points)
                Validate(point);
            var nativeContactCount = points.Count;
            if (Center is { } c && other.Center is { } oc && SameSupport(other)
                && (c.X != oc.X || c.Y != oc.Y || Radius != other.Radius))
                overlap = true; // Nearly coincident supports are uncertain, never clear.
            foreach (var point in new[] { Start, End, other.Start, other.End })
                if (Contains(point) && other.Contains(point)
                    && !points.Exists(p => p.DistanceTo(point) <= Epsilon))
                    points.Add(point);
            // Existing exact line/ray contact semantics guard native queries which
            // suppress very short or nearly parallel intersections. Uncertainty refuses.
            if (Center is null && !SameSupport(other))
            {
                var direction = (End - Start) * (1 / Length);
                if ((other.ContactAfterStart(Start, direction, Length)
                    || other.ContactAfterStart(End, direction * -1, Length)) && nativeContactCount == 0)
                    throw new NotSupportedException("Native contact query is numerically uncertain.");
            }
            if (other.Center is null && Center.HasValue)
            {
                var direction = (other.End - other.Start) * (1 / other.Length);
                if ((ContactAfterStart(other.Start, direction, other.Length)
                    || ContactAfterStart(other.End, direction * -1, other.Length)) && nativeContactCount == 0)
                    throw new NotSupportedException("Native contact query is numerically uncertain.");
            }
            return points;

            static bool InteriorWitness(Vector point, Curve curve) => curve.Contains(point)
                && (curve.Start.DistanceTo(curve.End) <= Epsilon
                    || (point.DistanceTo(curve.Start) > Epsilon && point.DistanceTo(curve.End) > Epsilon));
        }


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

        // How far past r^2 a squared distance from the centre still counts as touching the arc.
        // For small arcs this band reaches well beyond the radius (sqrt(1e-8) = 1e-4 as r -> 0).
        private double ContactSlack => Epsilon * System.Math.Max(1, Radius * 2);

        // The largest distance from the centre that ContactAfterStart can count as contact.
        private double ContactReach => System.Math.Sqrt(Radius * Radius + ContactSlack);

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
                if (square < -ContactSlack)
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
