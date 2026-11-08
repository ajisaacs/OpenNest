using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// One shared cut of a common-line group, cut once on the centerline by <see cref="Owner"/>, the
    /// member of the pair cut first: a straight span, or spans chained at corners (a Z or L shared
    /// line) cut as one open path. <see cref="Partner"/> is cut later; its remainder ends in the
    /// kerf this cut opened.
    /// </summary>
    public sealed record CommonLineSharedCut(CommonLinePath Path, Part Owner, Part Partner);

    /// <summary>
    /// How a common-line group is cut once (see docs/common-line-cutting.md): its members in cutting
    /// order, and each shared cut with the member that cuts it.
    /// </summary>
    public sealed record CommonLineCutOrder(CommonLineGroup Group, IReadOnlyList<Part> Members,
        IReadOnlyList<CommonLineSharedCut> Cuts)
    {
        /// <summary>The shared cuts <paramref name="member"/> cuts.</summary>
        public IEnumerable<CommonLineSharedCut> CutsOwnedBy(Part member) =>
            Cuts.Where(cut => ReferenceEquals(cut.Owner, member));

        /// <summary>Every shared path <paramref name="member"/> shares, whoever cuts it.</summary>
        public IEnumerable<CommonLinePath> PathsOf(Part member) => Cuts
            .Where(cut => ReferenceEquals(cut.Owner, member) || ReferenceEquals(cut.Partner, member))
            .Select(cut => cut.Path);
    }

    /// <summary>
    /// Single-pass common-line cutting: which member cuts each shared span, and the planner's owned
    /// clean perimeter with its shared spans marked as never cut by that member.
    /// </summary>
    public static class CommonLineCutting
    {
        /// <summary>
        /// The cutting order of an intact group: members breadth-first from its first member along
        /// shared spans (ties in member order), each shared cut owned by the member cut first. A
        /// member's remainder therefore only ever ends in kerf cut before it.
        /// </summary>
        /// <exception cref="ArgumentException">The group is not intact.</exception>
        public static CommonLineCutOrder Order(CommonLineGroup group, CommonLineCheck check)
        {
            ArgumentNullException.ThrowIfNull(group);
            ArgumentNullException.ThrowIfNull(check);
            if (!check.IsIntact)
                throw new ArgumentException(check.Problems[0].Message, nameof(check));
            var members = group.Members;
            int IndexOf(Part part) => members.ToList().FindIndex(member => ReferenceEquals(member, part));
            var order = new List<Part>();
            var seen = new bool[members.Count];
            var queue = new Queue<int>();
            queue.Enqueue(0);
            seen[0] = true;
            while (queue.Count > 0)
            {
                var current = members[queue.Dequeue()];
                order.Add(current);
                var neighbors = check.Spans
                    .Select(span => ReferenceEquals(span.First, current) ? span.Second
                        : ReferenceEquals(span.Second, current) ? span.First : null)
                    .Where(part => part != null)
                    .Select(IndexOf)
                    .Distinct()
                    .Order();
                foreach (var next in neighbors)
                    if (!seen[next])
                    {
                        seen[next] = true;
                        queue.Enqueue(next);
                    }
            }
            if (order.Count != members.Count)
                throw new ArgumentException("The common-line group has split into separate pieces.", nameof(check));
            int Rank(Part part) => order.FindIndex(member => ReferenceEquals(member, part));
            var cuts = CommonLineGeometry.Paths(check.Spans, group.Kerf)
                .Select(path => Rank(path.First) < Rank(path.Second)
                    ? new CommonLineSharedCut(path, path.First, path.Second)
                    : new CommonLineSharedCut(path, path.Second, path.First))
                .OrderBy(cut => Rank(cut.Owner))
                .ToList();
            return new CommonLineCutOrder(group, order, cuts);
        }

        /// <summary>
        /// <see cref="MarkSharedSpans(Program, Vector, IReadOnlyList{CommonLinePath}, double)"/> with
        /// every span a straight path of its own.
        /// </summary>
        public static Program MarkSharedSpans(Program clean, Vector location, IReadOnlyList<CommonLineSpan> spans,
            double kerf)
        {
            ArgumentNullException.ThrowIfNull(spans);
            return MarkSharedSpans(clean, location, spans
                .Select(span => new CommonLinePath(span.First, span.Second, [span], [span.Start, span.End]))
                .ToArray(), kerf);
        }

        /// <summary>
        /// A copy of <paramref name="clean"/> (a member's clean program, rotated as placed, in part
        /// coordinates; placed at <paramref name="location"/>) whose perimeter edges are split at
        /// every end of the shared <paramref name="paths"/> and whose shared portions are on the
        /// <see cref="LayerType.Tab"/> layer: material that this member never cuts, because the
        /// shared cut does. At a path's corner each leg's portion runs to the member's own vertex
        /// there, so the legs' portions join. The result is absolute. Only straight top-level
        /// moves can share.
        /// </summary>
        /// <exception cref="ArgumentException">A path's leg does not lie along the program's edges.</exception>
        public static Program MarkSharedSpans(Program clean, Vector location, IReadOnlyList<CommonLinePath> paths,
            double kerf)
        {
            ArgumentNullException.ThrowIfNull(clean);
            ArgumentNullException.ThrowIfNull(paths);
            if (!double.IsFinite(kerf) || kerf <= 0)
                throw new ArgumentOutOfRangeException(nameof(kerf));
            var program = (Program)clean.Clone();
            program.Mode = Mode.Absolute;
            var legs = paths.SelectMany(path => Legs(path, location, kerf)).ToArray();
            var covered = new double[legs.Length];
            var codes = new List<ICode>();
            Vector? position = null;
            foreach (var code in program.Codes)
            {
                if (code is LinearMove line && position is { } start
                    && line.Layer is LayerType.Cut or LayerType.Display)
                {
                    codes.AddRange(Split(line, start, legs, covered, kerf));
                    position = line.EndPoint;
                    continue;
                }
                codes.Add(code);
                position = code switch
                {
                    Motion motion => motion.EndPoint,
                    SubProgramCall => null,
                    _ => position,
                };
            }
            for (var i = 0; i < legs.Length; i++)
            {
                var leg = legs[i];
                // At a corner the member's portion ends at its own vertex, up to the snap off the centerline's.
                var slack = leg.StartSnap + leg.EndSnap + CommonLineGeometry.GapTolerance;
                if (covered[i] <= Math.Tolerance.Epsilon
                    || System.Math.Abs(covered[i] - leg.Start.DistanceTo(leg.End)) > slack)
                {
                    var (a, b) = (leg.Start + location, leg.End + location);
                    throw new ArgumentException(FormattableString.Invariant(
                        $"The shared span from ({a.X:0.####}, {a.Y:0.####}) to ({b.X:0.####}, {b.Y:0.####}) does not lie along one straight edge of the part's program."));
                }
            }
            program.Codes.Clear();
            program.Codes.AddRange(codes);
            return program;
        }

        // One straight leg of a shared path in part coordinates. An end at a corner snaps to the
        // edge's own vertex within kerf/2 · tan(turn/2) of the centerline corner's projection.
        private readonly record struct Leg(Vector Start, Vector End, double StartSnap, double EndSnap);

        private static IEnumerable<Leg> Legs(CommonLinePath path, Vector location, double kerf)
        {
            var points = path.Points.Select(point => point - location).ToArray();
            var snaps = new double[points.Length];
            for (var k = 1; k < points.Length - 1; k++)
            {
                var din = (points[k] - points[k - 1]).Normalize();
                var dout = (points[k + 1] - points[k]).Normalize();
                var turn = System.Math.Atan2(System.Math.Abs(Cross(din, dout)), Dot(din, dout));
                snaps[k] = kerf / 2 * System.Math.Tan(turn / 2) + 2 * CommonLineGeometry.GapTolerance;
            }
            for (var k = 0; k + 1 < points.Length; k++)
                yield return new Leg(points[k], points[k + 1], snaps[k], snaps[k + 1]);
        }

        // The line split where shared legs one half kerf off it begin and end; shared pieces on Tab.
        private static IEnumerable<ICode> Split(LinearMove line, Vector start, Leg[] legs, double[] covered,
            double kerf)
        {
            var end = line.EndPoint;
            var length = start.DistanceTo(end);
            if (length <= Math.Tolerance.Epsilon)
                return [line];
            var direction = new Vector((end.X - start.X) / length, (end.Y - start.Y) / length);
            var intervals = new List<(double From, double To)>();
            for (var i = 0; i < legs.Length; i++)
            {
                var (a, b, snapA, snapB) = legs[i];
                var legLength = a.DistanceTo(b);
                if (legLength <= Math.Tolerance.Epsilon)
                    continue;
                var along = new Vector((b.X - a.X) / legLength, (b.Y - a.Y) / legLength);
                if (System.Math.Abs(Cross(direction, along)) > 1e-6)
                    continue;
                var offset = System.Math.Abs(Cross(direction, a - start));
                if (System.Math.Abs(offset - kerf / 2) > CommonLineGeometry.GapTolerance)
                    continue;
                var ta = Snap(Dot(a - start, direction), snapA, length);
                var tb = Snap(Dot(b - start, direction), snapB, length);
                var from = System.Math.Max(0, System.Math.Min(ta, tb));
                var to = System.Math.Min(length, System.Math.Max(ta, tb));
                if (to - from <= Math.Tolerance.Epsilon)
                    continue;
                // An end within the snap tolerance of a vertex is that vertex: no sliver is left uncut.
                if (from <= Math.Tolerance.Epsilon)
                    from = 0;
                if (length - to <= Math.Tolerance.Epsilon)
                    to = length;
                covered[i] += to - from;
                intervals.Add((from, to));
            }
            if (intervals.Count == 0)
                return [line];
            var pieces = new List<ICode>();
            var at = 0.0;
            foreach (var (from, to) in intervals.OrderBy(interval => interval.From))
            {
                if (from < at - Math.Tolerance.Epsilon)
                    throw new ArgumentException("Shared spans overlap along one edge.");
                if (from - at > Math.Tolerance.Epsilon)
                    pieces.Add(new LinearMove(Point(from)) { Layer = line.Layer });
                pieces.Add(new LinearMove(to >= length ? end : Point(to)) { Layer = LayerType.Tab });
                at = to;
            }
            if (length - at > Math.Tolerance.Epsilon)
                pieces.Add(new LinearMove(end) { Layer = line.Layer });
            else if (pieces[^1] is LinearMove last)
                last.EndPoint = end;
            return pieces;

            Vector Point(double t) => new(start.X + direction.X * t, start.Y + direction.Y * t);
        }

        // A corner end within snap of either end of the edge is that end.
        private static double Snap(double t, double snap, double length)
        {
            if (snap <= 0)
                return t;
            if (System.Math.Abs(t) <= snap)
                return 0;
            if (System.Math.Abs(t - length) <= snap)
                return length;
            return t;
        }

        private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;

        private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
    }
}
