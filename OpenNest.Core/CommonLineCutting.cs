using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// One shared cut of a common-line group, cut once on the centerline by <see cref="Owner"/>, the
    /// member of the pair cut first. <see cref="Partner"/> is cut later; its remainder ends in the
    /// kerf this cut opened.
    /// </summary>
    public sealed record CommonLineSharedCut(CommonLineSpan Span, Part Owner, Part Partner);

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

        /// <summary>Every span <paramref name="member"/> shares, whoever cuts it.</summary>
        public IEnumerable<CommonLineSpan> SpansOf(Part member) => Cuts
            .Where(cut => ReferenceEquals(cut.Owner, member) || ReferenceEquals(cut.Partner, member))
            .Select(cut => cut.Span);
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
            var cuts = check.Spans
                .Select(span => Rank(span.First) < Rank(span.Second)
                    ? new CommonLineSharedCut(span, span.First, span.Second)
                    : new CommonLineSharedCut(span, span.Second, span.First))
                .OrderBy(cut => Rank(cut.Owner))
                .ToList();
            return new CommonLineCutOrder(group, order, cuts);
        }

        /// <summary>
        /// A copy of <paramref name="clean"/> (a member's clean program, rotated as placed, in part
        /// coordinates; placed at <paramref name="location"/>) whose perimeter edges are split at
        /// every end of <paramref name="spans"/> and whose shared portions are on the
        /// <see cref="LayerType.Tab"/> layer: material that this member never cuts, because the
        /// shared cut does. The result is absolute. Only straight top-level moves can share.
        /// </summary>
        /// <exception cref="ArgumentException">A span does not lie along the program's edges.</exception>
        public static Program MarkSharedSpans(Program clean, Vector location, IReadOnlyList<CommonLineSpan> spans,
            double kerf)
        {
            ArgumentNullException.ThrowIfNull(clean);
            ArgumentNullException.ThrowIfNull(spans);
            if (!double.IsFinite(kerf) || kerf <= 0)
                throw new ArgumentOutOfRangeException(nameof(kerf));
            var program = (Program)clean.Clone();
            program.Mode = Mode.Absolute;
            var local = spans.Select(span => (Start: span.Start - location, End: span.End - location)).ToArray();
            var covered = new double[local.Length];
            var codes = new List<ICode>();
            Vector? position = null;
            foreach (var code in program.Codes)
            {
                if (code is LinearMove line && position is { } start
                    && line.Layer is LayerType.Cut or LayerType.Display)
                {
                    codes.AddRange(Split(line, start, local, covered, kerf));
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
            for (var i = 0; i < local.Length; i++)
                if (System.Math.Abs(covered[i] - local[i].Start.DistanceTo(local[i].End)) > CommonLineGeometry.GapTolerance)
                    throw new ArgumentException(FormattableString.Invariant(
                        $"The shared span from ({spans[i].Start.X:0.####}, {spans[i].Start.Y:0.####}) to ({spans[i].End.X:0.####}, {spans[i].End.Y:0.####}) does not lie along one straight edge of the part's program."));
            program.Codes.Clear();
            program.Codes.AddRange(codes);
            return program;
        }

        // The line split where shared spans one half kerf off it begin and end; shared pieces on Tab.
        private static IEnumerable<ICode> Split(LinearMove line, Vector start, (Vector Start, Vector End)[] spans,
            double[] covered, double kerf)
        {
            var end = line.EndPoint;
            var length = start.DistanceTo(end);
            if (length <= Math.Tolerance.Epsilon)
                return [line];
            var direction = new Vector((end.X - start.X) / length, (end.Y - start.Y) / length);
            var intervals = new List<(double From, double To)>();
            for (var i = 0; i < spans.Length; i++)
            {
                var (a, b) = spans[i];
                var spanLength = a.DistanceTo(b);
                if (spanLength <= Math.Tolerance.Epsilon)
                    continue;
                var along = new Vector((b.X - a.X) / spanLength, (b.Y - a.Y) / spanLength);
                if (System.Math.Abs(Cross(direction, along)) > 1e-6)
                    continue;
                var offset = System.Math.Abs(Cross(direction, a - start));
                if (System.Math.Abs(offset - kerf / 2) > CommonLineGeometry.GapTolerance)
                    continue;
                var ta = Dot(a - start, direction);
                var tb = Dot(b - start, direction);
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

        private static double Dot(Vector a, Vector b) => a.X * b.X + a.Y * b.Y;

        private static double Cross(Vector a, Vector b) => a.X * b.Y - a.Y * b.X;
    }
}
