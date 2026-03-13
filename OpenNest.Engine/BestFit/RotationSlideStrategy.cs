using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    public class RotationSlideStrategy : IBestFitStrategy
    {
        public RotationSlideStrategy(double part2Rotation, int type, string description)
        {
            Part2Rotation = part2Rotation;
            Type = type;
            Description = description;
        }

        public double Part2Rotation { get; }
        public int Type { get; }
        public string Description { get; }

        public List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize)
        {
            var candidates = new List<PairCandidate>();

            var part1 = Part.CreateAtOrigin(drawing);
            var part2Template = Part.CreateAtOrigin(drawing, Part2Rotation);

            var testNumber = 0;

            // Try pushing left (horizontal slide)
            GenerateCandidatesForAxis(
                part1, part2Template, drawing, spacing, stepSize,
                PushDirection.Left, candidates, ref testNumber);

            // Try pushing down (vertical slide)
            GenerateCandidatesForAxis(
                part1, part2Template, drawing, spacing, stepSize,
                PushDirection.Down, candidates, ref testNumber);

            // Try pushing right (approach from left — finds concave interlocking)
            GenerateCandidatesForAxis(
                part1, part2Template, drawing, spacing, stepSize,
                PushDirection.Right, candidates, ref testNumber);

            // Try pushing up (approach from below — finds concave interlocking)
            GenerateCandidatesForAxis(
                part1, part2Template, drawing, spacing, stepSize,
                PushDirection.Up, candidates, ref testNumber);

            return candidates;
        }

        private void GenerateCandidatesForAxis(
            Part part1, Part part2Template, Drawing drawing,
            double spacing, double stepSize, PushDirection pushDir,
            List<PairCandidate> candidates, ref int testNumber)
        {
            var bbox1 = part1.BoundingBox;
            var bbox2 = part2Template.BoundingBox;
            var halfSpacing = spacing / 2;

            var isHorizontalPush = pushDir == PushDirection.Left || pushDir == PushDirection.Right;

            // Perpendicular range: part2 slides across the full extent of part1
            double perpMin, perpMax, pushStartOffset;

            if (isHorizontalPush)
            {
                perpMin = -(bbox2.Length + spacing);
                perpMax = bbox1.Length + bbox2.Length + spacing;
                pushStartOffset = bbox1.Width + bbox2.Width + spacing * 2;
            }
            else
            {
                perpMin = -(bbox2.Width + spacing);
                perpMax = bbox1.Width + bbox2.Width + spacing;
                pushStartOffset = bbox1.Length + bbox2.Length + spacing * 2;
            }

            // Pre-compute part1's offset lines (half-spacing outward)
            var part1Lines = Helper.GetOffsetPartLines(part1, halfSpacing);

            // Align sweep start to a multiple of stepSize so that offset=0 is always
            // included. This ensures perfect grid arrangements (side-by-side, stacked)
            // are generated for rectangular parts.
            var alignedStart = System.Math.Ceiling(perpMin / stepSize) * stepSize;

            for (var offset = alignedStart; offset <= perpMax; offset += stepSize)
            {
                var (slideDist, finalPosition) = ComputeSlideResult(
                    part2Template, part1Lines, halfSpacing,
                    offset, pushStartOffset, isHorizontalPush, pushDir);

                if (slideDist >= double.MaxValue || slideDist < 0)
                    continue;

                candidates.Add(new PairCandidate
                {
                    Drawing = drawing,
                    Part1Rotation = 0,
                    Part2Rotation = Part2Rotation,
                    Part2Offset = finalPosition,
                    StrategyType = Type,
                    TestNumber = testNumber++,
                    Spacing = spacing
                });
            }
        }

        private static Vector GetPushVector(PushDirection direction, double distance)
        {
            switch (direction)
            {
                case PushDirection.Left: return new Vector(-distance, 0);
                case PushDirection.Right: return new Vector(distance, 0);
                case PushDirection.Down: return new Vector(0, -distance);
                case PushDirection.Up: return new Vector(0, distance);
                default: return Vector.Zero;
            }
        }
        private static double ComputeSlideDistance(
            Part part2Template, List<Line> part1Lines, double halfSpacing,
            double offset, double pushStartOffset,
            bool isHorizontalPush, PushDirection pushDir)
        {
            var part2 = (Part)part2Template.Clone();

            var isPositiveStart = pushDir == PushDirection.Left || pushDir == PushDirection.Down;
            var startPos = isPositiveStart ? pushStartOffset : -pushStartOffset;

            if (isHorizontalPush)
                part2.Offset(startPos, offset);
            else
                part2.Offset(offset, startPos);

            var part2Lines = Helper.GetOffsetPartLines(part2, halfSpacing);

            return Helper.DirectionalDistance(part2Lines, part1Lines, pushDir);
        }

        private static (double slideDist, Vector finalPosition) ComputeSlideResult(
            Part part2Template, List<Line> part1Lines, double halfSpacing,
            double offset, double pushStartOffset,
            bool isHorizontalPush, PushDirection pushDir)
        {
            var part2 = (Part)part2Template.Clone();

            var isPositiveStart = pushDir == PushDirection.Left || pushDir == PushDirection.Down;
            var startPos = isPositiveStart ? pushStartOffset : -pushStartOffset;

            if (isHorizontalPush)
                part2.Offset(startPos, offset);
            else
                part2.Offset(offset, startPos);

            var part2Lines = Helper.GetOffsetPartLines(part2, halfSpacing);
            var slideDist = Helper.DirectionalDistance(part2Lines, part1Lines, pushDir);

            var pushVector = GetPushVector(pushDir, slideDist);
            var finalPosition = part2.Location + pushVector;

            return (slideDist, finalPosition);
        }
    }
}
