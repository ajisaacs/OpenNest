using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

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
