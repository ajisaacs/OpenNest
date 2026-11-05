using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Diagnostics;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>One shared cut: the centerline between two parts' facing edges, one kerf apart.</summary>
    public readonly record struct CommonLineSpan(Part First, Part Second, Vector Start, Vector End)
    {
        public double Length => Start.DistanceTo(End);
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
            var best = FacingEdges(Edges(anchor, cache), Edges(moving, cache), -maxGap, maxGap)
                .OrderByDescending(facing => System.Math.Round(facing.Overlap, 6))
                .ThenBy(facing => System.Math.Abs(facing.Gap - kerf))
                .Cast<Facing?>()
                .FirstOrDefault();
            if (best is not { } chosen)
                return null;
            var shift = kerf - chosen.Gap;
            return new Vector(chosen.First.Outward.X * shift, chosen.First.Outward.Y * shift);
        }

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
