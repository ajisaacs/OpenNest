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

        private List<(double Start, double End)> GetPartExclusions(
            Part part, Entity perimeter, double cutPosition, double lineStart, double lineEnd, double clearance)
        {
            var bb = part.BoundingBox;
            double partMin, partMax, partStart, partEnd;

            if (Axis == CutOffAxis.Vertical)
            {
                partMin = bb.X - clearance;
                partMax = bb.X + bb.Width + clearance;
                partStart = bb.Y - clearance;
                partEnd = bb.Y + bb.Length + clearance;
            }
            else
            {
                partMin = bb.Y - clearance;
                partMax = bb.Y + bb.Length + clearance;
                partStart = bb.X - clearance;
                partEnd = bb.X + bb.Width + clearance;
            }

            if (cutPosition < partMin || cutPosition > partMax)
                return new List<(double Start, double End)>();

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
            Vector p1, p2;
            if (Axis == CutOffAxis.Vertical)
            {
                p1 = new Vector(cutPosition, lineStart);
                p2 = new Vector(cutPosition, lineEnd);
            }
            else
            {
                p1 = new Vector(lineStart, cutPosition);
                p2 = new Vector(lineEnd, cutPosition);
            }

            var cutLine = new Line(p1, p2);

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

        private Program BuildProgram(List<(double Start, double End)> segments, CutOffSettings settings)
        {
            var program = new Program();

            if (segments.Count == 0)
                return program;

            if (settings.CutDirection == CutDirection.TowardOrigin)
                segments = segments.OrderByDescending(s => s.Start).ToList();
            else
                segments = segments.OrderBy(s => s.Start).ToList();

            foreach (var seg in segments)
            {
                double startVal, endVal;
                if (settings.CutDirection == CutDirection.TowardOrigin)
                {
                    startVal = seg.End;
                    endVal = seg.Start;
                }
                else
                {
                    startVal = seg.Start;
                    endVal = seg.End;
                }

                Vector startPt, endPt;
                if (Axis == CutOffAxis.Vertical)
                {
                    startPt = new Vector(Position.X, startVal);
                    endPt = new Vector(Position.X, endVal);
                }
                else
                {
                    startPt = new Vector(startVal, Position.Y);
                    endPt = new Vector(endVal, Position.Y);
                }

                program.Codes.Add(new RapidMove(startPt));
                program.Codes.Add(new LinearMove(endPt));
            }

            return program;
        }
    }
}
