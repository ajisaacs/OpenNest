using OpenNest.Geometry;
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Fill
{
    /// <summary>
    /// Pushes a group of parts left and down to close gaps after placement.
    /// Uses the same directional-distance logic as PlateView.PushSelected
    /// but operates on Part objects directly.
    /// </summary>
    public static class Compactor
    {
        public static double Push(List<Part> movingParts, Plate plate, PushDirection direction)
        {
            var obstacleParts = plate.Parts
                .Where(p => !movingParts.Contains(p))
                .ToList();

            return Push(movingParts, obstacleParts, plate.WorkArea(), plate.PartSpacing, direction);
        }

        /// <summary>
        /// Pushes movingParts along an arbitrary angle (radians, 0 = right, π/2 = up).
        /// </summary>
        public static double Push(List<Part> movingParts, Plate plate, double angle)
        {
            var obstacleParts = plate.Parts
                .Where(p => !movingParts.Contains(p))
                .ToList();

            var direction = new Vector(System.Math.Cos(angle), System.Math.Sin(angle));
            return Push(movingParts, obstacleParts, plate.WorkArea(), plate.PartSpacing, direction);
        }

        /// <summary>
        /// Pushes movingParts along an arbitrary angle (radians, 0 = right, π/2 = up).
        /// </summary>
        public static double Push(List<Part> movingParts, List<Part> obstacleParts,
            Box workArea, double partSpacing, Vector direction)
        {
            var opposite = -direction;

            var obstacleBoxes = new Box[obstacleParts.Count];
            var obstacleEntities = new List<Entity>[obstacleParts.Count];

            for (var i = 0; i < obstacleParts.Count; i++)
                obstacleBoxes[i] = obstacleParts[i].BoundingBox;

            var halfSpacing = partSpacing / 2;
            var distance = double.MaxValue;

            foreach (var moving in movingParts)
            {
                var edgeDist = SpatialQuery.EdgeDistance(moving.BoundingBox, workArea, direction);
                if (edgeDist <= 0)
                    distance = 0;
                else if (edgeDist < distance)
                    distance = edgeDist;

                var movingBox = moving.BoundingBox;
                List<Entity> movingEntities = null;

                // Check if any obstacle is inside the moving part — only then
                // do we need cutout entities on the moving part.
                var needCutouts = false;
                for (var i = 0; i < obstacleBoxes.Length; i++)
                {
                    if (movingBox.Contains(obstacleBoxes[i]))
                    {
                        needCutouts = true;
                        break;
                    }
                }

                for (var i = 0; i < obstacleBoxes.Length; i++)
                {
                    var reverseGap = SpatialQuery.DirectionalGap(movingBox, obstacleBoxes[i], opposite);
                    if (reverseGap > 0)
                        continue;

                    var gap = SpatialQuery.DirectionalGap(movingBox, obstacleBoxes[i], direction);
                    if (gap >= distance)
                        continue;

                    if (!SpatialQuery.PerpendicularOverlap(movingBox, obstacleBoxes[i], direction))
                        continue;

                    movingEntities ??= halfSpacing > 0
                        ? (needCutouts
                            ? PartGeometry.GetOffsetPartEntities(moving, halfSpacing)
                            : PartGeometry.GetOffsetPerimeterEntities(moving, halfSpacing))
                        : (needCutouts
                            ? PartGeometry.GetPartEntities(moving)
                            : PartGeometry.GetPerimeterEntities(moving));

                    obstacleEntities[i] ??= halfSpacing > 0
                        ? PartGeometry.GetOffsetPerimeterEntities(obstacleParts[i], halfSpacing)
                        : PartGeometry.GetPerimeterEntities(obstacleParts[i]);

                    var d = SpatialQuery.DirectionalDistance(movingEntities, obstacleEntities[i], direction);
                    if (d < distance)
                        distance = d;
                }
            }

            if (distance < double.MaxValue && distance > 0)
            {
                var offset = direction * distance;
                foreach (var moving in movingParts)
                    moving.Offset(offset);
                return distance;
            }

            return 0;
        }

        public static double Push(List<Part> movingParts, List<Part> obstacleParts,
            Box workArea, double partSpacing, PushDirection direction)
        {
            var vector = SpatialQuery.DirectionToOffset(direction, 1.0);
            return Push(movingParts, obstacleParts, workArea, partSpacing, vector);
        }

        /// <summary>
        /// Pushes movingParts using bounding-box distances only (no geometry lines).
        /// Much faster but less precise — use as a coarse positioning pass before
        /// a full geometry Push.
        /// </summary>
        public static double PushBoundingBox(List<Part> movingParts, Plate plate, PushDirection direction)
        {
            var obstacleParts = plate.Parts
                .Where(p => !movingParts.Contains(p))
                .ToList();

            return PushBoundingBox(movingParts, obstacleParts, plate.WorkArea(), plate.PartSpacing, direction);
        }

        public static double PushBoundingBox(List<Part> movingParts, List<Part> obstacleParts,
            Box workArea, double partSpacing, PushDirection direction)
        {
            var obstacleBoxes = new Box[obstacleParts.Count];
            for (var i = 0; i < obstacleParts.Count; i++)
                obstacleBoxes[i] = obstacleParts[i].BoundingBox;

            var opposite = SpatialQuery.OppositeDirection(direction);
            var isHorizontal = SpatialQuery.IsHorizontalDirection(direction);
            var distance = double.MaxValue;

            foreach (var moving in movingParts)
            {
                var edgeDist = SpatialQuery.EdgeDistance(moving.BoundingBox, workArea, direction);
                if (edgeDist <= 0)
                    distance = 0;
                else if (edgeDist < distance)
                    distance = edgeDist;

                var movingBox = moving.BoundingBox;

                for (var i = 0; i < obstacleBoxes.Length; i++)
                {
                    var reverseGap = SpatialQuery.DirectionalGap(movingBox, obstacleBoxes[i], opposite);
                    if (reverseGap > 0)
                        continue;

                    var perpOverlap = isHorizontal
                        ? movingBox.IsHorizontalTo(obstacleBoxes[i], out _)
                        : movingBox.IsVerticalTo(obstacleBoxes[i], out _);

                    if (!perpOverlap)
                        continue;

                    var gap = SpatialQuery.DirectionalGap(movingBox, obstacleBoxes[i], direction);
                    var d = gap - partSpacing - 0.002;
                    if (d < 0) d = 0;
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
        /// Repeatedly pushes parts left then down until total movement per
        /// iteration falls below the given threshold.
        /// </summary>
        public static void Settle(List<Part> parts, Box workArea, double partSpacing,
            double threshold = 0.01, int maxIterations = 20)
        {
            if (parts.Count < 2)
                return;

            var noObstacles = new List<Part>();

            for (var i = 0; i < maxIterations; i++)
            {
                var moved = 0.0;
                moved += Push(parts, noObstacles, workArea, partSpacing, PushDirection.Left);
                moved += Push(parts, noObstacles, workArea, partSpacing, PushDirection.Down);

                if (moved < threshold)
                    break;
            }
        }
    }
}
