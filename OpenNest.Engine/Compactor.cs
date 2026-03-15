using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// Pushes a group of parts left and down to close gaps after placement.
    /// Uses the same directional-distance logic as PlateView.PushSelected
    /// but operates on Part objects directly.
    /// </summary>
    public static class Compactor
    {
        private const double ChordTolerance = 0.001;

        /// <summary>
        /// Compacts movingParts toward the bottom-left of the plate work area.
        /// Everything already on the plate (excluding movingParts) is treated
        /// as stationary obstacles.
        /// </summary>
        private const double RepeatThreshold = 0.01;
        private const int MaxIterations = 20;

        public static void Compact(List<Part> movingParts, Plate plate)
        {
            if (movingParts == null || movingParts.Count == 0)
                return;

            var savedPositions = SavePositions(movingParts);

            // Try left-first.
            var leftFirst = CompactLoop(movingParts, plate, PushDirection.Left, PushDirection.Down);

            // Restore and try down-first.
            RestorePositions(movingParts, savedPositions);
            var downFirst = CompactLoop(movingParts, plate, PushDirection.Down, PushDirection.Left);

            // Keep left-first if it traveled further.
            if (leftFirst > downFirst)
            {
                RestorePositions(movingParts, savedPositions);
                CompactLoop(movingParts, plate, PushDirection.Left, PushDirection.Down);
            }
        }

        private static double CompactLoop(List<Part> parts, Plate plate,
            PushDirection first, PushDirection second)
        {
            var total = 0.0;

            for (var i = 0; i < MaxIterations; i++)
            {
                var a = Push(parts, plate, first);
                var b = Push(parts, plate, second);
                total += a + b;

                if (a <= RepeatThreshold && b <= RepeatThreshold)
                    break;
            }

            return total;
        }

        private static Vector[] SavePositions(List<Part> parts)
        {
            var positions = new Vector[parts.Count];
            for (var i = 0; i < parts.Count; i++)
                positions[i] = parts[i].Location;
            return positions;
        }

        private static void RestorePositions(List<Part> parts, Vector[] positions)
        {
            for (var i = 0; i < parts.Count; i++)
                parts[i].Location = positions[i];
        }

        public static double Push(List<Part> movingParts, Plate plate, PushDirection direction)
        {
            var obstacleParts = plate.Parts
                .Where(p => !movingParts.Contains(p))
                .ToList();

            var obstacleBoxes = new Box[obstacleParts.Count];
            var obstacleLines = new List<Line>[obstacleParts.Count];

            for (var i = 0; i < obstacleParts.Count; i++)
                obstacleBoxes[i] = obstacleParts[i].BoundingBox;

            var opposite = Helper.OppositeDirection(direction);
            var halfSpacing = plate.PartSpacing / 2;
            var isHorizontal = Helper.IsHorizontalDirection(direction);
            var workArea = plate.WorkArea();
            var distance = double.MaxValue;

            foreach (var moving in movingParts)
            {
                var edgeDist = Helper.EdgeDistance(moving.BoundingBox, workArea, direction);
                if (edgeDist > 0 && edgeDist < distance)
                    distance = edgeDist;

                var movingBox = moving.BoundingBox;
                List<Line> movingLines = null;

                for (var i = 0; i < obstacleBoxes.Length; i++)
                {
                    var gap = Helper.DirectionalGap(movingBox, obstacleBoxes[i], direction);
                    if (gap < 0 || gap >= distance)
                        continue;

                    var perpOverlap = isHorizontal
                        ? movingBox.IsHorizontalTo(obstacleBoxes[i], out _)
                        : movingBox.IsVerticalTo(obstacleBoxes[i], out _);

                    if (!perpOverlap)
                        continue;

                    movingLines ??= halfSpacing > 0
                        ? Helper.GetOffsetPartLines(moving, halfSpacing, direction, ChordTolerance)
                        : Helper.GetPartLines(moving, direction, ChordTolerance);

                    obstacleLines[i] ??= halfSpacing > 0
                        ? Helper.GetOffsetPartLines(obstacleParts[i], halfSpacing, opposite, ChordTolerance)
                        : Helper.GetPartLines(obstacleParts[i], opposite, ChordTolerance);

                    var d = Helper.DirectionalDistance(movingLines, obstacleLines[i], direction);
                    if (d < distance)
                        distance = d;
                }
            }

            if (distance < double.MaxValue && distance > 0)
            {
                var offset = Helper.DirectionToOffset(direction, distance);
                foreach (var moving in movingParts)
                    moving.Offset(offset);
                return distance;
            }

            return 0;
        }
    }
}
