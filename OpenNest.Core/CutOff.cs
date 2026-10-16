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

    public class CutOff
    {
        private const double OffsetTolerance = 0.001;

        public Vector Position { get; set; }
        public CutOffAxis Axis { get; set; }
        public double? StartLimit { get; set; }
        public double? EndLimit { get; set; }
        public Drawing Drawing { get; private set; }

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
            var program = BuildProgram(segments, settings);
            Drawing.Program = program;
        }

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
            foreach (var code in part.Program.Codes)
            {
                switch (code)
                {
                    case RapidMove rapid:
                        if (cutting)
                            return false; // A second pierce leaves an uncut gap.
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

            double lineStart,
                lineEnd,
                cutPosition;

            if (Axis == CutOffAxis.Vertical)
            {
                cutPosition = Position.X;
                lineStart = StartLimit ?? bounds.Y;
                lineEnd = EndLimit ?? (bounds.Y + bounds.Width + settings.Overtravel);
            }
            else
            {
                cutPosition = Position.Y;
                lineStart = StartLimit ?? bounds.X;
                lineEnd = EndLimit ?? (bounds.X + bounds.Length + settings.Overtravel);
            }

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
            var current = lineStart;

            foreach (var ex in merged)
            {
                var clampedStart = System.Math.Max(ex.Start, lineStart);
                var clampedEnd = System.Math.Min(ex.End, lineEnd);

                if (clampedStart > current)
                    segments.Add((current, clampedStart));

                current = System.Math.Max(current, clampedEnd);
            }

            if (current < lineEnd)
                segments.Add((current, lineEnd));

            segments = segments.Where(s => (s.End - s.Start) >= settings.MinSegmentLength).ToList();

            return segments;
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
            // larger refits still require an explicit geometry repair.
            var arcFitRoundoff = System.Math.Min(4096 * step, Math.Tolerance.Epsilon / 100000);
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
            CutOffSettings settings
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
                var (from, to) = toward ? (seg.End, seg.Start) : (seg.Start, seg.End);
                program.Codes.Add(new RapidMove(MakePoint(cutPos, from)));
                program.Codes.Add(new LinearMove(MakePoint(cutPos, to)));
            }

            return program;
        }
    }
}
