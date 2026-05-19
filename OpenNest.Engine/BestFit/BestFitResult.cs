using OpenNest.Engine;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.BestFit
{
    public class BestFitResult
    {
        public PairCandidate Candidate { get; set; }
        public double RotatedArea { get; set; }
        public double BoundingWidth { get; set; }
        public double BoundingHeight { get; set; }
        public double OptimalRotation { get; set; }
        public bool Keep { get; set; }
        public string Reason { get; set; }
        public double TrueArea { get; set; }
        public List<double> HullAngles { get; set; }

        public double Utilization
        {
            get { return RotatedArea > 0 ? TrueArea / RotatedArea : 0; }
        }

        public double LongestSide
        {
            get { return System.Math.Max(BoundingWidth, BoundingHeight); }
        }

        public double ShortestSide
        {
            get { return System.Math.Min(BoundingWidth, BoundingHeight); }
        }

        public List<Part> BuildParts(Drawing drawing)
        {
            var part1 = Part.CreateAtOrigin(drawing);

            var part2 = Part.CreateAtOrigin(drawing, Candidate.Part2Rotation);
            part2.Location = Candidate.Part2Offset;
            part2.UpdateBounds();

            if (!OptimalRotation.IsEqualTo(0))
            {
                var pairBounds = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
                var center = pairBounds.Center;
                part1.Rotate(-OptimalRotation, center);
                part2.Rotate(-OptimalRotation, center);
            }

            var finalBounds = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
            var offset = new Vector(-finalBounds.Left, -finalBounds.Bottom);
            part1.Offset(offset);
            part2.Offset(offset);

            return new List<Part> { part1, part2 };
        }

        public List<Part> BuildCanonicalParts()
        {
            return NormalizeToCutOrigin(BuildParts(Candidate.Drawing));
        }

        public List<Part> BuildSourceParts(Drawing drawing)
        {
            var parts = BuildCanonicalParts();
            var sourceAngle = drawing?.Source?.Angle ?? 0.0;

            for (var i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var rebound = Part.CreateAtOrigin(drawing, p.Rotation);
                var delta = p.BoundingBox.Location - rebound.BoundingBox.Location;
                rebound.Offset(delta);
                rebound.UpdateBounds();
                parts[i] = rebound;
            }

            return NormalizeToCutOrigin(CanonicalFrame.FromCanonical(parts, sourceAngle));
        }

        public Box GetCutBounds(List<Part> parts)
        {
            return GetCutBoundingBox(parts);
        }

        private static List<Part> NormalizeToCutOrigin(List<Part> parts)
        {
            if (parts == null || parts.Count == 0)
                return parts;

            var bounds = GetCutBoundingBox(parts);
            var offset = new Vector(-bounds.Left, -bounds.Bottom);

            foreach (var part in parts)
                part.Offset(offset);

            return parts;
        }

        private static Box GetCutBoundingBox(List<Part> parts)
        {
            var entities = new List<IBoundable>();

            foreach (var part in parts)
            {
                var partEntities = ConvertProgram.ToGeometry(part.Program)
                    .Where(e => e.Layer != SpecialLayers.Rapid)
                    .ToList();

                foreach (var entity in partEntities)
                {
                    entity.Offset(part.Location);
                    entities.Add(entity);
                }
            }

            return entities.GetBoundingBox();
        }
    }

    public enum BestFitSortField
    {
        Area,
        LongestSide,
        ShortestSide,
        Type,
        OriginalSequence,
        Keep,
        WhyKeepDrop
    }
}
