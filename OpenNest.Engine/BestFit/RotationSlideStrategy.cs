using OpenNest.Geometry;
using System.Collections.Generic;

namespace OpenNest.Engine.BestFit
{
    public class RotationSlideStrategy : IBestFitStrategy
    {
        private readonly IDistanceComputer _distanceComputer;

        private static readonly (double DirX, double DirY)[] PushDirections =
        {
            (-1, 0),  // Left
            (0, -1),  // Down
            (1, 0),   // Right
            (0, 1)    // Up
        };

        public RotationSlideStrategy(double part2Rotation, int strategyIndex, string description,
            IDistanceComputer distanceComputer)
        {
            Part2Rotation = part2Rotation;
            StrategyIndex = strategyIndex;
            Description = description;
            _distanceComputer = distanceComputer;
        }

        public double Part2Rotation { get; }
        public int StrategyIndex { get; }
        public string Description { get; }

        public List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize)
        {
            var candidates = new List<PairCandidate>();

            var part1 = Part.CreateAtOrigin(drawing);
            var part2Template = Part.CreateAtOrigin(drawing, Part2Rotation);

            var halfSpacing = spacing / 2;
            var part1Lines = PartGeometry.GetOffsetPartLines(part1, halfSpacing);
            var part2TemplateLines = PartGeometry.GetOffsetPartLines(part2Template, halfSpacing);

            var bbox1 = part1.BoundingBox;
            var bbox2 = part2Template.BoundingBox;

            var offsets = BuildOffsets(bbox1, bbox2, spacing, stepSize);

            if (offsets.Length == 0)
                return candidates;

            var distances = _distanceComputer.ComputeDistances(
                part1Lines, part2TemplateLines, offsets);

            var testNumber = 0;

            for (var i = 0; i < offsets.Length; i++)
            {
                var slideDist = distances[i];
                if (slideDist >= double.MaxValue || slideDist < 0)
                    continue;

                var finalPosition = new Vector(
                    part2Template.Location.X + offsets[i].Dx + offsets[i].DirX * slideDist,
                    part2Template.Location.Y + offsets[i].Dy + offsets[i].DirY * slideDist);

                candidates.Add(new PairCandidate
                {
                    Drawing = drawing,
                    Part1Rotation = 0,
                    Part2Rotation = Part2Rotation,
                    Part2Offset = finalPosition,
                    StrategyIndex = StrategyIndex,
                    TestNumber = testNumber++,
                    Spacing = spacing
                });
            }

            return candidates;
        }

        private static SlideOffset[] BuildOffsets(Box bbox1, Box bbox2, double spacing, double stepSize)
        {
            var offsets = new List<SlideOffset>();

            foreach (var (dirX, dirY) in PushDirections)
            {
                var isHorizontalPush = System.Math.Abs(dirX) > System.Math.Abs(dirY);

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

                var alignedStart = System.Math.Ceiling(perpMin / stepSize) * stepSize;

                // Start on the opposite side of the push direction.
                var pushComponent = isHorizontalPush ? dirX : dirY;
                var startPos = pushComponent < 0 ? pushStartOffset : -pushStartOffset;

                for (var offset = alignedStart; offset <= perpMax; offset += stepSize)
                {
                    var dx = isHorizontalPush ? startPos : offset;
                    var dy = isHorizontalPush ? offset : startPos;
                    offsets.Add(new SlideOffset(dx, dy, dirX, dirY));
                }
            }

            return offsets.ToArray();
        }
    }
}
