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

            return Push(movingParts, obstacleParts, plate.WorkArea(), plate.PartSpacing, direction);
        }

        public static double Push(List<Part> movingParts, List<Part> obstacleParts,
            Box workArea, double partSpacing, PushDirection direction)
        {
            var obstacleBoxes = new Box[obstacleParts.Count];
            var obstacleLines = new List<Line>[obstacleParts.Count];

            for (var i = 0; i < obstacleParts.Count; i++)
                obstacleBoxes[i] = obstacleParts[i].BoundingBox;

            var opposite = SpatialQuery.OppositeDirection(direction);
            var halfSpacing = partSpacing / 2;
            var isHorizontal = SpatialQuery.IsHorizontalDirection(direction);
            var distance = double.MaxValue;

            // BB gap at which offset geometries are expected to be touching.
            var contactGap = (halfSpacing + ChordTolerance) * 2;

            foreach (var moving in movingParts)
            {
                var edgeDist = SpatialQuery.EdgeDistance(moving.BoundingBox, workArea, direction);
                if (edgeDist <= 0)
                    distance = 0;
                else if (edgeDist < distance)
                    distance = edgeDist;

                var movingBox = moving.BoundingBox;
                List<Line> movingLines = null;

                for (var i = 0; i < obstacleBoxes.Length; i++)
                {
                    // Use the reverse-direction gap to check if the obstacle is entirely
                    // behind the moving part. The forward gap (gap < 0) is unreliable for
                    // irregular shapes whose bounding boxes overlap even when the actual
                    // geometry still has a valid contact in the push direction.
                    var reverseGap = SpatialQuery.DirectionalGap(movingBox, obstacleBoxes[i], opposite);
                    if (reverseGap > 0)
                        continue;

                    var gap = SpatialQuery.DirectionalGap(movingBox, obstacleBoxes[i], direction);
                    if (gap >= distance)
                        continue;

                    var perpOverlap = isHorizontal
                        ? movingBox.IsHorizontalTo(obstacleBoxes[i], out _)
                        : movingBox.IsVerticalTo(obstacleBoxes[i], out _);

                    if (!perpOverlap)
                        continue;

                    movingLines ??= halfSpacing > 0
                        ? PartGeometry.GetOffsetPartLines(moving, halfSpacing, direction, ChordTolerance)
                        : PartGeometry.GetPartLines(moving, direction, ChordTolerance);

                    obstacleLines[i] ??= halfSpacing > 0
                        ? PartGeometry.GetOffsetPartLines(obstacleParts[i], halfSpacing, opposite, ChordTolerance)
                        : PartGeometry.GetPartLines(obstacleParts[i], opposite, ChordTolerance);

                    var d = SpatialQuery.DirectionalDistance(movingLines, obstacleLines[i], direction);
                    if (d < distance)
                        distance = d;
                }
            }

            if (distance < double.MaxValue && distance > 0)
            {
                var offset = SpatialQuery.DirectionToOffset(direction, distance);
                foreach (var moving in movingParts)
                    moving.Offset(offset);
                return distance;
            }

            return 0;
        }

        /// <summary>
        /// Compacts parts individually toward the bottom-left of the work area.
        /// Each part is pushed against all others as obstacles, closing geometry-based gaps.
        /// Does not require parts to be on a plate.
        /// </summary>
        public static void CompactIndividual(List<Part> parts, Box workArea, double partSpacing)
        {
            if (parts == null || parts.Count < 2)
                return;

            var savedPositions = SavePositions(parts);

            var leftFirst = CompactIndividualLoop(parts, workArea, partSpacing,
                PushDirection.Left, PushDirection.Down);

            RestorePositions(parts, savedPositions);
            var downFirst = CompactIndividualLoop(parts, workArea, partSpacing,
                PushDirection.Down, PushDirection.Left);

            if (leftFirst > downFirst)
            {
                RestorePositions(parts, savedPositions);
                CompactIndividualLoop(parts, workArea, partSpacing,
                    PushDirection.Left, PushDirection.Down);
            }
        }

        private static double CompactIndividualLoop(List<Part> parts, Box workArea,
            double partSpacing, PushDirection first, PushDirection second)
        {
            var total = 0.0;

            for (var pass = 0; pass < MaxIterations; pass++)
            {
                var moved = 0.0;

                foreach (var part in parts)
                {
                    var single = new List<Part>(1) { part };
                    var obstacles = new List<Part>(parts.Count - 1);
                    foreach (var p in parts)
                        if (p != part) obstacles.Add(p);

                    moved += Push(single, obstacles, workArea, partSpacing, first);
                    moved += Push(single, obstacles, workArea, partSpacing, second);
                }

                total += moved;
                if (moved <= RepeatThreshold)
                    break;
            }

            return total;
        }
    }
}
