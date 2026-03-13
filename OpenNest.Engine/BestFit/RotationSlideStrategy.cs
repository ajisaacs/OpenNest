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
            const int CoarseMultiplier = 16;
            const int MaxRegions = 5;

            var bbox1 = part1.BoundingBox;
            var bbox2 = part2Template.BoundingBox;
            var halfSpacing = spacing / 2;

            var isHorizontalPush = pushDir == PushDirection.Left || pushDir == PushDirection.Right;

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

            var part1Lines = Helper.GetOffsetPartLines(part1, halfSpacing);

            // Start with the full range as a single region.
            var regions = new List<(double min, double max)> { (perpMin, perpMax) };
            var currentStep = stepSize * CoarseMultiplier;

            // Iterative halving: coarse sweep, select top regions, narrow, repeat.
            while (currentStep > stepSize)
            {
                var hits = new List<(double offset, double slideDist)>();

                foreach (var (regionMin, regionMax) in regions)
                {
                    var alignedStart = System.Math.Ceiling(regionMin / currentStep) * currentStep;

                    for (var offset = alignedStart; offset <= regionMax; offset += currentStep)
                    {
                        var slideDist = ComputeSlideDistance(
                            part2Template, part1Lines, halfSpacing,
                            offset, pushStartOffset, isHorizontalPush, pushDir);

                        if (slideDist >= double.MaxValue || slideDist < 0)
                            continue;

                        hits.Add((offset, slideDist));
                    }
                }

                if (hits.Count == 0)
                    return;

                // Select top regions by tightest fit, deduplicating nearby hits.
                hits.Sort((a, b) => a.slideDist.CompareTo(b.slideDist));

                var selectedOffsets = new List<double>();

                foreach (var (offset, _) in hits)
                {
                    var tooClose = false;

                    foreach (var selected in selectedOffsets)
                    {
                        if (System.Math.Abs(offset - selected) < currentStep)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (!tooClose)
                    {
                        selectedOffsets.Add(offset);

                        if (selectedOffsets.Count >= MaxRegions)
                            break;
                    }
                }

                // Build narrowed regions around selected offsets.
                regions = new List<(double min, double max)>();

                foreach (var offset in selectedOffsets)
                {
                    var regionMin = System.Math.Max(perpMin, offset - currentStep);
                    var regionMax = System.Math.Min(perpMax, offset + currentStep);
                    regions.Add((regionMin, regionMax));
                }

                currentStep /= 2;
            }

            // Final pass: sweep refined regions at stepSize, generating candidates.
            foreach (var (regionMin, regionMax) in regions)
            {
                var alignedStart = System.Math.Ceiling(regionMin / stepSize) * stepSize;

                for (var offset = alignedStart; offset <= regionMax; offset += stepSize)
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
