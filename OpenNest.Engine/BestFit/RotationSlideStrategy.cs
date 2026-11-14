using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.BestFit
{
    public class RotationSlideStrategy : IBestFitStrategy
    {
        /// <summary>Settled positions closer than this to an earlier one are duplicates.</summary>
        private const double PositionResolution = 1e-4;

        private readonly IDistanceComputer _distanceComputer;

        private static readonly (double DirX, double DirY)[] PushDirections =
        {
            (-1, 0), // Left
            (0, -1), // Down
            (1, 0), // Right
            (0, 1), // Up
        };

        public RotationSlideStrategy(
            double part2Rotation,
            int strategyIndex,
            string description,
            IDistanceComputer distanceComputer
        )
        {
            Part2Rotation = part2Rotation;
            StrategyIndex = strategyIndex;
            Description = description;
            _distanceComputer = distanceComputer;
        }

        public double Part2Rotation { get; }
        public int StrategyIndex { get; }
        public string Description { get; }

        public List<PairCandidate> GenerateCandidates(
            Drawing drawing,
            double spacing,
            double stepSize
        )
        {
            var candidates = new List<PairCandidate>();

            var part1 = Part.CreateAtOrigin(drawing);
            var part2Template = Part.CreateAtOrigin(drawing, Part2Rotation);

            var halfSpacing = spacing / 2;
            var part1Entities = PartGeometry.GetOffsetPerimeterEntities(part1, halfSpacing);
            var part2Entities = PartGeometry.GetOffsetPerimeterEntities(part2Template, halfSpacing);

            var bbox1 = part1.BoundingBox;
            var bbox2 = part2Template.BoundingBox;

            var offsets = BuildOffsets(bbox1, bbox2, spacing, stepSize);

            if (offsets.Length == 0)
                return candidates;

            var distances = _distanceComputer.ComputeDistances(
                part1Entities,
                part2Entities,
                offsets
            );

            // Where each slide came to rest, as an offset of the part 2 template.
            var rests = new List<SlideOffset>();

            for (var i = 0; i < offsets.Length; i++)
            {
                var slideDist = distances[i];
                if (slideDist >= double.MaxValue || slideDist < 0)
                    continue;

                rests.Add(
                    new SlideOffset(
                        offsets[i].Dx + offsets[i].DirX * slideDist,
                        offsets[i].Dy + offsets[i].DirY * slideDist,
                        offsets[i].DirX,
                        offsets[i].DirY
                    )
                );
            }

            var testNumber = 0;

            foreach (var rest in rests)
                candidates.Add(CreateCandidate(drawing, part2Template, rest, spacing, testNumber++));

            foreach (var settled in Settle(part1Entities, part2Entities, rests))
                candidates.Add(CreateCandidate(drawing, part2Template, settled, spacing, testNumber++));

            return candidates;
        }

        /// <summary>
        /// Slides every resting position sideways, both ways, until part 2 meets part 1 again.
        /// The first slide starts on the step grid, so it can stop up to a step short of a
        /// corner that locks the pair tighter (two interlocking channels, say); the sideways
        /// slide closes that gap exactly. Positions that cannot move, never meet part 1 again
        /// or repeat an earlier one are dropped.
        /// </summary>
        private List<SlideOffset> Settle(
            List<Entity> part1Entities,
            List<Entity> part2Entities,
            List<SlideOffset> rests
        )
        {
            var settled = new List<SlideOffset>();

            if (rests.Count == 0)
                return settled;

            var sideways = new SlideOffset[rests.Count * 2];

            for (var i = 0; i < rests.Count; i++)
            {
                var rest = rests[i];
                // Perpendicular to the first slide: (x, y) -> (-y, x) and its opposite.
                sideways[i * 2] = new SlideOffset(rest.Dx, rest.Dy, -rest.DirY, rest.DirX);
                sideways[i * 2 + 1] = new SlideOffset(rest.Dx, rest.Dy, rest.DirY, -rest.DirX);
            }

            var distances = _distanceComputer.ComputeDistances(
                part1Entities,
                part2Entities,
                sideways
            );

            var seen = new HashSet<(long X, long Y)>();

            foreach (var rest in rests)
                seen.Add(PositionKey(rest.Dx, rest.Dy));

            for (var i = 0; i < sideways.Length; i++)
            {
                var slideDist = distances[i];
                if (slideDist >= double.MaxValue || slideDist <= Tolerance.Epsilon)
                    continue;

                var dx = sideways[i].Dx + sideways[i].DirX * slideDist;
                var dy = sideways[i].Dy + sideways[i].DirY * slideDist;

                if (seen.Add(PositionKey(dx, dy)))
                    settled.Add(new SlideOffset(dx, dy, sideways[i].DirX, sideways[i].DirY));
            }

            return settled;
        }

        private static (long X, long Y) PositionKey(double dx, double dy) =>
            (
                (long)System.Math.Round(dx / PositionResolution),
                (long)System.Math.Round(dy / PositionResolution)
            );

        private PairCandidate CreateCandidate(
            Drawing drawing,
            Part part2Template,
            SlideOffset rest,
            double spacing,
            int testNumber
        )
        {
            return new PairCandidate
            {
                Drawing = drawing,
                Part1Rotation = 0,
                Part2Rotation = Part2Rotation,
                Part2Offset = new Vector(
                    part2Template.Location.X + rest.Dx,
                    part2Template.Location.Y + rest.Dy
                ),
                StrategyIndex = StrategyIndex,
                TestNumber = testNumber,
                Spacing = spacing,
            };
        }

        private static SlideOffset[] BuildOffsets(
            Box bbox1,
            Box bbox2,
            double spacing,
            double stepSize
        )
        {
            var offsets = new List<SlideOffset>();

            foreach (var (dirX, dirY) in PushDirections)
            {
                var isHorizontalPush = System.Math.Abs(dirX) > System.Math.Abs(dirY);

                double perpMin,
                    perpMax,
                    pushStartOffset;

                if (isHorizontalPush)
                {
                    // Perpendicular sweep along Y → Width; push extent along X → Length
                    // Trim to offsets where the parts overlap by at least 50%.
                    var halfOverlap = bbox2.Width * 0.5;
                    perpMin = -(halfOverlap - spacing);
                    perpMax = bbox1.Width + halfOverlap + spacing;
                    pushStartOffset = bbox1.Length + bbox2.Length + spacing * 2;
                }
                else
                {
                    // Perpendicular sweep along X → Length; push extent along Y → Width
                    var halfOverlap = bbox2.Length * 0.5;
                    perpMin = -(halfOverlap - spacing);
                    perpMax = bbox1.Length + halfOverlap + spacing;
                    pushStartOffset = bbox1.Width + bbox2.Width + spacing * 2;
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
