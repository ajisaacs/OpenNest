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
        public static void Compact(List<Part> movingParts, Plate plate)
        {
            if (movingParts == null || movingParts.Count == 0)
                return;

            Push(movingParts, plate, PushDirection.Left);
            Push(movingParts, plate, PushDirection.Down);
        }

        private static void Push(List<Part> movingParts, Plate plate, PushDirection direction)
        {
            // Start with parts already on the plate (excluding the moving group).
            var obstacleParts = plate.Parts
                .Where(p => !movingParts.Contains(p))
                .ToList();

            var obstacleBoxes = new List<Box>(obstacleParts.Count + movingParts.Count);
            var obstacleLines = new List<List<Line>>(obstacleParts.Count + movingParts.Count);

            for (var i = 0; i < obstacleParts.Count; i++)
            {
                obstacleBoxes.Add(obstacleParts[i].BoundingBox);
                obstacleLines.Add(null); // lazy
            }

            var opposite = Helper.OppositeDirection(direction);
            var halfSpacing = plate.PartSpacing / 2;
            var isHorizontal = Helper.IsHorizontalDirection(direction);
            var workArea = plate.WorkArea();

            foreach (var moving in movingParts)
            {
                var distance = double.MaxValue;
                var movingBox = moving.BoundingBox;

                // Plate edge distance.
                var edgeDist = Helper.EdgeDistance(movingBox, workArea, direction);
                if (edgeDist > 0 && edgeDist < distance)
                    distance = edgeDist;

                List<Line> movingLines = null;

                for (var i = 0; i < obstacleBoxes.Count; i++)
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

                    var obstaclePart = i < obstacleParts.Count ? obstacleParts[i] : null;

                    obstacleLines[i] ??= obstaclePart != null
                        ? (halfSpacing > 0
                            ? Helper.GetOffsetPartLines(obstaclePart, halfSpacing, opposite, ChordTolerance)
                            : Helper.GetPartLines(obstaclePart, opposite, ChordTolerance))
                        : (halfSpacing > 0
                            ? Helper.GetOffsetPartLines(moving, halfSpacing, opposite, ChordTolerance)
                            : Helper.GetPartLines(moving, opposite, ChordTolerance));

                    var d = Helper.DirectionalDistance(movingLines, obstacleLines[i], direction);
                    if (d < distance)
                        distance = d;
                }

                if (distance < double.MaxValue && distance > 0)
                {
                    var offset = Helper.DirectionToOffset(direction, distance);
                    moving.Offset(offset);
                }

                // This part is now an obstacle for subsequent moving parts.
                obstacleBoxes.Add(moving.BoundingBox);
                obstacleParts.Add(moving);
                obstacleLines.Add(null); // will be lazily computed if needed
            }
        }
    }
}
