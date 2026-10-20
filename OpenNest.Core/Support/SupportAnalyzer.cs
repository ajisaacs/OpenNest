using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Support
{
    /// <summary>
    /// Decides whether a cut part stays level on its support bed. Plate is of even thickness,
    /// so the part's center of gravity is its area centroid; the part rests on the bed's
    /// contacts under its material and stays level while that center lies inside their convex
    /// hull. The margin is how far inside (positive) or outside (negative) it lies; a part with
    /// fewer than three contacts that are not in line can only balance, so its margin is at
    /// most zero. With a <see cref="SupportBed.RegistrationTolerance"/>, the bed is also
    /// checked shifted by that much along each axis and diagonally, keeping the worst margin.
    /// A perimeter tab holds the part at the tab, so a tab point counts as one more contact.
    /// </summary>
    public static class SupportAnalyzer
    {
        /// <summary>Points sampled along the outline, besides its vertices, to find the best tab point.</summary>
        public const int TabSamples = 256;

        public static PartSupport Analyze(SupportBed bed, int quadrant, Part part) =>
            Analyze(bed, quadrant, SupportMaterial.FromPart(part));

        public static PartSupport Analyze(SupportBed bed, int quadrant, SupportMaterial material)
        {
            ArgumentNullException.ThrowIfNull(bed);
            ArgumentNullException.ThrowIfNull(material);
            if (!material.IsComplete)
                return new PartSupport(material.Reason);
            if (!material.TryGetCenterOfGravity(out var center, out _))
                return new PartSupport("The part encloses no area.");

            var hulls = Shifts(bed.RegistrationTolerance)
                .Select(shift => Hull(bed.Contacts(material, quadrant, shift, extremesOnly: true)))
                .ToArray();
            var margin = hulls.Min(hull => Margin(center, hull));
            var contacts = bed.Contacts(material, quadrant, Vector.Zero);

            Vector? best = null;
            var bestMargin = double.NegativeInfinity;
            foreach (var point in OutlineSamples(material.Outline))
            {
                var withTab = MarginWith(center, hulls, point);
                if (withTab > bestMargin)
                {
                    best = point;
                    bestMargin = withTab;
                }
            }

            return new PartSupport(center, contacts, margin, bed.StabilityMargin, hulls, best, bestMargin);
        }

        /// <summary>The worst margin of <paramref name="center"/> over every shifted hull, each with <paramref name="tab"/> added.</summary>
        internal static double MarginWith(Vector center, IReadOnlyList<Vector[]> hulls, Vector tab)
        {
            var worst = double.PositiveInfinity;
            foreach (var hull in hulls)
            {
                var points = new Vector[hull.Length + 1];
                hull.CopyTo(points, 0);
                points[^1] = tab;
                worst = System.Math.Min(worst, Margin(center, Hull(points)));
            }
            return worst;
        }

        /// <summary>
        /// Signed distance of <paramref name="point"/> inside the convex polygon
        /// <paramref name="hull"/> (counter-clockwise): positive inside, negative outside, and
        /// never positive for a point or segment hull. An empty hull is negative infinity.
        /// </summary>
        internal static double Margin(Vector point, IReadOnlyList<Vector> hull)
        {
            switch (hull.Count)
            {
                case 0:
                    return double.NegativeInfinity;
                case 1:
                    return -point.DistanceTo(hull[0]);
                case 2:
                    return -SegmentDistance(point, hull[0], hull[1]);
            }

            var inside = double.PositiveInfinity;
            var outside = double.PositiveInfinity;
            var isInside = true;
            for (var i = 0; i < hull.Count; i++)
            {
                var a = hull[i];
                var b = hull[(i + 1) % hull.Count];
                var edge = b - a;
                var length = System.Math.Sqrt(edge.X * edge.X + edge.Y * edge.Y);
                var side = (edge.X * (point.Y - a.Y) - edge.Y * (point.X - a.X)) / length;
                if (side < 0)
                    isInside = false;
                inside = System.Math.Min(inside, side);
                outside = System.Math.Min(outside, SegmentDistance(point, a, b));
            }
            return isInside ? inside : -outside;
        }

        /// <summary>The convex hull, counter-clockwise, without repeated or collinear points (monotone chain).</summary>
        internal static Vector[] Hull(IEnumerable<Vector> points)
        {
            var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            if (sorted.Length < 3)
                return sorted;

            var hull = new Vector[sorted.Length * 2];
            var count = 0;
            foreach (var point in sorted)
            {
                while (count >= 2 && Cross(hull[count - 2], hull[count - 1], point) <= 0)
                    count--;
                hull[count++] = point;
            }
            var lower = count + 1;
            for (var i = sorted.Length - 2; i >= 0; i--)
            {
                while (count >= lower && Cross(hull[count - 2], hull[count - 1], sorted[i]) <= 0)
                    count--;
                hull[count++] = sorted[i];
            }
            // The last point repeats the first.
            return hull.Take(count - 1).ToArray();
        }

        private static double Cross(Vector o, Vector a, Vector b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        private static double SegmentDistance(Vector point, Vector a, Vector b)
        {
            var edge = b - a;
            var lengthSquared = edge.X * edge.X + edge.Y * edge.Y;
            if (lengthSquared <= 0)
                return point.DistanceTo(a);
            var t = System.Math.Clamp(((point.X - a.X) * edge.X + (point.Y - a.Y) * edge.Y) / lengthSquared, 0, 1);
            return point.DistanceTo(a + edge * t);
        }

        private static IEnumerable<Vector> Shifts(double tolerance)
        {
            if (tolerance <= 0)
                return new[] { Vector.Zero };
            var steps = new[] { -tolerance, 0, tolerance };
            return steps.SelectMany(x => steps.Select(y => new Vector(x, y)));
        }

        // Every outline vertex, then points spaced evenly along the outline.
        private static IEnumerable<Vector> OutlineSamples(IReadOnlyList<Vector> outline)
        {
            foreach (var vertex in outline)
                yield return vertex;

            var perimeter = 0.0;
            for (var i = 0; i < outline.Count; i++)
                perimeter += outline[i].DistanceTo(outline[(i + 1) % outline.Count]);
            if (!(perimeter > 0))
                yield break;

            var step = perimeter / TabSamples;
            var travelled = 0.0;
            var next = step;
            for (var i = 0; i < outline.Count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outline.Count];
                var length = a.DistanceTo(b);
                while (length > 0 && next < travelled + length)
                {
                    yield return a + (b - a) * ((next - travelled) / length);
                    next += step;
                }
                travelled += length;
            }
        }
    }

    /// <summary>
    /// How one placed part sits on its support bed (world coordinates): see
    /// <see cref="SupportAnalyzer"/>. Immutable.
    /// </summary>
    public sealed class PartSupport
    {
        private readonly IReadOnlyList<Vector[]> hulls = Array.Empty<Vector[]>();

        internal PartSupport(string reason)
        {
            Reason = reason ?? "The part's support could not be analyzed.";
            Contacts = Array.Empty<Vector>();
        }

        internal PartSupport(Vector centerOfGravity, IReadOnlyList<Vector> contacts, double margin,
            double requiredMargin, IReadOnlyList<Vector[]> hulls, Vector? bestTabPoint, double bestTabMargin)
        {
            CenterOfGravity = centerOfGravity;
            Contacts = contacts;
            Margin = margin;
            RequiredMargin = requiredMargin;
            this.hulls = hulls;
            BestTabPoint = bestTabPoint;
            BestTabMargin = bestTabMargin;
        }

        /// <summary>True when the part was analyzed; otherwise <see cref="Reason"/> says why not.</summary>
        public bool IsComplete => Reason == null;

        public string Reason { get; }

        public Vector CenterOfGravity { get; } = Vector.Invalid;

        /// <summary>Every bed contact under the part at the bed's nominal position.</summary>
        public IReadOnlyList<Vector> Contacts { get; }

        /// <summary>The worst signed distance of the center of gravity inside the contacts' hull.</summary>
        public double Margin { get; } = double.NegativeInfinity;

        /// <summary>The bed's <see cref="SupportBed.StabilityMargin"/>.</summary>
        public double RequiredMargin { get; }

        /// <summary>True when the bed alone holds the part level by at least the required margin.</summary>
        public bool IsSupported => IsComplete && Meets(Margin);

        /// <summary>The outline point where one tab steadies the part most, or null when not analyzed.</summary>
        public Vector? BestTabPoint { get; }

        /// <summary>The margin with a tab at <see cref="BestTabPoint"/>.</summary>
        public double BestTabMargin { get; } = double.NegativeInfinity;

        /// <summary>True when a tab at <see cref="BestTabPoint"/> holds the part level by the required margin.</summary>
        public bool TabCanSupport => IsComplete && Meets(BestTabMargin);

        /// <summary>The worst margin with a tab holding the part at <paramref name="tab"/>.</summary>
        public double MarginWith(Vector tab) =>
            IsComplete ? SupportAnalyzer.MarginWith(CenterOfGravity, hulls, tab) : double.NegativeInfinity;

        /// <summary>
        /// Whether a tab at <paramref name="tab"/> is good enough: it holds the part level by the
        /// required margin, or, where no single tab can, it is as good as the best one found.
        /// </summary>
        public bool AcceptsTabAt(Vector tab)
        {
            if (!IsComplete)
                return false;
            var margin = MarginWith(tab);
            return TabCanSupport ? Meets(margin) : margin >= BestTabMargin - Math.Tolerance.Epsilon;
        }

        private bool Meets(double margin) => margin > 0 && margin >= RequiredMargin;
    }
}
