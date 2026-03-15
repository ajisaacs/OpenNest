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
            var stationaryParts = plate.Parts
                .Where(p => !movingParts.Contains(p))
                .ToList();

            var stationaryBoxes = new Box[stationaryParts.Count];

            for (var i = 0; i < stationaryParts.Count; i++)
                stationaryBoxes[i] = stationaryParts[i].BoundingBox;

            var stationaryLines = new List<Line>[stationaryParts.Count];
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

                for (var i = 0; i < stationaryBoxes.Length; i++)
                {
                    var gap = Helper.DirectionalGap(movingBox, stationaryBoxes[i], direction);
                    if (gap < 0 || gap >= distance)
                        continue;

                    var perpOverlap = isHorizontal
                        ? movingBox.IsHorizontalTo(stationaryBoxes[i], out _)
                        : movingBox.IsVerticalTo(stationaryBoxes[i], out _);

                    if (!perpOverlap)
                        continue;

                    movingLines ??= halfSpacing > 0
                        ? Helper.GetOffsetPartLines(moving, halfSpacing, direction, ChordTolerance)
                        : Helper.GetPartLines(moving, direction, ChordTolerance);

                    stationaryLines[i] ??= halfSpacing > 0
                        ? Helper.GetOffsetPartLines(stationaryParts[i], halfSpacing, opposite, ChordTolerance)
                        : Helper.GetPartLines(stationaryParts[i], opposite, ChordTolerance);

                    var d = Helper.DirectionalDistance(movingLines, stationaryLines[i], direction);
                    if (d < distance)
                        distance = d;
                }

                if (distance < double.MaxValue && distance > 0)
                {
                    var offset = Helper.DirectionToOffset(direction, distance);
                    moving.Offset(offset);
                }
            }
        }
    }
}
