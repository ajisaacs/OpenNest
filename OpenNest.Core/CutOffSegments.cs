using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// One placed cutting segment of a cut-off: the part that cuts it, where its cut runs along
    /// the cut-off's axis, and the span of the nominal line it stands for, from the end of the
    /// segment before it (or the line's start) to the start of the one after it (or the line's
    /// end), stopping short of a <see cref="CutOff.Gaps">gap</see> between them. The parts the
    /// nominal line passes through lie in the gaps between segments, so each is inside the span
    /// of the segment on either side of it; a segment is cut before exactly those parts.
    /// </summary>
    public sealed record CutOffSegment(Part Part, double Start, double End, double SpanStart, double SpanEnd);

    /// <summary>
    /// A cut-off's trimmed segments are materialized as one part each, so every segment has its
    /// own place in the cut sequence. This finds those parts and the span each stands for.
    /// </summary>
    public static class CutOffSegments
    {
        /// <summary>The definition's placed segment parts in ascending order along its axis.</summary>
        public static IReadOnlyList<CutOffSegment> Of(Plate plate, CutOff definition) =>
            Of(definition, plate.Parts, plate.BoundingBox(includeParts: false));

        /// <summary>
        /// The definition's segment parts among <paramref name="parts"/> (those placed from its
        /// drawing) in ascending order along its axis, with spans measured against the nominal
        /// line's limits on <paramref name="sheet"/>. A part whose cut cannot be measured stands
        /// for the whole line.
        /// </summary>
        public static IReadOnlyList<CutOffSegment> Of(CutOff definition, IEnumerable<Part> parts, Box sheet)
        {
            var vertical = definition.Axis == CutOffAxis.Vertical;
            var lineStart = definition.StartLimit ?? (vertical ? sheet.Bottom : sheet.Left);
            var lineEnd = definition.EndLimit ?? (vertical ? sheet.Top : sheet.Right);
            var low = System.Math.Min(lineStart, lineEnd);
            var high = System.Math.Max(lineStart, lineEnd);
            var measured = new List<(Part Part, double Start, double End)>();
            var whole = new List<CutOffSegment>();
            foreach (var part in parts)
            {
                if (!ReferenceEquals(part.BaseDrawing, definition.Drawing))
                    continue;
                if (Extent(part, vertical) is { } extent)
                    measured.Add((part, extent.Start, extent.End));
                else
                    whole.Add(new CutOffSegment(part, low, high, low, high));
            }
            measured.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));
            var result = new List<CutOffSegment>(measured.Count + whole.Count);
            for (var i = 0; i < measured.Count; i++)
            {
                var (part, start, end) = measured[i];
                var spanStart = i == 0 ? System.Math.Min(low, start) : measured[i - 1].End;
                var spanEnd = i == measured.Count - 1 ? System.Math.Max(high, end) : measured[i + 1].Start;
                // A gap between two segments belongs to neither: each span stops at its edge.
                foreach (var gap in definition.Gaps)
                {
                    if (i > 0 && gap.End <= start + Math.Tolerance.Epsilon && gap.End > spanStart)
                        spanStart = gap.End;
                    if (i < measured.Count - 1 && gap.Start >= end - Math.Tolerance.Epsilon && gap.Start < spanEnd)
                        spanEnd = gap.Start;
                }
                result.Add(new CutOffSegment(part, start, end, spanStart, spanEnd));
            }
            result.AddRange(whole);
            return result;
        }

        /// <summary>
        /// The extent along the cut-off's axis of the part's cutting moves, or null when the
        /// part cuts nothing or its program is not the absolute rapid-and-cut form cut-offs use.
        /// </summary>
        public static (double Start, double End)? Extent(Part part, bool vertical)
        {
            if (part?.Program == null || part.Program.Mode != Mode.Absolute)
                return null;
            double? min = null, max = null;
            Vector? position = null;
            foreach (var code in part.Program.Codes)
            {
                switch (code)
                {
                    case RapidMove rapid:
                        position = rapid.EndPoint + part.Location;
                        break;
                    case Motion motion when position != null:
                        var end = motion.EndPoint + part.Location;
                        foreach (var point in new[] { position.Value, end })
                        {
                            var along = vertical ? point.Y : point.X;
                            min = min == null ? along : System.Math.Min(min.Value, along);
                            max = max == null ? along : System.Math.Max(max.Value, along);
                        }
                        position = end;
                        break;
                    case Motion:
                        return null;
                }
            }
            return min == null ? null : (min.Value, max.Value);
        }
    }
}
