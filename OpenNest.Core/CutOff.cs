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

        public void Regenerate(Plate plate, CutOffSettings settings)
        {
            var segments = ComputeSegments(plate, settings);
            var program = BuildProgram(segments, settings);
            Drawing.Program = program;
        }

        private string GetName()
        {
            var axisChar = Axis == CutOffAxis.Vertical ? "V" : "H";
            var coord = Axis == CutOffAxis.Vertical ? Position.X : Position.Y;
            return $"CutOff-{axisChar}-{coord:F2}";
        }

        private List<(double Start, double End)> ComputeSegments(Plate plate, CutOffSettings settings)
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

                var bb = part.BoundingBox;
                double partStart, partEnd, partMin, partMax;

                if (Axis == CutOffAxis.Vertical)
                {
                    partMin = bb.X - settings.PartClearance;
                    partMax = bb.X + bb.Width + settings.PartClearance;
                    partStart = bb.Y - settings.PartClearance;
                    partEnd = bb.Y + bb.Length + settings.PartClearance;
                }
                else
                {
                    partMin = bb.Y - settings.PartClearance;
                    partMax = bb.Y + bb.Length + settings.PartClearance;
                    partStart = bb.X - settings.PartClearance;
                    partEnd = bb.X + bb.Width + settings.PartClearance;
                }

                if (cutPosition >= partMin && cutPosition <= partMax)
                    exclusions.Add((partStart, partEnd));
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
