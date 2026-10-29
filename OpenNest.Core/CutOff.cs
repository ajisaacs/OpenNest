using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest
{
    public enum CutOffAxis
    {
        Horizontal,
        Vertical,
    }

    /// <summary>A stretch of a cut-off's line, along its axis, left uncut.</summary>
    public readonly record struct CutOffGap(double Start, double End);

    public class CutOff
    {
        private const double OffsetTolerance = 0.001;
        private IReadOnlyList<CutOffGap> gaps = System.Array.Empty<CutOffGap>();

        public Vector Position { get; set; }
        public CutOffAxis Axis { get; set; }
        public double? StartLimit { get; set; }
        public double? EndLimit { get; set; }
        public Drawing Drawing { get; private set; }

        /// <summary>
        /// Stretches between the limits left uncut, so the line splits into pieces as it does
        /// around a part; a trim of a middle piece makes one from crossing line to crossing line
        /// (<see cref="CutOffTrim"/>). Kept sorted along the axis, with touching gaps merged and
        /// empty or nonfinite ones dropped.
        /// </summary>
        public IReadOnlyList<CutOffGap> Gaps
        {
            get => gaps;
            set => gaps = Normalize(value);
        }

        /// <summary>
        /// The nominal line from <paramref name="start"/> to <paramref name="end"/> with its gaps
        /// taken out, in ascending order: the pieces a consumer of the limits treats as cut.
        /// </summary>
        public IReadOnlyList<(double Start, double End)> Pieces(double start, double end) =>
            Pieces(start, end, gaps);

        internal static IReadOnlyList<(double Start, double End)> Pieces(double start, double end,
            IReadOnlyList<CutOffGap> gaps)
        {
            var (low, high) = (System.Math.Min(start, end), System.Math.Max(start, end));
            var pieces = new List<(double Start, double End)>();
            if (gaps.Count == 0)
            {
                pieces.Add((low, high));
                return pieces;
            }
            var current = low;
            foreach (var gap in gaps)
            {
                if (gap.End <= current || gap.Start >= high)
                    continue;
                if (gap.Start > current)
                    pieces.Add((current, gap.Start));
                current = System.Math.Max(current, gap.End);
            }
            if (current < high)
                pieces.Add((current, high));
            return pieces;
        }

        private static IReadOnlyList<CutOffGap> Normalize(IEnumerable<CutOffGap> value)
        {
            var sorted = (value ?? Enumerable.Empty<CutOffGap>())
                .Where(g => double.IsFinite(g.Start) && double.IsFinite(g.End) && g.End > g.Start)
                .OrderBy(g => g.Start)
                .ToList();
            var merged = new List<CutOffGap>();
            foreach (var gap in sorted)
            {
                if (merged.Count > 0 && gap.Start <= merged[^1].End + Math.Tolerance.Epsilon)
                    merged[^1] = merged[^1] with { End = System.Math.Max(merged[^1].End, gap.End) };
                else
                    merged.Add(gap);
            }
            return merged.Count == 0 ? System.Array.Empty<CutOffGap>() : merged.AsReadOnly();
        }

        /// <summary>
        /// How far past the nominal far sheet edge (the edge away from the plate's origin) the
        /// segment reaching that edge cuts with the head height locked; zero for none. Stock can
        /// arrive wider than nominal, so only the origin-side edge is certain: the overcut cuts the
        /// oversize off. It replaces <see cref="CutOffSettings.Overtravel"/> at the far edge and
        /// applies only when the line runs to that edge (no limit on that side).
        /// </summary>
        public double HeadLockOvercut { get; set; }

        /// <summary>
        /// Whether the segment reaching the far edge is cut from it toward the origin: it starts at
        /// the nominal far edge, overcuts outward with the head locked, rapids back with the head
        /// still locked, unlocks and cuts toward the origin. Ignored without a head-locked overcut,
        /// since nothing else may start a cut at an edge whose position is unknown.
        /// </summary>
        public bool FarEdgeFirst { get; set; }

        public CutOff(Vector position, CutOffAxis axis)
        {
            Position = position;
            Axis = axis;
            Drawing = new Drawing(GetName()) { IsCutOff = true };
        }

        public void Regenerate(
            Plate plate,
            CutOffSettings settings,
            Dictionary<Part, Entity> cache = null
        )
        {
            var segments = ComputeSegments(plate, settings, cache);
            var program = BuildProgram(segments, settings, LockedFarEdge(plate));
            Drawing.Program = program;
        }

        /// <summary>
        /// The regenerated program split into its cutting segments, one rapid-and-cut program
        /// each in program order, so the plate can sequence every segment on its own. A rapid with
        /// the head height locked returns along the overcut and stays in its segment.
        /// </summary>
        public IReadOnlyList<Program> SegmentPrograms()
        {
            var program = Drawing.Program;
            var result = new List<Program>();
            if (program == null)
                return result;
            Program current = null;
            var locked = false;
            foreach (var code in program.Codes)
            {
                if (code is RapidMove && !locked || current == null)
                {
                    current = new Program(program.Mode);
                    result.Add(current);
                }
                if (HeadHeightLock.IsLock(code))
                    locked = true;
                else if (HeadHeightLock.IsUnlock(code))
                    locked = false;
                current.Codes.Add(code);
            }
            return result;
        }

        /// <summary>
        /// Whether the far edge, along this axis on <paramref name="plate"/>, is the high
        /// coordinate: the sheet runs from the origin toward positive coordinates on that axis.
        /// </summary>
        internal static bool FarEdgeIsHigh(CutOffAxis axis, int quadrant) =>
            axis == CutOffAxis.Vertical ? quadrant is 1 or 2 : quadrant is 1 or 4;

        /// <summary>
        /// The nominal far edge this cut-off overcuts with the head locked, along its axis, or null
        /// when it has no head-locked overcut or a limit stops it short of that edge.
        /// </summary>
        private (double Edge, bool High)? LockedFarEdge(Plate plate)
        {
            if (!(HeadLockOvercut > 0) || !double.IsFinite(HeadLockOvercut))
                return null;
            var bounds = plate.BoundingBox(includeParts: false);
            var high = FarEdgeIsHigh(Axis, plate.Quadrant);
            if ((high ? EndLimit : StartLimit) != null)
                return null;
            var vertical = Axis == CutOffAxis.Vertical;
            return (high ? (vertical ? bounds.Top : bounds.Right) : (vertical ? bounds.Bottom : bounds.Left), high);
        }

        /// <summary>
        /// The same segment cut the other way at the far edge: a head-locked segment cut toward the
        /// far edge becomes one started there, and back; null when <paramref name="segment"/> is not
        /// one of the two forms <see cref="Regenerate"/> builds for the segment reaching that edge.
        /// </summary>
        public static Program ReverseAtFarEdge(Program segment)
        {
            if (segment?.Codes == null || segment.Mode != Mode.Absolute)
                return null;
            var codes = segment.Codes;
            var result = new Program(segment.Mode);
            // Toward the far edge: rapid near, cut to the edge, lock, overcut, unlock.
            if (codes.Count == 5 && codes[0] is RapidMove { Suppressed: false } near
                && codes[1] is LinearMove { Suppressed: false, Layer: LayerType.Cut } toEdge
                && HeadHeightLock.IsLock(codes[2])
                && codes[3] is LinearMove { Suppressed: false, Layer: LayerType.Cut } overcut
                && HeadHeightLock.IsUnlock(codes[4]))
            {
                result.Codes.Add(new RapidMove(toEdge.EndPoint));
                result.Codes.Add(new Comment(HeadHeightLock.Lock));
                result.Codes.Add(new LinearMove(overcut.EndPoint));
                result.Codes.Add(new RapidMove(toEdge.EndPoint));
                result.Codes.Add(new Comment(HeadHeightLock.Unlock));
                result.Codes.Add(new LinearMove(near.EndPoint));
                return result;
            }
            // From the far edge: rapid to the edge, lock, overcut, rapid back, unlock, cut away.
            if (codes.Count == 6 && codes[0] is RapidMove { Suppressed: false } edge
                && HeadHeightLock.IsLock(codes[1])
                && codes[2] is LinearMove { Suppressed: false, Layer: LayerType.Cut } outward
                && codes[3] is RapidMove { Suppressed: false } back && back.EndPoint == edge.EndPoint
                && HeadHeightLock.IsUnlock(codes[4])
                && codes[5] is LinearMove { Suppressed: false, Layer: LayerType.Cut } away)
            {
                result.Codes.Add(new RapidMove(away.EndPoint));
                result.Codes.Add(new LinearMove(edge.EndPoint));
                result.Codes.Add(new Comment(HeadHeightLock.Lock));
                result.Codes.Add(new LinearMove(outward.EndPoint));
                result.Codes.Add(new Comment(HeadHeightLock.Unlock));
                return result;
            }
            return null;
        }

        /// <summary>
        /// Installs Plan Cutting's choice for the far-edge segment: <paramref name="segment"/> (one
        /// of this cut-off's placed parts) takes <paramref name="program"/>, the same segment cut the
        /// other way, and <see cref="FarEdgeFirst"/> and the whole-line program follow it, so
        /// regeneration keeps the direction. The caller has checked that the program is that flip.
        /// </summary>
        internal void InstallFarEdgeSegment(Part segment, Program program)
        {
            var codes = new List<ICode>();
            foreach (var part in SegmentPrograms())
                codes.AddRange((ReverseAtFarEdge(part) != null ? program : part).Codes);
            var whole = new Program(Drawing.Program?.Mode ?? Mode.Absolute);
            whole.Codes.AddRange(codes);
            segment.ReplaceCutOffProgram(program);
            FarEdgeFirst = StartsAtFarEdge(program);
            Drawing.Program = whole;
        }

        /// <summary>Whether <paramref name="segment"/> is the far-edge segment cut from the far edge.</summary>
        public static bool StartsAtFarEdge(Program segment) =>
            segment?.Codes is { Count: 6 } codes && codes[0] is RapidMove && HeadHeightLock.IsLock(codes[1]);

        /// <summary>Measures the same trimmed, filtered segments as regeneration without building a program.</summary>
        internal (double Length, int SegmentCount) MeasureSegments(
            Plate plate,
            CutOffSettings settings,
            Dictionary<Part, Entity> cache = null
        )
        {
            var segments = ComputeSegments(plate, settings, cache);
            return (segments.Sum(s => s.End - s.Start), segments.Count);
        }

        /// <summary>
        /// True when <paramref name="part"/>, the placed part of <paramref name="definition"/>, cuts
        /// one continuous straight line on the definition's axis across the whole sheet, so it
        /// splits the sheet in two. Anything else (gaps around parts, limits short of an edge, a
        /// line on the sheet edge, a stale or unrecognised program) does not separate the sheet.
        /// </summary>
        internal static bool SeparatesSheet(Part part, CutOff definition, Box sheet)
        {
            if (part?.Program == null || definition == null || sheet == null
                || part.Program.Mode != Mode.Absolute)
                return false;
            var vertical = definition.Axis == CutOffAxis.Vertical;
            var line = vertical ? definition.Position.X : definition.Position.Y;
            var acrossMin = vertical ? sheet.Left : sheet.Bottom;
            var acrossMax = vertical ? sheet.Right : sheet.Top;
            var alongMin = vertical ? sheet.Bottom : sheet.Left;
            var alongMax = vertical ? sheet.Top : sheet.Right;
            if (!(line > acrossMin + Math.Tolerance.Epsilon && line < acrossMax - Math.Tolerance.Epsilon))
                return false;

            Vector? position = null;
            double? low = null, high = null;
            var cutting = false;
            var locked = false;
            foreach (var code in part.Program.Codes)
            {
                if (HeadHeightLock.IsLock(code))
                    locked = true;
                else if (HeadHeightLock.IsUnlock(code))
                    locked = false;
                switch (code)
                {
                    case RapidMove rapid:
                        // A second pierce leaves an uncut gap; a head-locked rapid returns along
                        // the overcut to where the cut toward the origin begins.
                        if (cutting && !locked)
                            return false;
                        position = rapid.EndPoint + part.Location;
                        break;
                    case LinearMove move when position != null:
                        var end = move.EndPoint + part.Location;
                        if (!OnLine(position.Value) || !OnLine(end))
                            return false;
                        foreach (var value in new[] { Along(position.Value), Along(end) })
                        {
                            low = low == null ? value : System.Math.Min(low.Value, value);
                            high = high == null ? value : System.Math.Max(high.Value, value);
                        }
                        position = end;
                        cutting = true;
                        break;
                    case Motion:
                        return false;
                }
            }
            return cutting && low <= alongMin + Math.Tolerance.Epsilon && high >= alongMax - Math.Tolerance.Epsilon;

            bool OnLine(Vector point) =>
                System.Math.Abs((vertical ? point.X : point.Y) - line) <= Math.Tolerance.Epsilon;
            double Along(Vector point) => vertical ? point.Y : point.X;
        }

        private string GetName()
        {
            var axisChar = Axis == CutOffAxis.Vertical ? "V" : "H";
            var coord = Axis == CutOffAxis.Vertical ? Position.X : Position.Y;
            return $"CutOff-{axisChar}-{coord:F2}";
        }

        private List<(double Start, double End)> ComputeSegments(
            Plate plate,
            CutOffSettings settings,
            Dictionary<Part, Entity> cache
        )
        {
            var bounds = plate.BoundingBox(includeParts: false);
            // A head-locked overcut replaces the overtravel at the high edge when that is the far edge.
            var lockedFarEdge = LockedFarEdge(plate);
            var overtravel = lockedFarEdge is { High: true } ? 0 : settings.Overtravel;

            double lineStart,
                lineEnd,
                cutPosition;

            if (Axis == CutOffAxis.Vertical)
            {
                cutPosition = Position.X;
                lineStart = StartLimit ?? bounds.Y;
                lineEnd = EndLimit ?? (bounds.Y + bounds.Width + overtravel);
            }
            else
            {
                cutPosition = Position.Y;
                lineStart = StartLimit ?? bounds.X;
                lineEnd = EndLimit ?? (bounds.X + bounds.Length + overtravel);
            }

            // An outlined sheet is cut only where the line crosses material: each crossing is a
            // span of its own, started at the material's edge and overtravelled past the far
            // one, within the user's limits.
            var spans = plate.Outline == null
                ? new List<(double Start, double End)> { (lineStart, lineEnd) }
                : MaterialSpans(plate, cutPosition, lineStart, lineEnd, settings.Overtravel, lockedFarEdge);

            var exclusions = new List<(double Start, double End)>();

            foreach (var part in plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff)
                    continue;

                Entity perimeter;
                if (cache != null)
                {
                    cache.TryGetValue(part, out perimeter);
                }
                else
                {
                    // No pre-built cache: this is a direct/uncached caller (e.g. an
                    // external API consumer calling Regenerate without going through
                    // Plate.RegenerateCutOffs or the UI handlers, which always pass a
                    // cache). Build the same converted-material envelope on the fly so
                    // this path gets the same safety as the cached one, instead of
                    // silently trusting the part's bare BoundingBox.
                    perimeter = Plate.BuildPartPerimeter(part);
                }
                var partExclusions = GetPartExclusions(
                    part,
                    perimeter,
                    cutPosition,
                    lineStart,
                    lineEnd,
                    settings.PartClearance
                );
                exclusions.AddRange(partExclusions);
            }
            foreach (var gap in gaps)
                exclusions.Add((gap.Start, gap.End));

            exclusions.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<(double Start, double End)>();
            foreach (var ex in exclusions)
            {
                if (merged.Count > 0 && ex.Start <= merged[^1].End)
                    merged[^1] = (merged[^1].Start, System.Math.Max(merged[^1].End, ex.End));
                else
                    merged.Add(ex);
            }

            var segments = new List<(double Start, double End)>();
            foreach (var (spanStart, spanEnd) in spans)
            {
                var current = spanStart;

                foreach (var ex in merged)
                {
                    var clampedStart = System.Math.Max(ex.Start, spanStart);
                    var clampedEnd = System.Math.Min(ex.End, spanEnd);

                    if (clampedStart > current)
                        segments.Add((current, clampedStart));

                    current = System.Math.Max(current, clampedEnd);
                }

                if (current < spanEnd)
                    segments.Add((current, spanEnd));
            }

            segments = segments.Where(s => (s.End - s.Start) >= settings.MinSegmentLength).ToList();

            return segments;
        }

        /// <summary>
        /// Where the line at <paramref name="cutPosition"/> crosses the sheet's outlined material,
        /// in ascending order along the axis and clipped to the line's limits. Each span starts at
        /// the material edge and runs <paramref name="overtravel"/> past the far one unless a limit
        /// or a head-locked far edge ends it there.
        /// </summary>
        private List<(double Start, double End)> MaterialSpans(Plate plate, double cutPosition,
            double lineStart, double lineEnd, double overtravel, (double Edge, bool High)? lockedFarEdge)
        {
            var vertical = Axis == CutOffAxis.Vertical;
            var crossings = new List<double>();
            foreach (var path in plate.Outline.Material(plate.Outline.PlacementOrigin(plate.Quadrant)))
            {
                for (var i = 0; i < path.Count; i++)
                {
                    var a = path[i];
                    var b = path[(i + 1) % path.Count];
                    var (aAcross, aAlong) = vertical ? (a.x, a.y) : (a.y, a.x);
                    var (bAcross, bAlong) = vertical ? (b.x, b.y) : (b.y, b.x);
                    // Half-open on the across axis, so a line through a vertex counts one edge.
                    if (aAcross <= cutPosition == bAcross <= cutPosition)
                        continue;
                    crossings.Add(aAlong + (bAlong - aAlong) * (cutPosition - aAcross) / (bAcross - aAcross));
                }
            }
            crossings.Sort();

            var spans = new List<(double Start, double End)>();
            for (var i = 0; i + 1 < crossings.Count; i += 2)
            {
                var start = System.Math.Max(crossings[i], lineStart);
                var end = crossings[i + 1];
                var limited = EndLimit != null && end >= EndLimit.Value
                    || lockedFarEdge is var (edge, high) && high && end == edge;
                end = System.Math.Min(limited ? end : end + overtravel, lineEnd);
                if (end > start)
                    spans.Add((start, end));
            }
            return spans;
        }

        private static readonly List<(double Start, double End)> EmptyExclusions = new();

        private List<(double Start, double End)> GetPartExclusions(
            Part part,
            Entity perimeter,
            double cutPosition,
            double lineStart,
            double lineEnd,
            double clearance
        )
        {
            var cached = part.BoundingBox;
            var roundoff = GetBoundsRoundoff(part);
            // Keep the farther of the cached and converted perimeter extents. The
            // perimeter's fine intersection/offset can miss a near-tangent sliver;
            // only a line in that narrow outer band needs the conservative box.
            var bb = cached;
            if (perimeter != null)
            {
                var converted = perimeter.BoundingBox;
                var left = System.Math.Min(cached.Left, converted.Left);
                var right = System.Math.Max(cached.Right, converted.Right);
                var bottom = System.Math.Min(cached.Bottom, converted.Bottom);
                var top = System.Math.Max(cached.Top, converted.Top);
                bb = new Box(left, bottom, right - left, top - bottom);
            }
            var (cachedMin, cachedMax) = AxisBounds(cached, clearance);
            var (partMin, partMax) = AxisBounds(bb, clearance);
            var (partStart, partEnd) = CrossAxisBounds(bb, clearance);
            // Match the planner's representational allowance in BOTH pruning and
            // fallback exclusions. Tolerating it only in validation could cut an edge.
            partMin -= roundoff;
            partMax += roundoff;
            partStart -= roundoff;
            partEnd += roundoff;

            if (cutPosition < partMin || cutPosition > partMax)
                return EmptyExclusions;

            if (perimeter != null)
            {
                if (cutPosition <= cachedMin + roundoff || cutPosition >= cachedMax - roundoff)
                    return new List<(double Start, double End)> { (partStart, partEnd) };
                var perimeterExclusions = IntersectPerimeter(
                    perimeter,
                    cutPosition,
                    lineStart,
                    lineEnd,
                    clearance
                );
                if (perimeterExclusions != null)
                    return perimeterExclusions;
            }

            return new List<(double Start, double End)> { (partStart, partEnd) };
        }

        /// <summary>
        /// Bounds reconstructed as (local minimum + placement) + size can differ
        /// from translated material endpoints by a few floating-point steps. This
        /// is not a geometry tolerance: cap it well below epsilon so inconsistent
        /// arcs still require repair. Do not change shared Part bounds or programs.
        /// </summary>
        internal static double GetBoundsRoundoff(Part part)
        {
            var bb = part.BoundingBox;
            var magnitude = System.Math.Max(System.Math.Abs(bb.Left), System.Math.Abs(bb.Right));
            magnitude = System.Math.Max(magnitude,
                System.Math.Max(System.Math.Abs(bb.Bottom), System.Math.Abs(bb.Top)));
            magnitude = System.Math.Max(magnitude, System.Math.Max(bb.Length, bb.Width));
            magnitude = System.Math.Max(magnitude,
                System.Math.Max(System.Math.Abs(part.Location.X), System.Math.Abs(part.Location.Y)));
            var step = System.Math.BitIncrement(magnitude) - magnitude;
            // Imported, nearly tangent arcs can amplify endpoint rounding when
            // ConvertProgram fits their centers to a chord. Keep that allowance
            // tiny and apply it to both pruning and fallback exclusions above;
            // larger refits still require an explicit geometry repair. The allowance is
            // absolute: rotated small arcs on ordinary sheets refit by ~5e-11, which a
            // magnitude-scaled step count would reject.
            var arcFitRoundoff = Math.Tolerance.Epsilon / 100000;
            return double.IsFinite(step)
                ? System.Math.Min(System.Math.Max(8 * step, arcFitRoundoff), Math.Tolerance.Epsilon / 4)
                : 0;
        }

        private List<(double Start, double End)> IntersectPerimeter(
            Entity perimeter,
            double cutPosition,
            double lineStart,
            double lineEnd,
            double clearance
        )
        {
            var offset = OffsetOutward(perimeter, clearance);
            if (offset != null && offset.All(target => target is Polygon))
                return SlicePolygons(offset.Cast<Polygon>(), cutPosition);

            var usedOffset = offset != null;
            var targets = offset ?? new List<Entity> { perimeter };
            var cutLine = new Line(
                MakePoint(cutPosition, lineStart),
                MakePoint(cutPosition, lineEnd)
            );

            var pts = new List<Vector>();

            foreach (var target in targets)
            {
                if (target.Intersects(cutLine, out var targetPts))
                    pts.AddRange(targetPts);
            }

            if (pts.Count < 2)
                return null;

            var coords = pts.Select(pt => Axis == CutOffAxis.Vertical ? pt.Y : pt.X)
                .OrderBy(c => c)
                .ToList();

            if (coords.Count % 2 != 0)
                return null;

            // Intersects reports both incident edges at a shared vertex. Such hits may be
            // crossings or tangencies, so neither pairing nor deduplicating them preserves
            // inside/outside parity. Fall back to the clearance-expanded part bounds.
            for (var i = 1; i < coords.Count; i++)
            {
                if (coords[i] - coords[i - 1] <= Math.Tolerance.Epsilon)
                    return null;
            }

            var padding = usedOffset ? 0 : clearance;
            var result = new List<(double Start, double End)>();
            for (var i = 0; i < coords.Count; i += 2)
                result.Add((coords[i] - padding, coords[i + 1] + padding));

            return result;
        }

        /// <summary>
        /// Exact cross-section of the clearance outline on the cut line. A vertex on the line
        /// counts as below it (half-open edges), so a line through a vertex counts one
        /// crossing when the outline passes through and zero or two when it only touches. Edges
        /// lying on the line are excluded whole. Unlike <see cref="Entity.Intersects"/>, which
        /// reports a shared vertex once per incident edge, this never needs the bounding-box
        /// fallback, so a lattice line through a vertex is trimmed like any other line.
        /// </summary>
        private List<(double Start, double End)> SlicePolygons(IEnumerable<Polygon> polygons, double cutPosition)
        {
            var crossings = new List<double>();
            var result = new List<(double Start, double End)>();
            foreach (var polygon in polygons)
            {
                var vertices = polygon.Vertices;
                if (vertices.Count < 3)
                    return null;
                var closed = vertices[0].X == vertices[^1].X && vertices[0].Y == vertices[^1].Y;
                var edges = closed ? vertices.Count - 1 : vertices.Count;
                for (var i = 0; i < edges; i++)
                {
                    var from = vertices[i];
                    var to = vertices[(i + 1) % vertices.Count];
                    var (acrossFrom, alongFrom) = Split(from);
                    var (acrossTo, alongTo) = Split(to);
                    if (acrossFrom == cutPosition && acrossTo == cutPosition)
                        result.Add((System.Math.Min(alongFrom, alongTo), System.Math.Max(alongFrom, alongTo)));
                    else if (acrossFrom > cutPosition != acrossTo > cutPosition)
                        crossings.Add(acrossFrom == cutPosition ? alongFrom
                            : acrossTo == cutPosition ? alongTo
                            : alongFrom + (cutPosition - acrossFrom) * (alongTo - alongFrom) / (acrossTo - acrossFrom));
                }
            }

            if (crossings.Count % 2 != 0)
                return null;
            crossings.Sort();
            for (var i = 0; i < crossings.Count; i += 2)
                result.Add((crossings[i], crossings[i + 1]));
            return result;

            (double Across, double Along) Split(Vector point) =>
                Axis == CutOffAxis.Vertical ? (point.X, point.Y) : (point.Y, point.X);
        }

        /// <summary>
        /// Grows the perimeter by the clearance as one Clipper region offset, so slots
        /// narrower than twice the clearance close up instead of leaving a gap the cut
        /// could run into. Holes appear only where the perimeter curls back on itself.
        /// </summary>
        private static List<Entity> OffsetOutward(Entity perimeter, double clearance)
        {
            if (clearance <= 0)
                return null;

            var offset = perimeter switch
            {
                Shape shape => ClipperBridge.OffsetPerimeter(
                    shape,
                    clearance,
                    OffsetTolerance,
                    circumscribe: true
                ),
                Polygon polygon => ClipperBridge.OffsetPerimeter(
                    polygon,
                    clearance,
                    OffsetTolerance,
                    circumscribe: true
                ),
                _ => null,
            };

            if (offset == null || offset.Outers.Count == 0)
                return null;

            return offset.Outers.Concat(offset.Holes).Cast<Entity>().ToList();
        }

        private Vector MakePoint(double cutCoord, double lineCoord) =>
            Axis == CutOffAxis.Vertical
                ? new Vector(cutCoord, lineCoord)
                : new Vector(lineCoord, cutCoord);

        private (double Min, double Max) AxisBounds(Box bb, double clearance) =>
            Axis == CutOffAxis.Vertical
                ? (bb.X - clearance, bb.X + bb.Length + clearance)
                : (bb.Y - clearance, bb.Y + bb.Width + clearance);

        private (double Start, double End) CrossAxisBounds(Box bb, double clearance) =>
            Axis == CutOffAxis.Vertical
                ? (bb.Y - clearance, bb.Y + bb.Width + clearance)
                : (bb.X - clearance, bb.X + bb.Length + clearance);

        private Program BuildProgram(
            List<(double Start, double End)> segments,
            CutOffSettings settings,
            (double Edge, bool High)? farEdge
        )
        {
            var program = new Program();

            if (segments.Count == 0)
                return program;

            var toward = settings.CutDirection == CutDirection.TowardOrigin;
            segments = toward
                ? segments.OrderByDescending(s => s.Start).ToList()
                : segments.OrderBy(s => s.Start).ToList();

            var cutPos = Axis == CutOffAxis.Vertical ? Position.X : Position.Y;

            foreach (var seg in segments)
            {
                if (farEdge is var (edge, high) && (high ? seg.End : seg.Start) == edge)
                {
                    // The segment reaching the far edge overcuts it with the head height locked,
                    // whichever way the rest of the line runs.
                    var near = MakePoint(cutPos, high ? seg.Start : seg.End);
                    var atEdge = MakePoint(cutPos, edge);
                    var beyond = MakePoint(cutPos, high ? edge + HeadLockOvercut : edge - HeadLockOvercut);
                    var towardEdge = new Program();
                    towardEdge.Codes.Add(new RapidMove(near));
                    towardEdge.Codes.Add(new LinearMove(atEdge));
                    towardEdge.Codes.Add(new Comment(HeadHeightLock.Lock));
                    towardEdge.Codes.Add(new LinearMove(beyond));
                    towardEdge.Codes.Add(new Comment(HeadHeightLock.Unlock));
                    program.Codes.AddRange((FarEdgeFirst ? ReverseAtFarEdge(towardEdge) : towardEdge).Codes);
                    continue;
                }
                var (from, to) = toward ? (seg.End, seg.Start) : (seg.Start, seg.End);
                program.Codes.Add(new RapidMove(MakePoint(cutPos, from)));
                program.Codes.Add(new LinearMove(MakePoint(cutPos, to)));
            }

            return program;
        }
    }
}
