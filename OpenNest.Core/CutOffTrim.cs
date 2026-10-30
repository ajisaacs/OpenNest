using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    /// <summary>
    /// The limits and gaps one cut-off takes when a <see cref="CutOffEdit"/> is applied; null
    /// <paramref name="Gaps"/> keeps the cut-off's gaps.
    /// </summary>
    public sealed record CutOffLimitChange(CutOff CutOff, double? StartLimit, double? EndLimit,
        IReadOnlyList<CutOffGap> Gaps = null)
    {
        /// <summary>The gaps the cut-off has once the change is applied.</summary>
        public IReadOnlyList<CutOffGap> NewGaps => Gaps ?? CutOff.Gaps;
    }

    /// <summary>
    /// A worked-out trim, extend or swap: the new limits and gaps of each cut-off it changes, or
    /// why it was refused. Nothing changes until <see cref="Apply"/>; the caller then regenerates
    /// the plate's cut-offs and places their segments in the cutting order.
    /// </summary>
    public sealed class CutOffEdit
    {
        private CutOffEdit(IReadOnlyList<CutOffLimitChange> changes, string refusal, string notice)
        {
            Changes = changes;
            Refusal = refusal;
            Notice = notice;
        }

        public IReadOnlyList<CutOffLimitChange> Changes { get; }

        /// <summary>Why the edit cannot be made; null when it can.</summary>
        public string Refusal { get; }

        /// <summary>Something the operator should know once it is made, such as a head-locked overcut turning off.</summary>
        public string Notice { get; }

        public bool Succeeded => Refusal == null;

        internal static CutOffEdit Refused(string reason) => new(System.Array.Empty<CutOffLimitChange>(), reason, null);

        internal static CutOffEdit Of(IReadOnlyList<CutOffLimitChange> changes, string notice) => new(changes, null, notice);

        public void Apply()
        {
            if (!Succeeded)
                throw new System.InvalidOperationException(Refusal);
            foreach (var change in Changes)
            {
                change.CutOff.StartLimit = change.StartLimit;
                change.CutOff.EndLimit = change.EndLimit;
                if (change.Gaps != null)
                    change.CutOff.Gaps = change.Gaps;
            }
        }
    }

    /// <summary>
    /// Trims and extends cut-offs against the cut-offs that cross them, by changing only their
    /// start and end limits and their gaps (see docs/automatic-scrap-cutoffs.md, "Trim and
    /// extend"). A trim stops on the crossing line's center, inside its kerf; trimming a piece
    /// between two crossing lines leaves a gap from one center to the other. An extend closes the
    /// gap at the picked end, or runs the line's end to the next crossing line beyond it, or to
    /// the sheet edge, which clears the limit.
    /// </summary>
    public static class CutOffTrim
    {
        public const string NotOnPlate = "That cut-off is not on this plate.";
        public const string NothingToTrimTo = "No cut-off crosses this line to trim it to; delete it instead.";
        public const string InGap = "That stretch of the line is already uncut.";
        public const string TooShort = "Trimming here would leave a line shorter than the minimum segment length.";
        public const string AtSheetEdge = "This end already runs to the sheet edge.";
        public const string NotAJunction = "These cut-offs do not meet with one running through and the other stopping at it.";
        public const string GapAtJunction = "A gap in one of these lines meets the junction; extend to close it before swapping.";
        public const string OvercutOff = "The head-locked overcut is off while this line stops short of the far edge.";
        public const string OvercutOn = "The head-locked overcut is back on at the far edge.";

        /// <summary>
        /// Removes the piece of <paramref name="target"/> picked at <paramref name="pick"/>, between
        /// the crossing cut-offs on either side of it, or a gap or the line's end where there is no
        /// crossing on that side. An end piece moves that end's limit; a piece between two crossing
        /// lines becomes a gap, merged with any gap it touches.
        /// </summary>
        public static CutOffEdit Trim(Plate plate, CutOff target, Vector pick, CutOffSettings settings)
        {
            if (!plate.CutOffs.Contains(target))
                return CutOffEdit.Refused(NotOnPlate);
            var (start, end) = NominalSpan(plate, target);
            var at = System.Math.Clamp(Along(target, pick), start, end);
            var pieces = target.Pieces(start, end);
            var piece = pieces.Where(p => at >= p.Start - Tolerance.Epsilon && at <= p.End + Tolerance.Epsilon)
                .Cast<(double Start, double End)?>().FirstOrDefault();
            if (piece is not var (pieceStart, pieceEnd))
                return CutOffEdit.Refused(InGap);

            var crossings = Crossings(plate, target)
                .Where(c => c > pieceStart + Tolerance.Epsilon && c < pieceEnd - Tolerance.Epsilon)
                .ToList();
            var low = crossings.Where(c => c <= at).DefaultIfEmpty(pieceStart).Max();
            var high = crossings.Where(c => c > at).DefaultIfEmpty(pieceEnd).Min();
            if (pieces.Count == 1 && low == start && high == end)
                return CutOffEdit.Refused(NothingToTrimTo);

            var remaining = new List<(double Start, double End)>();
            foreach (var (from, to) in pieces)
            {
                if (from < low)
                    remaining.Add((from, System.Math.Min(to, low)));
                if (to > high)
                    remaining.Add((System.Math.Max(from, high), to));
            }
            var minimum = System.Math.Max(settings?.MinSegmentLength ?? 0, Tolerance.Epsilon);
            if (remaining.Count == 0
                || remaining.Any(p => (p.End == low || p.Start == high) && p.End - p.Start < minimum))
                return CutOffEdit.Refused(TooShort);
            return Single(plate, Change(target, start, end, remaining));
        }

        /// <summary>
        /// Lengthens <paramref name="target"/> at the end of the piece nearer <paramref name="pick"/>:
        /// a gap there closes; the line's own end runs to the next crossing cut-off beyond it, or to
        /// the sheet edge when there is none. A cut-off sitting at the current end is skipped, so
        /// picking again extends one more step. A pick inside a gap closes that gap.
        /// </summary>
        public static CutOffEdit Extend(Plate plate, CutOff target, Vector pick)
        {
            if (!plate.CutOffs.Contains(target))
                return CutOffEdit.Refused(NotOnPlate);
            var (start, end) = NominalSpan(plate, target);
            var at = Along(target, pick);
            var gaps = target.Gaps;
            var inside = gaps.Where(g => at > g.Start && at < g.End).ToList();
            if (inside.Count == 0)
            {
                var pieces = target.Pieces(start, end);
                var piece = pieces.OrderBy(p => Distance(at, p)).First();
                var high = System.Math.Abs(at - piece.End) <= System.Math.Abs(at - piece.Start);
                var edge = high ? piece.End : piece.Start;
                inside = gaps.Where(g => System.Math.Abs((high ? g.Start : g.End) - edge) <= Tolerance.Epsilon).ToList();
                if (inside.Count == 0)
                {
                    return ExtendEnd(plate, target, high, out var limit)
                        ? Single(plate, new CutOffLimitChange(target,
                            high ? target.StartLimit : limit, high ? limit : target.EndLimit))
                        : CutOffEdit.Refused(AtSheetEdge);
                }
            }
            return Single(plate, new CutOffLimitChange(target, target.StartLimit, target.EndLimit,
                gaps.Except(inside).ToList()));
        }

        /// <summary>
        /// Stops a new <paramref name="target"/> at the first crossing cut-off from its start edge,
        /// the origin side unless <paramref name="settings"/> cut toward the origin. It stays full
        /// length, with no changes, when nothing crosses it or the line left would be shorter than
        /// the minimum segment length. <paramref name="target"/> need not be on the plate yet.
        /// </summary>
        public static CutOffEdit StopAtFirstCrossing(Plate plate, CutOff target, CutOffSettings settings)
        {
            var (start, end) = NominalSpan(plate, target);
            var crossings = Crossings(plate, target)
                .Where(c => c > start + Tolerance.Epsilon && c < end - Tolerance.Epsilon)
                .ToList();
            if (crossings.Count == 0)
                return CutOffEdit.Of(System.Array.Empty<CutOffLimitChange>(), null);

            var towardOrigin = settings?.CutDirection == CutDirection.TowardOrigin;
            var startsHigh = towardOrigin == CutOff.FarEdgeIsHigh(target.Axis, plate.Quadrant);
            var stop = startsHigh ? crossings.Max() : crossings.Min();
            var remaining = startsHigh ? end - stop : stop - start;
            if (remaining < System.Math.Max(settings?.MinSegmentLength ?? 0, Tolerance.Epsilon))
                return CutOffEdit.Of(System.Array.Empty<CutOffLimitChange>(), null);
            return startsHigh
                ? Single(plate, new CutOffLimitChange(target, stop, target.EndLimit))
                : Single(plate, new CutOffLimitChange(target, target.StartLimit, stop));
        }

        /// <summary>
        /// Swaps which of two meeting cut-offs runs through: the one that stopped at the junction
        /// extends past it (as <see cref="Extend"/> does), and the one that ran through is trimmed
        /// there, losing the side away from the plate's origin and any gaps on it. Only the
        /// origin-side sheet edge is certain, so the trimmed line keeps starting there. Refused when
        /// a gap in either line meets the junction.
        /// </summary>
        public static CutOffEdit Swap(Plate plate, CutOff first, CutOff second, CutOffSettings settings)
        {
            if (!plate.CutOffs.Contains(first) || !plate.CutOffs.Contains(second))
                return CutOffEdit.Refused(NotOnPlate);
            if (first != second && first.Axis != second.Axis
                && (TouchesGap(first, Line(second)) || TouchesGap(second, Line(first))))
                return CutOffEdit.Refused(GapAtJunction);
            if (Junction(plate, first, second) is not var (through, stopper, point))
                return CutOffEdit.Refused(NotAJunction);

            var (start, end) = NominalSpan(plate, through);
            var farHigh = CutOff.FarEdgeIsHigh(through.Axis, plate.Quadrant);
            var kept = through.Pieces(start, end)
                .Select(p => farHigh ? (p.Start, System.Math.Min(p.End, point)) : (System.Math.Max(p.Start, point), p.End))
                .Where(p => p.Item2 > p.Item1)
                .ToList();
            var adjacent = kept.FirstOrDefault(p => (farHigh ? p.Item2 : p.Item1) == point);
            if (kept.Count == 0
                || adjacent.Item2 - adjacent.Item1 < System.Math.Max(settings?.MinSegmentLength ?? 0, Tolerance.Epsilon))
                return CutOffEdit.Refused(TooShort);
            var throughChange = farHigh
                ? new CutOffLimitChange(through, through.StartLimit, point, GapsBetween(kept))
                : new CutOffLimitChange(through, point, through.EndLimit, GapsBetween(kept));

            var (stopStart, stopEnd) = NominalSpan(plate, stopper);
            var stopperPoint = Line(through);
            var high = System.Math.Abs(stopperPoint - stopEnd) <= System.Math.Abs(stopperPoint - stopStart);
            if (!ExtendEnd(plate, stopper, high, out var limit))
                return CutOffEdit.Refused(NotAJunction);
            var stopperChange = high
                ? new CutOffLimitChange(stopper, stopper.StartLimit, limit)
                : new CutOffLimitChange(stopper, limit, stopper.EndLimit);

            var notices = new[] { Notice(plate, throughChange), Notice(plate, stopperChange) }
                .Where(n => n != null).Distinct().ToList();
            return CutOffEdit.Of(new[] { stopperChange, throughChange },
                notices.Count == 0 ? null : string.Join(" ", notices));
        }

        /// <summary>
        /// The two cut-offs meeting within <paramref name="tolerance"/> of <paramref name="point"/>,
        /// one running through the junction and the other stopping at it; null when there is none.
        /// </summary>
        public static (CutOff Through, CutOff Stopper)? JunctionAt(Plate plate, Vector point, double tolerance)
        {
            if (plate?.CutOffs == null)
                return null;
            (CutOff, CutOff)? best = null;
            var bestDistance = double.MaxValue;
            foreach (var vertical in plate.CutOffs.Where(c => c.Axis == CutOffAxis.Vertical))
                foreach (var horizontal in plate.CutOffs.Where(c => c.Axis == CutOffAxis.Horizontal))
                {
                    if (Junction(plate, vertical, horizontal) is not var (through, stopper, _))
                        continue;
                    var distance = new Vector(vertical.Position.X, horizontal.Position.Y).DistanceTo(point);
                    if (distance <= tolerance && distance < bestDistance)
                    {
                        best = (through, stopper);
                        bestDistance = distance;
                    }
                }
            return best;
        }

        /// <summary>
        /// Where <paramref name="cutOff"/>'s line runs along its axis: its limits, or the sheet
        /// edges where it has none. Overtravel is not part of the nominal line, and neither does it
        /// take out the gaps (see <see cref="CutOff.Pieces(double, double)"/>).
        /// </summary>
        public static (double Start, double End) NominalSpan(Plate plate, CutOff cutOff)
        {
            var sheet = plate.BoundingBox(includeParts: false);
            var vertical = cutOff.Axis == CutOffAxis.Vertical;
            return (cutOff.StartLimit ?? (vertical ? sheet.Bottom : sheet.Left),
                cutOff.EndLimit ?? (vertical ? sheet.Top : sheet.Right));
        }

        /// <summary>
        /// The stretches of the nominal line a change removes and adds, along the axis, in ascending
        /// order: what a preview marks.
        /// </summary>
        public static (IReadOnlyList<(double Start, double End)> Removed, IReadOnlyList<(double Start, double End)> Added)
            Difference(Plate plate, CutOffLimitChange change)
        {
            var cutOff = change.CutOff;
            var sheet = plate.BoundingBox(includeParts: false);
            var vertical = cutOff.Axis == CutOffAxis.Vertical;
            var before = cutOff.Pieces(NominalSpan(plate, cutOff).Start, NominalSpan(plate, cutOff).End);
            var after = CutOff.Pieces(change.StartLimit ?? (vertical ? sheet.Bottom : sheet.Left),
                change.EndLimit ?? (vertical ? sheet.Top : sheet.Right), change.NewGaps);
            return (Subtract(before, after), Subtract(after, before));
        }

        private static List<(double Start, double End)> Subtract(
            IReadOnlyList<(double Start, double End)> from, IReadOnlyList<(double Start, double End)> remove)
        {
            var result = new List<(double Start, double End)>();
            foreach (var piece in from)
            {
                var current = piece.Start;
                foreach (var (start, end) in remove.Where(r => r.End > piece.Start && r.Start < piece.End))
                {
                    if (start > current + Tolerance.Epsilon)
                        result.Add((current, start));
                    current = System.Math.Max(current, end);
                }
                if (piece.End > current + Tolerance.Epsilon)
                    result.Add((current, piece.End));
            }
            return result;
        }

        private static (CutOff Through, CutOff Stopper, double Point)? Junction(Plate plate, CutOff first, CutOff second)
        {
            if (first == second || first.Axis == second.Axis)
                return null;
            var firstAt = Line(second);
            var secondAt = Line(first);
            if (TouchesGap(first, firstAt) || TouchesGap(second, secondAt))
                return null;
            var (firstStart, firstEnd) = NominalSpan(plate, first);
            var (secondStart, secondEnd) = NominalSpan(plate, second);
            if (!Within(firstAt, firstStart, firstEnd) || !Within(secondAt, secondStart, secondEnd))
                return null;
            var firstStops = AtEnd(firstAt, firstStart, firstEnd);
            var secondStops = AtEnd(secondAt, secondStart, secondEnd);
            if (firstStops == secondStops)
                return null;
            return firstStops ? (second, first, secondAt) : (first, second, firstAt);
        }

        // Whether one of the cut-off's gaps contains or ends at the value along its axis.
        private static bool TouchesGap(CutOff cutOff, double along) =>
            cutOff.Gaps.Any(g => Within(along, g.Start, g.End));

        // The limit an end takes when extended: the next crossing line beyond it, or null for the
        // sheet edge. False when that end already runs to the sheet edge.
        private static bool ExtendEnd(Plate plate, CutOff target, bool high, out double? limit)
        {
            limit = null;
            if ((high ? target.EndLimit : target.StartLimit) == null)
                return false;
            var (start, end) = NominalSpan(plate, target);
            var crossings = Crossings(plate, target);
            if (high)
            {
                var next = crossings.Where(c => c > end + Tolerance.Epsilon).DefaultIfEmpty(double.NaN).Min();
                limit = double.IsNaN(next) ? null : next;
            }
            else
            {
                var next = crossings.Where(c => c < start - Tolerance.Epsilon).DefaultIfEmpty(double.NaN).Max();
                limit = double.IsNaN(next) ? null : next;
            }
            return true;
        }

        // Where the perpendicular cut-offs whose cut pieces reach this one cross it, along its axis.
        private static List<double> Crossings(Plate plate, CutOff target)
        {
            var line = Line(target);
            return plate.CutOffs
                .Where(other => other != target && other.Axis != target.Axis)
                .Where(other =>
                {
                    var (start, end) = NominalSpan(plate, other);
                    return other.Pieces(start, end).Any(p => Within(line, p.Start, p.End));
                })
                .Select(Line)
                .ToList();
        }

        // The change that leaves exactly the given pieces of the nominal line from start to end: a
        // limit stays null while its end stays at the sheet edge.
        private static CutOffLimitChange Change(CutOff target, double start, double end,
            IReadOnlyList<(double Start, double End)> pieces)
        {
            var first = pieces[0].Start;
            var last = pieces[^1].End;
            return new CutOffLimitChange(target,
                target.StartLimit == null && first == start ? null : first,
                target.EndLimit == null && last == end ? null : last,
                GapsBetween(pieces));
        }

        private static List<CutOffGap> GapsBetween(IReadOnlyList<(double Start, double End)> pieces)
        {
            var gaps = new List<CutOffGap>();
            for (var i = 1; i < pieces.Count; i++)
                if (pieces[i].Start > pieces[i - 1].End)
                    gaps.Add(new CutOffGap(pieces[i - 1].End, pieces[i].Start));
            return gaps;
        }

        private static CutOffEdit Single(Plate plate, CutOffLimitChange change) =>
            CutOffEdit.Of(new[] { change }, Notice(plate, change));

        private static string Notice(Plate plate, CutOffLimitChange change)
        {
            var cutOff = change.CutOff;
            if (!(cutOff.HeadLockOvercut > 0))
                return null;
            var high = CutOff.FarEdgeIsHigh(cutOff.Axis, plate.Quadrant);
            var before = (high ? cutOff.EndLimit : cutOff.StartLimit) == null;
            var after = (high ? change.EndLimit : change.StartLimit) == null;
            return before == after ? null : after ? OvercutOn : OvercutOff;
        }

        // The cut-off's own coordinate across its axis: X for a vertical line, Y for a horizontal one.
        private static double Line(CutOff cutOff) =>
            cutOff.Axis == CutOffAxis.Vertical ? cutOff.Position.X : cutOff.Position.Y;

        private static double Along(CutOff cutOff, Vector point) =>
            cutOff.Axis == CutOffAxis.Vertical ? point.Y : point.X;

        private static double Distance(double value, (double Start, double End) piece) =>
            value < piece.Start ? piece.Start - value : value > piece.End ? value - piece.End : 0;

        private static bool Within(double value, double start, double end) =>
            value >= start - Tolerance.Epsilon && value <= end + Tolerance.Epsilon;

        private static bool AtEnd(double value, double start, double end) =>
            System.Math.Abs(value - start) <= Tolerance.Epsilon || System.Math.Abs(value - end) <= Tolerance.Epsilon;
    }
}
