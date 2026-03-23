using OpenNest.CNC;
using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest
{
    public enum CutOffAxis
    {
        Horizontal,
        Vertical
    }

    public class CutOff
    {
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

        public void Regenerate(Plate plate, CutOffSettings settings, Dictionary<Part, Entity> cache = null)
        {
            var segments = ComputeSegments(plate, settings, cache);
            var program = BuildProgram(segments, settings);
            Drawing.Program = program;
        }

        private string GetName()
        {
            var axisChar = Axis == CutOffAxis.Vertical ? "V" : "H";
            var coord = Axis == CutOffAxis.Vertical ? Position.X : Position.Y;
            return $"CutOff-{axisChar}-{coord:F2}";
        }

        private List<(double Start, double End)> ComputeSegments(Plate plate, CutOffSettings settings, Dictionary<Part, Entity> cache)
        {
            var bounds = plate.BoundingBox(includeParts: false);

            double lineStart, lineEnd, cutPosition;

            if (Axis == CutOffAxis.Vertical)
            {
                cutPosition = Position.X;
                lineStart = StartLimit ?? bounds.Y;
                lineEnd = EndLimit ?? (bounds.Y + bounds.Length + settings.Overtravel);
            }
            else
            {
                cutPosition = Position.Y;
                lineStart = StartLimit ?? bounds.X;
                lineEnd = EndLimit ?? (bounds.X + bounds.Width + settings.Overtravel);
            }

            var exclusions = new List<(double Start, double End)>();

            foreach (var part in plate.Parts)
            {
                if (part.BaseDrawing.IsCutOff)
                    continue;

                Entity perimeter = null;
                cache?.TryGetValue(part, out perimeter);
                var partExclusions = GetPartExclusions(part, perimeter, cutPosition, lineStart, lineEnd, settings.PartClearance);
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
            Part part, Entity perimeter, double cutPosition, double lineStart, double lineEnd, double clearance)
        {
            var bb = part.BoundingBox;
            var (partMin, partMax) = AxisBounds(bb, clearance);
            var (partStart, partEnd) = CrossAxisBounds(bb, clearance);

            if (cutPosition < partMin || cutPosition > partMax)
                return EmptyExclusions;

            if (perimeter != null)
            {
                var perimeterExclusions = IntersectPerimeter(perimeter, cutPosition, lineStart, lineEnd, clearance);
                if (perimeterExclusions != null)
                    return perimeterExclusions;
            }

            return new List<(double Start, double End)> { (partStart, partEnd) };
        }

        private List<(double Start, double End)> IntersectPerimeter(
            Entity perimeter, double cutPosition, double lineStart, double lineEnd, double clearance)
        {
            var cutLine = new Line(MakePoint(cutPosition, lineStart), MakePoint(cutPosition, lineEnd));

            if (!perimeter.Intersects(cutLine, out var pts) || pts.Count < 2)
                return null;

            var coords = pts
                .Select(pt => Axis == CutOffAxis.Vertical ? pt.Y : pt.X)
                .OrderBy(c => c)
                .ToList();

            if (coords.Count % 2 != 0)
                return null;

            var result = new List<(double Start, double End)>();
            for (var i = 0; i < coords.Count; i += 2)
                result.Add((coords[i] - clearance, coords[i + 1] + clearance));

            return result;
        }

        private Vector MakePoint(double cutCoord, double lineCoord) =>
            Axis == CutOffAxis.Vertical
                ? new Vector(cutCoord, lineCoord)
                : new Vector(lineCoord, cutCoord);

        private (double Min, double Max) AxisBounds(Box bb, double clearance) =>
            Axis == CutOffAxis.Vertical
                ? (bb.X - clearance, bb.X + bb.Width + clearance)
                : (bb.Y - clearance, bb.Y + bb.Length + clearance);

        private (double Start, double End) CrossAxisBounds(Box bb, double clearance) =>
            Axis == CutOffAxis.Vertical
                ? (bb.Y - clearance, bb.Y + bb.Length + clearance)
                : (bb.X - clearance, bb.X + bb.Width + clearance);

        private Program BuildProgram(List<(double Start, double End)> segments, CutOffSettings settings)
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
