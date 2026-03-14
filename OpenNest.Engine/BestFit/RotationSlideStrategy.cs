using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.BestFit
{
    public class RotationSlideStrategy : IBestFitStrategy
    {
        private readonly ISlideComputer _slideComputer;

        private static readonly PushDirection[] AllDirections =
        {
            PushDirection.Left, PushDirection.Down, PushDirection.Right, PushDirection.Up
        };

        public RotationSlideStrategy(double part2Rotation, int type, string description,
            ISlideComputer slideComputer = null)
        {
            Part2Rotation = part2Rotation;
            Type = type;
            Description = description;
            _slideComputer = slideComputer;
        }

        public double Part2Rotation { get; }
        public int Type { get; }
        public string Description { get; }

        public List<PairCandidate> GenerateCandidates(Drawing drawing, double spacing, double stepSize)
        {
            var candidates = new List<PairCandidate>();

            var part1 = Part.CreateAtOrigin(drawing);
            var part2Template = Part.CreateAtOrigin(drawing, Part2Rotation);

            var halfSpacing = spacing / 2;
            var part1Lines = Helper.GetOffsetPartLines(part1, halfSpacing);
            var part2TemplateLines = Helper.GetOffsetPartLines(part2Template, halfSpacing);

            var bbox1 = part1.BoundingBox;
            var bbox2 = part2Template.BoundingBox;

            // Collect offsets and directions across all 4 axes
            var allDx = new List<double>();
            var allDy = new List<double>();
            var allDirs = new List<PushDirection>();

            foreach (var pushDir in AllDirections)
                BuildOffsets(bbox1, bbox2, spacing, stepSize, pushDir, allDx, allDy, allDirs);

            if (allDx.Count == 0)
                return candidates;

            // Compute all distances — single GPU dispatch or CPU loop
            var distances = ComputeAllDistances(
                part1Lines, part2TemplateLines, allDx, allDy, allDirs);

            // Create candidates from valid results
            var testNumber = 0;

            for (var i = 0; i < allDx.Count; i++)
            {
                var slideDist = distances[i];
                if (slideDist >= double.MaxValue || slideDist < 0)
                    continue;

                var dx = allDx[i];
                var dy = allDy[i];
                var pushVector = GetPushVector(allDirs[i], slideDist);
                var finalPosition = new Vector(
                    part2Template.Location.X + dx + pushVector.X,
                    part2Template.Location.Y + dy + pushVector.Y);

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

            return candidates;
        }

        private static void BuildOffsets(
            Box bbox1, Box bbox2, double spacing, double stepSize,
            PushDirection pushDir, List<double> allDx, List<double> allDy,
            List<PushDirection> allDirs)
        {
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

            var alignedStart = System.Math.Ceiling(perpMin / stepSize) * stepSize;
            var isPositiveStart = pushDir == PushDirection.Left || pushDir == PushDirection.Down;
            var startPos = isPositiveStart ? pushStartOffset : -pushStartOffset;

            for (var offset = alignedStart; offset <= perpMax; offset += stepSize)
            {
                allDx.Add(isHorizontalPush ? startPos : offset);
                allDy.Add(isHorizontalPush ? offset : startPos);
                allDirs.Add(pushDir);
            }
        }

        private double[] ComputeAllDistances(
            List<Line> part1Lines, List<Line> part2TemplateLines,
            List<double> allDx, List<double> allDy, List<PushDirection> allDirs)
        {
            var count = allDx.Count;

            if (_slideComputer != null)
            {
                var stationarySegments = Helper.FlattenLines(part1Lines);
                var movingSegments = Helper.FlattenLines(part2TemplateLines);
                var offsets = new double[count * 2];
                var directions = new int[count];

                for (var i = 0; i < count; i++)
                {
                    offsets[i * 2] = allDx[i];
                    offsets[i * 2 + 1] = allDy[i];
                    directions[i] = (int)allDirs[i];
                }

                return _slideComputer.ComputeBatchMultiDir(
                    stationarySegments, part1Lines.Count,
                    movingSegments, part2TemplateLines.Count,
                    offsets, count, directions);
            }

            var results = new double[count];

            for (var i = 0; i < count; i++)
            {
                results[i] = Helper.DirectionalDistance(
                    part2TemplateLines, allDx[i], allDy[i], part1Lines, allDirs[i]);
            }

            return results;
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
    }
}
