using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.CommonLines
{
    /// <summary>One shared cut: the centerline between two parts' facing edges, one kerf apart.</summary>
    public readonly record struct CommonLineSpan(Part First, Part Second, Vector Start, Vector End)
    {
        public double Length => Start.DistanceTo(End);
    }

    /// <summary>
    /// One shared cut's path: the spans of one pair chained end to end at corners (a Z or L shared
    /// line), or a single straight span. <see cref="Points"/> runs from the first span's free end
    /// through each corner, where the spans' centerlines meet, to the last span's free end.
    /// </summary>
    public sealed record CommonLinePath(Part First, Part Second, IReadOnlyList<CommonLineSpan> Spans,
        IReadOnlyList<Vector> Points)
    {
        public Vector Start => Points[0];

        public Vector End => Points[^1];

        /// <summary>The kerf-centerline length of the whole path, corners included.</summary>
        public double Length
        {
            get
            {
                var length = 0.0;
                for (var i = 1; i < Points.Count; i++)
                    length += Points[i - 1].DistanceTo(Points[i]);
                return length;
            }
        }
    }

    /// <summary>A member of a group that no longer shares a span, or could not be read.</summary>
    public readonly record struct CommonLineProblem(Part Part, string Message);

    /// <summary>Spans a group's members share now, and what breaks the group.</summary>
    public sealed record CommonLineCheck(IReadOnlyList<CommonLineSpan> Spans, IReadOnlyList<CommonLineProblem> Problems)
    {
        public bool IsIntact => Problems.Count == 0;
    }

    /// <summary>
    /// Derives common-line spans from placed parts' clean perimeters (leads excluded, holes never
    /// share). Two straight perimeter edges share a span when they face each other (antiparallel,
    /// outward normals opposed), lie one kerf apart within <see cref="GapTolerance"/> and overlap
    /// along the line by at least <see cref="MinSharedLength"/>. Arcs never share.
    /// </summary>
    public static class CommonLineGeometry
    {
        /// <summary>How far a shared edge pair's gap may differ from the group's kerf (inches).</summary>
        public const double GapTolerance = 0.0005;

        /// <summary>The shortest overlap that counts as a shared span (inches).</summary>
        public const double MinSharedLength = 0.25;

        private const double ParallelTolerance = 1e-6;

        /// <summary>A placed perimeter edge with its outward unit normal.</summary>
        internal readonly record struct Edge(Vector Start, Vector End, Vector Direction, Vector Outward, double Length);

        /// <summary>A facing edge pair: <see cref="Gap"/> along the first edge's outward normal,
        /// the overlap as distances along the first edge from its start.</summary>
        internal readonly record struct Facing(Edge First, Edge Second, double Gap, double From, double To)
        {
            public double Overlap => To - From;
        }

        /// <summary>The spans <paramref name="first"/> and <paramref name="second"/> share at <paramref name="kerf"/>.</summary>
        public static IReadOnlyList<CommonLineSpan> Spans(Part first, Part second, double kerf)
        {
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            var cache = new ProfileCache();
            return Spans(first, Edges(first, cache), second, Edges(second, cache), kerf);
        }

        /// <summary>
        /// Checks that every member of <paramref name="group"/> shares at least one span with another
        /// member at the group's kerf, and that the members form one connected group.
        /// </summary>
        public static CommonLineCheck Check(CommonLineGroup group)
        {
            ArgumentNullException.ThrowIfNull(group);
            var members = group.Members;
            var cache = new ProfileCache();
            var edges = new IReadOnlyList<Edge>[members.Count];
            var problems = new List<CommonLineProblem>();
            for (var i = 0; i < members.Count; i++)
            {
                try
                {
                    edges[i] = Edges(members[i], cache);
                }
                catch (Exception exception) when (IsInvalid(exception))
                {
                    problems.Add(new(members[i], $"could not be read ({exception.Message})."));
                }
            }
            var spans = new List<CommonLineSpan>();
            var links = members.Select(_ => new List<int>()).ToArray();
            for (var i = 0; i < members.Count; i++)
                for (var j = i + 1; j < members.Count; j++)
                {
                    if (edges[i] == null || edges[j] == null)
                        continue;
                    var shared = Spans(members[i], edges[i], members[j], edges[j], group.Kerf);
                    if (shared.Count == 0)
                        continue;
                    spans.AddRange(shared);
                    links[i].Add(j);
                    links[j].Add(i);
                }
            for (var i = 0; i < members.Count; i++)
                if (edges[i] != null && links[i].Count == 0)
                    problems.Add(new(members[i],
                        $"no longer shares a straight edge one kerf ({group.Kerf:0.####}) apart with another part in its common-line group."));
            if (problems.Count == 0 && !Connected(links))
                problems.Add(new(members[0], "its common-line group has split into separate pieces."));
            return new CommonLineCheck(spans, problems);
        }

        /// <summary>
        /// The move that puts <paramref name="moving"/> one kerf from <paramref name="anchor"/> along
        /// their best facing edge pair: the longest overlap, then the smallest gap. Only pairs closer
        /// than <paramref name="maxGap"/> (or already overlapping by up to that much) are considered;
        /// null when there are none.
        /// </summary>
        public static Vector? JoinOffset(Part anchor, Part moving, double kerf, double maxGap)
        {
            ArgumentNullException.ThrowIfNull(anchor);
            ArgumentNullException.ThrowIfNull(moving);
            if (!double.IsFinite(kerf) || kerf <= 0)
                throw new ArgumentOutOfRangeException(nameof(kerf));
            var cache = new ProfileCache();
            var anchorEdges = Edges(anchor, cache);
            var movingEdges = Edges(moving, cache);
            var best = FacingEdges(anchorEdges, movingEdges, -maxGap, maxGap)
                .OrderByDescending(facing => System.Math.Round(facing.Overlap, 6))
                .ThenBy(facing => System.Math.Abs(facing.Gap - kerf))
                .Cast<Facing?>()
                .FirstOrDefault();
            if (best is not { } chosen)
                return null;
            var shift = kerf - chosen.Gap;
            var offset = new Vector(chosen.First.Outward.X * shift, chosen.First.Outward.Y * shift);
            return Slide(anchorEdges, movingEdges, chosen, offset, kerf, maxGap) ?? offset;
        }

        // The offset slid along the chosen edge pair so that a facing pair across it (the step of a
        // Z or L shared line) lands one kerf apart too, keeping the chosen pair's gap and span; null
        // when no such pair is within reach.
        private static Vector? Slide(IReadOnlyList<Edge> anchor, IReadOnlyList<Edge> moving, Facing chosen,
            Vector offset, double kerf, double maxGap)
        {
            var along = chosen.First.Direction;
            var minimum = PortableMath.Sin(MinCornerTurn);
            var shifted = Shift(moving, offset);
            var slides = FacingEdges(anchor, shifted, -maxGap, maxGap)
                .Select(facing => (Facing: facing, Across: Dot(along, facing.First.Outward)))
                .Where(choice => System.Math.Abs(choice.Across) >= minimum)
                .Select(choice => (choice.Facing, Slide: (kerf - choice.Facing.Gap) / choice.Across))
                .Where(choice => System.Math.Abs(choice.Slide) <= maxGap)
                .OrderByDescending(choice => System.Math.Round(choice.Facing.Overlap, 6))
                .ThenBy(choice => System.Math.Abs(choice.Slide));
            foreach (var (_, slide) in slides)
            {
                var candidate = new Vector(offset.X + along.X * slide, offset.Y + along.Y * slide);
                var placed = Shift(moving, candidate);
                var spans = FacingEdges(anchor, placed, kerf - GapTolerance, kerf + GapTolerance).ToList();
                // The chosen pair must still share, and the slid pair must now share across it.
                if (spans.Any(facing => System.Math.Abs(Cross(facing.First.Direction, along)) <= ParallelTolerance)
                    && spans.Any(facing => System.Math.Abs(Dot(along, facing.First.Outward)) >= minimum))
                    return candidate;
            }
            return null;
        }

        private static IReadOnlyList<Edge> Shift(IReadOnlyList<Edge> edges, Vector offset) =>
            edges.Select(edge => edge with { Start = edge.Start + offset, End = edge.End + offset }).ToArray();

        /// <summary>The sharpest corner a shared path may turn (radians): 135°.</summary>
        public const double MaxCornerTurn = 3 * System.Math.PI / 4;

        /// <summary>The gentlest corner a shared path may turn (radians): 10°. Gentler meets are not corners.</summary>
        public const double MinCornerTurn = System.Math.PI / 18;

        /// <summary>
        /// The shared cuts of <paramref name="spans"/> at <paramref name="kerf"/>: spans of one pair
        /// whose ends meet at a corner (a Z or L shared line) chain into one path; every other span
        /// is a path of its own. Two spans meet when their centerlines cross beyond both ends, each
        /// end short of the crossing by what the kerf leaves there, <c>kerf/2 · tan(turn/2)</c>, and
        /// the path turns between <see cref="MinCornerTurn"/> and <see cref="MaxCornerTurn"/>. Paths
        /// keep the spans' order of first appearance.
        /// </summary>
        public static IReadOnlyList<CommonLinePath> Paths(IReadOnlyList<CommonLineSpan> spans, double kerf)
        {
            ArgumentNullException.ThrowIfNull(spans);
            if (!double.IsFinite(kerf) || kerf <= 0)
                throw new ArgumentOutOfRangeException(nameof(kerf));
            // links[i, end]: the span and end met at span i's end (0 = Start, 1 = End), and the corner.
            var links = new (int Span, int End, Vector Corner)?[spans.Count, 2];
            for (var i = 0; i < spans.Count; i++)
                for (var j = i + 1; j < spans.Count; j++)
                {
                    if (!SamePair(spans[i], spans[j]))
                        continue;
                    for (var ei = 0; ei < 2; ei++)
                        for (var ej = 0; ej < 2; ej++)
                            if (links[i, ei] == null && links[j, ej] == null
                                && Corner(spans[i], ei, spans[j], ej, kerf) is { } corner)
                            {
                                links[i, ei] = (j, ej, corner);
                                links[j, ej] = (i, ei, corner);
                            }
                }
            var used = new bool[spans.Count];
            var paths = new List<CommonLinePath>();
            for (var i = 0; i < spans.Count; i++)
            {
                if (used[i])
                    continue;
                // Walk back to a free end (or round a loop back to this span), then trace forward.
                var (span, entry) = (i, links[i, 0] == null ? 0 : links[i, 1] == null ? 1 : 0);
                for (var guard = 0; guard < spans.Count && links[span, entry] is { } back && back.Span != i; guard++)
                    (span, entry) = (back.Span, 1 - back.End);
                var chain = new List<CommonLineSpan>();
                var points = new List<Vector> { EndOf(spans[span], entry) };
                while (true)
                {
                    used[span] = true;
                    chain.Add(spans[span]);
                    var exit = 1 - entry;
                    if (links[span, exit] is not { } next || used[next.Span])
                    {
                        points.Add(EndOf(spans[span], exit));
                        break;
                    }
                    points.Add(next.Corner);
                    (span, entry) = (next.Span, next.End);
                }
                paths.Add(new CommonLinePath(spans[i].First, spans[i].Second, chain, points));
            }
            return paths;
        }

        // Where span a's end ea meets span b's end eb at a corner, or null.
        private static Vector? Corner(CommonLineSpan a, int ea, CommonLineSpan b, int eb, double kerf)
        {
            var aEnd = EndOf(a, ea);
            var aOther = EndOf(a, 1 - ea);
            var bEnd = EndOf(b, eb);
            var bOther = EndOf(b, 1 - eb);
            var aLength = aEnd.DistanceTo(aOther);
            var bLength = bEnd.DistanceTo(bOther);
            if (aLength <= Math.Tolerance.Epsilon || bLength <= Math.Tolerance.Epsilon)
                return null;
            // Travel along a into its end, then out along b from its end.
            var din = new Vector((aEnd.X - aOther.X) / aLength, (aEnd.Y - aOther.Y) / aLength);
            var dout = new Vector((bOther.X - bEnd.X) / bLength, (bOther.Y - bEnd.Y) / bLength);
            var cross = Cross(din, dout);
            var turn = PortableMath.Atan2(System.Math.Abs(cross), Dot(din, dout));
            if (turn < MinCornerTurn || turn > MaxCornerTurn)
                return null;
            // aEnd + din·s = bEnd − dout·t: the corner lies s past a's end and t before b's end.
            var gap = bEnd - aEnd;
            var s = Cross(gap, dout) / cross;
            var t = Cross(din, gap) / cross;
            var reach = kerf / 2 * PortableMath.Tan(turn / 2) + 2 * GapTolerance;
            if (s < -GapTolerance || t < -GapTolerance || s > reach || t > reach)
                return null;
            return new Vector(aEnd.X + din.X * s, aEnd.Y + din.Y * s);
        }

        private static Vector EndOf(CommonLineSpan span, int end) => end == 0 ? span.Start : span.End;

        private static bool SamePair(CommonLineSpan a, CommonLineSpan b) =>
            (ReferenceEquals(a.First, b.First) && ReferenceEquals(a.Second, b.Second))
            || (ReferenceEquals(a.First, b.Second) && ReferenceEquals(a.Second, b.First));

        private static IReadOnlyList<CommonLineSpan> Spans(Part first, IReadOnlyList<Edge> a, Part second,
            IReadOnlyList<Edge> b, double kerf)
        {
            if (!double.IsFinite(kerf) || kerf <= 0)
                throw new ArgumentOutOfRangeException(nameof(kerf));
            var spans = new List<CommonLineSpan>();
            foreach (var facing in FacingEdges(a, b, kerf - GapTolerance, kerf + GapTolerance))
            {
                var edge = facing.First;
                var half = facing.Gap / 2;
                Vector At(double t) => new(edge.Start.X + edge.Direction.X * t + edge.Outward.X * half,
                    edge.Start.Y + edge.Direction.Y * t + edge.Outward.Y * half);
                spans.Add(new CommonLineSpan(first, second, At(facing.From), At(facing.To)));
            }
            return spans;
        }

        internal static IEnumerable<Facing> FacingEdges(IReadOnlyList<Edge> a, IReadOnlyList<Edge> b,
            double minGap, double maxGap)
        {
            foreach (var first in a)
                foreach (var second in b)
                {
                    // Antiparallel edges with opposed outward normals face each other.
                    if (System.Math.Abs(Cross(first.Direction, second.Direction)) > ParallelTolerance
                        || Dot(first.Outward, second.Outward) > -1 + ParallelTolerance)
                        continue;
                    var gap = Dot(second.Start - first.Start, first.Outward);
                    if (gap < minGap || gap > maxGap)
                        continue;
                    var t0 = Dot(second.Start - first.Start, first.Direction);
                    var t1 = Dot(second.End - first.Start, first.Direction);
                    var from = System.Math.Max(0, System.Math.Min(t0, t1));
                    var to = System.Math.Min(first.Length, System.Math.Max(t0, t1));
                    if (to - from >= MinSharedLength)
                        yield return new Facing(first, second, gap, from, to);
                }
        }

        /// <summary>The straight edges of a part's placed clean perimeter, with outward normals.</summary>
        internal static IReadOnlyList<Edge> Edges(Part part, ProfileCache cache)
        {
            var profile = PartSpacingReview.Profile(part, cache.Profiles)
                ?? throw new ArgumentException("Part has no material.");
            var location = part.Location;
            var ring = profile.Perimeter.ToPolygon();
            var edges = new List<Edge>();
            foreach (var line in profile.Perimeter.Entities.OfType<Line>())
            {
                var start = line.StartPoint + location;
                var end = line.EndPoint + location;
                var length = start.DistanceTo(end);
                if (length < MinSharedLength)
                    continue;
                var direction = new Vector((end.X - start.X) / length, (end.Y - start.Y) / length);
                var outward = new Vector(direction.Y, -direction.X);
                // The probe is in part-local coordinates: the ring is not moved to the location.
                var mid = new Vector((line.StartPoint.X + line.EndPoint.X) / 2, (line.StartPoint.Y + line.EndPoint.Y) / 2);
                var step = System.Math.Min(0.001, length / 100);
                if (Inside(ring.Vertices, new Vector(mid.X + outward.X * step, mid.Y + outward.Y * step)))
                    outward = new Vector(-outward.X, -outward.Y);
                edges.Add(new Edge(start, end, direction, outward, length));
            }
            return edges;
        }

        internal sealed class ProfileCache
        {
            public readonly Dictionary<(Program, long), ShapeProfile> Profiles = new();
        }

        private static bool Connected(IReadOnlyList<List<int>> links)
        {
            var seen = new bool[links.Count];
            var stack = new Stack<int>();
            stack.Push(0);
            seen[0] = true;
            while (stack.Count > 0)
                foreach (var next in links[stack.Pop()])
                    if (!seen[next])
                    {
                        seen[next] = true;
                        stack.Push(next);
                    }
            return seen.All(s => s);
        }

        private static bool Inside(IReadOnlyList<Vector> ring, Vector point)
        {
            var inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Y > point.Y) != (b.Y > point.Y)
                    && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;

        private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;

        private static bool IsInvalid(Exception exception) => exception is
            ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException;
    }
}
