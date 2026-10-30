using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    /// <summary>The limits one cut-off takes when a <see cref="CutOffEdit"/> is applied.</summary>
    public sealed record CutOffLimitChange(CutOff CutOff, double? StartLimit, double? EndLimit);

    /// <summary>
    /// A worked-out trim, extend or swap: the new limits of each cut-off it changes, or why it was
    /// refused. Nothing changes until <see cref="Apply"/>; the caller then regenerates the plate's
    /// cut-offs and places their segments in the cutting order.
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
            }
        }
    }

    /// <summary>
    /// Trims and extends cut-offs against the cut-offs that cross them, by changing only their
    /// start and end limits (see docs/automatic-scrap-cutoffs.md, "Trim and extend"). A trim stops
    /// on the crossing line's centre, inside its kerf; an extend runs to the next crossing line
    /// beyond the current end, or to the sheet edge, which clears the limit.
    /// </summary>
    public static class CutOffTrim
    {
        public const string NotOnPlate = "That cut-off is not on this plate.";
        public const string NothingToTrimTo = "No cut-off crosses this line to trim it to; delete it instead.";
        public const string MiddlePiece = "Trimming the middle piece would split the line in two; trim an end piece instead.";
        public const string TooShort = "Trimming here would leave a line shorter than the minimum segment length.";
        public const string AtSheetEdge = "This end already runs to the sheet edge.";
        public const string NotAJunction = "These cut-offs do not meet with one running through and the other stopping at it.";
        public const string OvercutOff = "The head-locked overcut is off while this line stops short of the far edge.";
        public const string OvercutOn = "The head-locked overcut is back on at the far edge.";

        /// <summary>
        /// Removes the piece of <paramref name="target"/> picked at <paramref name="pick"/>, between
        /// the crossing cut-offs on either side of it (or the line's end): the limit on that side
        /// moves to the crossing line.
        /// </summary>
        public static CutOffEdit Trim(Plate plate, CutOff target, Vector pick, CutOffSettings settings)
        {
            if (!plate.CutOffs.Contains(target))
                return CutOffEdit.Refused(NotOnPlate);
            var (start, end) = NominalSpan(plate, target);
            var at = System.Math.Clamp(Along(target, pick), start, end);
            var crossings = Crossings(plate, target)
                .Where(c => c > start + Tolerance.Epsilon && c < end - Tolerance.Epsilon)
                .ToList();
            if (crossings.Count == 0)
                return CutOffEdit.Refused(NothingToTrimTo);

            var below = crossings.Where(c => c <= at).DefaultIfEmpty(double.NaN).Max();
            var above = crossings.Where(c => c > at).DefaultIfEmpty(double.NaN).Min();
            if (!double.IsNaN(below) && !double.IsNaN(above))
                return CutOffEdit.Refused(MiddlePiece);

            var (newStart, newEnd) = double.IsNaN(above)
                ? (target.StartLimit, (double?)below)
                : ((double?)above, target.EndLimit);
            var remaining = double.IsNaN(above) ? below - start : end - above;
            if (remaining < System.Math.Max(settings?.MinSegmentLength ?? 0, Tolerance.Epsilon))
                return CutOffEdit.Refused(TooShort);
            return Single(plate, target, newStart, newEnd);
        }

        /// <summary>
        /// Lengthens the end of <paramref name="target"/> nearer <paramref name="pick"/> to the next
        /// crossing cut-off beyond it, or to the sheet edge when there is none. A cut-off sitting at
        /// the current end is skipped, so picking again extends one more step.
        /// </summary>
        public static CutOffEdit Extend(Plate plate, CutOff target, Vector pick)
        {
            if (!plate.CutOffs.Contains(target))
                return CutOffEdit.Refused(NotOnPlate);
            var (start, end) = NominalSpan(plate, target);
            var at = Along(target, pick);
            var high = System.Math.Abs(at - end) <= System.Math.Abs(at - start);
            return ExtendEnd(plate, target, high, out var limit)
                ? Single(plate, target, high ? target.StartLimit : limit, high ? limit : target.EndLimit)
                : CutOffEdit.Refused(AtSheetEdge);
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
                ? Single(plate, target, stop, target.EndLimit)
                : Single(plate, target, target.StartLimit, stop);
        }

        /// <summary>
        /// Swaps which of two meeting cut-offs runs through: the one that stopped at the junction
        /// extends past it (as <see cref="Extend"/> does), and the one that ran through is trimmed
        /// there, losing the side away from the plate's origin. Only the origin-side sheet edge is
        /// certain, so the trimmed line keeps starting there.
        /// </summary>
        public static CutOffEdit Swap(Plate plate, CutOff first, CutOff second, CutOffSettings settings)
        {
            if (!plate.CutOffs.Contains(first) || !plate.CutOffs.Contains(second))
                return CutOffEdit.Refused(NotOnPlate);
            if (Junction(plate, first, second) is not var (through, stopper, point))
                return CutOffEdit.Refused(NotAJunction);

            var (start, end) = NominalSpan(plate, through);
            var farHigh = CutOff.FarEdgeIsHigh(through.Axis, plate.Quadrant);
            var remaining = farHigh ? point - start : end - point;
            if (remaining < System.Math.Max(settings?.MinSegmentLength ?? 0, Tolerance.Epsilon))
                return CutOffEdit.Refused(TooShort);
            var throughChange = farHigh
                ? new CutOffLimitChange(through, through.StartLimit, point)
                : new CutOffLimitChange(through, point, through.EndLimit);

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
        /// edges where it has none. Overtravel is not part of the nominal line.
        /// </summary>
        public static (double Start, double End) NominalSpan(Plate plate, CutOff cutOff)
        {
            var sheet = plate.BoundingBox(includeParts: false);
            var vertical = cutOff.Axis == CutOffAxis.Vertical;
            return (cutOff.StartLimit ?? (vertical ? sheet.Bottom : sheet.Left),
                cutOff.EndLimit ?? (vertical ? sheet.Top : sheet.Right));
        }

        private static (CutOff Through, CutOff Stopper, double Point)? Junction(Plate plate, CutOff first, CutOff second)
        {
            if (first == second || first.Axis == second.Axis)
                return null;
            var firstAt = Line(second);
            var secondAt = Line(first);
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

        // Where the perpendicular cut-offs whose lines reach this one cross it, along its axis.
        private static List<double> Crossings(Plate plate, CutOff target)
        {
            var line = Line(target);
            return plate.CutOffs
                .Where(other => other != target && other.Axis != target.Axis)
                .Where(other =>
                {
                    var (start, end) = NominalSpan(plate, other);
                    return Within(line, start, end);
                })
                .Select(Line)
                .ToList();
        }

        private static CutOffEdit Single(Plate plate, CutOff target, double? start, double? end)
        {
            var change = new CutOffLimitChange(target, start, end);
            return CutOffEdit.Of(new[] { change }, Notice(plate, change));
        }

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

        private static bool Within(double value, double start, double end) =>
            value >= start - Tolerance.Epsilon && value <= end + Tolerance.Epsilon;

        private static bool AtEnd(double value, double start, double end) =>
            System.Math.Abs(value - start) <= Tolerance.Epsilon || System.Math.Abs(value - end) <= Tolerance.Epsilon;
    }
}
