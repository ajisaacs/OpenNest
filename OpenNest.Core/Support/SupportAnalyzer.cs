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
    /// A tab never sits in an inside corner, where the operator cannot grind its nub flush.
    /// </summary>
    public static class SupportAnalyzer
    {
        /// <summary>Points sampled along the outline, besides its vertices, to find the best tab point.</summary>
        public const int TabSamples = 256;

        /// <summary>
        /// The least the outline must turn inward, in radians, for an inside corner; a run of
        /// inward-turning vertices (a chorded fillet) counts as one corner by its whole turn.
        /// </summary>
        public const double InsideCornerAngle = System.Math.PI / 6;

        /// <summary>How far a tab of size <paramref name="tabSize"/> keeps from an inside corner: its uncut span and as much again.</summary>
        public static double InsideCornerClearance(double tabSize) => tabSize > 0 ? 2 * tabSize : 0;

        /// <param name="insideCornerClearance">
        /// How far a tab point keeps from an inside corner; 0 keeps it off the corner only.
        /// </param>
        public static PartSupport Analyze(SupportBed bed, int quadrant, Part part, double insideCornerClearance = 0) =>
            Analyze(bed, quadrant, SupportMaterial.FromPart(part), insideCornerClearance);

        /// <param name="insideCornerClearance">
        /// How far a tab point keeps from an inside corner; 0 keeps it off the corner only.
        /// </param>
        public static PartSupport Analyze(SupportBed bed, int quadrant, SupportMaterial material,
            double insideCornerClearance = 0)
        {
            ArgumentNullException.ThrowIfNull(bed);
            ArgumentNullException.ThrowIfNull(material);
            if (!(insideCornerClearance >= 0) || double.IsInfinity(insideCornerClearance))
                throw new ArgumentOutOfRangeException(nameof(insideCornerClearance));
            if (!material.IsComplete)
                return new PartSupport(material.Reason);
            if (!material.TryGetCenterOfGravity(out var center, out _))
                return new PartSupport("The part encloses no area.");

            var hulls = Shifts(bed.RegistrationTolerance)
                .Select(shift => Hull(bed.Contacts(material, quadrant, shift, extremesOnly: true)))
                .ToArray();
            var margin = hulls.Min(hull => Margin(center, hull));
            var contacts = bed.Contacts(material, quadrant, Vector.Zero);

            var corners = InsideCorners(material.Outline);
            var best = BestTabPoint(material.Outline, center, corners, insideCornerClearance,
                point => MarginWith(center, hulls, point), out var bestMargin);

            return new PartSupport(center, contacts, margin, bed.StabilityMargin, hulls, best, bestMargin,
                corners, insideCornerClearance);
        }

        /// <summary>
        /// A part on a plate with no support bed, tabbed for its <see cref="Width"/> instead
        /// (<see cref="CNC.CuttingStrategy.CuttingParameters.TabsWithoutBedAt"/>). With no contacts
        /// to judge by, a tab anywhere clear of inside corners is accepted; the best tab point is
        /// the clear outline point nearest the line through the center of gravity across the
        /// outline's long side, then the one farthest from an inside corner.
        /// </summary>
        /// <param name="insideCornerClearance">
        /// How far a tab point keeps from an inside corner; 0 keeps it off the corner only.
        /// </param>
        public static PartSupport AnalyzeWithoutBed(SupportMaterial material, double insideCornerClearance = 0)
        {
            ArgumentNullException.ThrowIfNull(material);
            if (!(insideCornerClearance >= 0) || double.IsInfinity(insideCornerClearance))
                throw new ArgumentOutOfRangeException(nameof(insideCornerClearance));
            if (!material.IsComplete)
                return new PartSupport(material.Reason);
            if (!material.TryGetCenterOfGravity(out var center, out _))
                return new PartSupport("The part encloses no area.");

            var corners = InsideCorners(material.Outline);
            var best = BestTabPoint(material.Outline, center, corners, insideCornerClearance,
                _ => double.PositiveInfinity, out _);
            return new PartSupport(center, best, corners, insideCornerClearance, Width(material));
        }

        /// <summary>
        /// The part's width: the short side of its outline's minimum-area bounding rectangle, or
        /// NaN when its material could not be read.
        /// </summary>
        public static double Width(SupportMaterial material)
        {
            ArgumentNullException.ThrowIfNull(material);
            if (!material.IsComplete || material.Outline.Count < 3)
                return double.NaN;
            var box = BoundingRectangle(material.Outline);
            return System.Math.Min(box.Width, box.Height);
        }

        /// <summary>
        /// The part's length: the long side of its outline's minimum-area bounding rectangle, or
        /// NaN when its material could not be read.
        /// </summary>
        public static double Length(SupportMaterial material)
        {
            ArgumentNullException.ThrowIfNull(material);
            if (!material.IsComplete || material.Outline.Count < 3)
                return double.NaN;
            var box = BoundingRectangle(material.Outline);
            return System.Math.Max(box.Width, box.Height);
        }

        /// <summary>
        /// <paramref name="support"/>, a bed analysis of a part the bed cannot hold level, for a
        /// part short enough for its tab alone to hold
        /// (<see cref="CNC.CuttingStrategy.CuttingParameters.TabHoldsAt"/>): its contacts and
        /// margin are kept, any tab clear of inside corners steadies it, and the best tab point is
        /// chosen as without a bed (<see cref="AnalyzeWithoutBed"/>).
        /// </summary>
        public static PartSupport HeldByTab(PartSupport support, SupportMaterial material)
        {
            ArgumentNullException.ThrowIfNull(support);
            ArgumentNullException.ThrowIfNull(material);
            if (!support.IsComplete || support.IsSupported || !support.HasBed)
                return support;
            var best = BestTabPoint(material.Outline, support.CenterOfGravity, support.InsideCorners,
                support.InsideCornerClearance, _ => double.PositiveInfinity, out _);
            return support.WithTabHold(best, Length(material));
        }

        // The outline point a tab steadies the part most from, by `score`, clear of every inside
        // corner where the outline allows it. A tab cannot lift the margin past the contacts' own
        // hull edges, so a whole stretch of outline often ties; of those, the one nearest the line
        // through the center across the outline's long side, then the one farthest from an inside
        // corner, or the first.
        private static Vector? BestTabPoint(IReadOnlyList<Vector> outline, Vector center, IReadOnlyList<Vector> corners,
            double insideCornerClearance, Func<Vector, double> score, out double bestMargin)
        {
            var samples = OutlineSamples(outline)
                .Select(point => (Point: point, Clear: Clearance(corners, point)))
                .ToList();
            var allowed = samples.Where(s => IsClear(s.Clear, insideCornerClearance)).ToList();
            if (allowed.Count > 0)
                samples = allowed;
            var scored = samples.Select(s => (s.Point, s.Clear, Margin: score(s.Point))).ToList();

            bestMargin = scored.Count == 0 ? double.NegativeInfinity : scored.Max(s => s.Margin);
            var along = scored.Count == 0 ? Vector.Zero : LongSide(outline);
            Vector? best = null;
            var bestOffset = double.PositiveInfinity;
            var bestClear = double.NegativeInfinity;
            foreach (var sample in scored)
            {
                if (sample.Margin < bestMargin - Math.Tolerance.Epsilon)
                    continue;
                var offset = System.Math.Abs((sample.Point - center).DotProduct(along));
                if (offset < bestOffset - Math.Tolerance.Epsilon
                    || (offset <= bestOffset + Math.Tolerance.Epsilon && sample.Clear > bestClear))
                {
                    best = sample.Point;
                    bestOffset = offset;
                    bestClear = sample.Clear;
                }
            }
            return best;
        }

        /// <summary>The unit direction of the longer side of the outline's minimum-area bounding rectangle.</summary>
        private static Vector LongSide(IReadOnlyList<Vector> outline)
        {
            var box = BoundingRectangle(outline);
            var angle = box.Width >= box.Height ? box.Angle : box.Angle + Math.Angle.HalfPI;
            return new Vector(System.Math.Cos(angle), System.Math.Sin(angle));
        }

        // The full angle range checks every hull edge.
        private static BoundingRectangleResult BoundingRectangle(IReadOnlyList<Vector> outline) =>
            RotatingCalipers.MinimumBoundingRectangle(ConvexHull.Compute(outline.ToList()), 0, Math.Angle.TwoPI);

        // A point `clear` from the nearest inside corner keeps `clearance` from it; with no
        // clearance it must still not be the corner.
        internal static bool IsClear(double clear, double clearance) =>
            clearance > 0 ? clear >= clearance : clear > Math.Tolerance.Epsilon;

        internal static double Clearance(IReadOnlyList<Vector> corners, Vector point)
        {
            var clear = double.PositiveInfinity;
            foreach (var corner in corners)
                clear = System.Math.Min(clear, corner.DistanceTo(point));
            return clear;
        }

        /// <summary>
        /// The outline vertices that lie in an inside corner: each run of consecutive vertices
        /// turning inward whose turns add up to at least <see cref="InsideCornerAngle"/>.
        /// </summary>
        internal static Vector[] InsideCorners(IReadOnlyList<Vector> outline)
        {
            var count = outline.Count;
            if (count < 3)
                return Array.Empty<Vector>();
            var area = 0.0;
            for (var i = 0; i < count; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % count];
                area += a.X * b.Y - b.X * a.Y;
            }
            if (area == 0)
                return Array.Empty<Vector>();

            // Signed turn at each vertex: negative turns inward, whatever the outline's winding.
            var turns = new double[count];
            for (var i = 0; i < count; i++)
            {
                var into = outline[i] - outline[(i + count - 1) % count];
                var outOf = outline[(i + 1) % count] - outline[i];
                var cross = into.X * outOf.Y - into.Y * outOf.X;
                var dot = into.X * outOf.X + into.Y * outOf.Y;
                turns[i] = System.Math.Atan2(cross, dot) * System.Math.Sign(area);
            }

            // Start after a vertex that does not turn inward so no run is split by the wrap.
            var start = Array.FindIndex(turns, turn => turn >= 0);
            if (start < 0)
                return Array.Empty<Vector>();
            var corners = new List<Vector>();
            var run = new List<Vector>();
            var turned = 0.0;
            for (var step = 1; step <= count; step++)
            {
                var i = (start + step) % count;
                if (turns[i] < 0)
                {
                    run.Add(outline[i]);
                    turned -= turns[i];
                    continue;
                }
                if (turned >= InsideCornerAngle - Math.Tolerance.Epsilon)
                    corners.AddRange(run);
                run.Clear();
                turned = 0;
            }
            return corners.ToArray();
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
            double requiredMargin, IReadOnlyList<Vector[]> hulls, Vector? bestTabPoint, double bestTabMargin,
            IReadOnlyList<Vector> insideCorners, double insideCornerClearance)
        {
            CenterOfGravity = centerOfGravity;
            Contacts = contacts;
            Margin = margin;
            RequiredMargin = requiredMargin;
            this.hulls = hulls;
            BestTabPoint = bestTabPoint;
            BestTabMargin = bestTabMargin;
            InsideCorners = insideCorners;
            InsideCornerClearance = insideCornerClearance;
        }

        // A part on a plate with no bed, tabbed for its width: it rests on nothing modeled, and a
        // tab anywhere clear of inside corners is as good as any other.
        // A copy of `source` held by its tab alone: it keeps its contacts and margin, and any tab
        // clear of inside corners steadies it.
        private PartSupport(PartSupport source, Vector? bestTabPoint, double length)
        {
            CenterOfGravity = source.CenterOfGravity;
            Contacts = source.Contacts;
            Margin = source.Margin;
            RequiredMargin = source.RequiredMargin;
            hulls = source.hulls;
            InsideCorners = source.InsideCorners;
            InsideCornerClearance = source.InsideCornerClearance;
            BestTabPoint = bestTabPoint;
            BestTabMargin = double.PositiveInfinity;
            IsHeldByTab = true;
            Length = length;
        }

        internal PartSupport WithTabHold(Vector? bestTabPoint, double length) => new(this, bestTabPoint, length);

        internal PartSupport(Vector centerOfGravity, Vector? bestTabPoint, IReadOnlyList<Vector> insideCorners,
            double insideCornerClearance, double width)
        {
            HasBed = false;
            CenterOfGravity = centerOfGravity;
            Contacts = Array.Empty<Vector>();
            BestTabPoint = bestTabPoint;
            BestTabMargin = double.PositiveInfinity;
            InsideCorners = insideCorners;
            InsideCornerClearance = insideCornerClearance;
            Width = width;
        }

        /// <summary>
        /// False for a part on a plate with no support bed, tabbed for its <see cref="Width"/>
        /// (<see cref="SupportAnalyzer.AnalyzeWithoutBed"/>): it has no contacts, never counts as
        /// supported, and any tab clear of inside corners steadies it.
        /// </summary>
        public bool HasBed { get; } = true;

        /// <summary>
        /// True for a part on a bed that cannot hold it level but short enough for its tab alone
        /// to hold (<see cref="SupportAnalyzer.HeldByTab"/>): any tab clear of inside corners
        /// steadies it.
        /// </summary>
        public bool IsHeldByTab { get; }

        /// <summary>The part's length when <see cref="IsHeldByTab"/> (see <see cref="SupportAnalyzer.Length"/>); NaN otherwise.</summary>
        public double Length { get; } = double.NaN;

        /// <summary>The part's width when analyzed without a bed (see <see cref="SupportAnalyzer.Width"/>); NaN otherwise.</summary>
        public double Width { get; } = double.NaN;

        /// <summary>True when the part was analyzed; otherwise <see cref="Reason"/> says why not.</summary>
        public bool IsComplete => Reason == null;

        public string Reason { get; }

        public Vector CenterOfGravity { get; } = Vector.Invalid;

        /// <summary>Every bed contact under the part at the bed's nominal position.</summary>
        public IReadOnlyList<Vector> Contacts { get; }

        /// <summary>The outline vertices in an inside corner (see <see cref="SupportAnalyzer.InsideCorners"/>).</summary>
        public IReadOnlyList<Vector> InsideCorners { get; } = Array.Empty<Vector>();

        /// <summary>How far an accepted tab point keeps from an inside corner.</summary>
        public double InsideCornerClearance { get; }

        /// <summary>The worst signed distance of the center of gravity inside the contacts' hull.</summary>
        public double Margin { get; } = double.NegativeInfinity;

        /// <summary>The bed's <see cref="SupportBed.StabilityMargin"/>.</summary>
        public double RequiredMargin { get; }

        /// <summary>True when the bed alone holds the part level by at least the required margin.</summary>
        public bool IsSupported => IsComplete && Meets(Margin);

        /// <summary>
        /// The outline point clear of inside corners where one tab steadies the part most, or
        /// null when not analyzed. Of equally good points, the one farthest from an inside corner.
        /// </summary>
        public Vector? BestTabPoint { get; }

        /// <summary>The margin with a tab at <see cref="BestTabPoint"/>.</summary>
        public double BestTabMargin { get; } = double.NegativeInfinity;

        /// <summary>True when a tab at <see cref="BestTabPoint"/> holds the part level by the required margin.</summary>
        public bool TabCanSupport => IsComplete && Meets(BestTabMargin);

        /// <summary>The worst margin with a tab holding the part at <paramref name="tab"/>.</summary>
        public double MarginWith(Vector tab) =>
            !IsComplete ? double.NegativeInfinity
            : !HasBed || IsHeldByTab ? double.PositiveInfinity
            : SupportAnalyzer.MarginWith(CenterOfGravity, hulls, tab);

        /// <summary>True when a tab at <paramref name="tab"/> holds the part level by the required margin.</summary>
        public bool IsSteadiedByTabAt(Vector tab) => IsComplete && Meets(MarginWith(tab));

        /// <summary>
        /// Whether a tab at <paramref name="tab"/> is good enough: it keeps clear of inside corners,
        /// and it holds the part level by the required margin or, where no single tab can, it is
        /// as good as the best one found.
        /// </summary>
        public bool AcceptsTabAt(Vector tab)
        {
            if (!IsComplete || !IsClearOfInsideCorners(tab))
                return false;
            var margin = MarginWith(tab);
            return TabCanSupport ? Meets(margin) : margin >= BestTabMargin - Math.Tolerance.Epsilon;
        }

        /// <summary>Whether a tab at <paramref name="tab"/> holds the part level by the required margin.</summary>
        public bool HoldsLevelWithTabAt(Vector tab) => IsComplete && Meets(MarginWith(tab));

        /// <summary>Whether <paramref name="point"/> keeps <see cref="InsideCornerClearance"/> from every inside corner.</summary>
        public bool IsClearOfInsideCorners(Vector point) =>
            SupportAnalyzer.IsClear(SupportAnalyzer.Clearance(InsideCorners, point), InsideCornerClearance);

        private bool Meets(double margin) => margin > 0 && margin >= RequiredMargin;
    }
}
