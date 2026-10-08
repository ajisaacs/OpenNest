using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;
using OpenNest.Math;

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
            var obstacleParts = plate
                .Parts.Where(p => !movingParts.Contains(p) && !IntersectsAny(p, movingParts))
                .ToList();

            return Push(movingParts, obstacleParts, plate.WorkArea(), plate.PartSpacing, direction);
        }

        /// <summary>
        /// Pushes movingParts along an arbitrary angle (radians, 0 = right, π/2 = up).
        /// </summary>
        public static double Push(List<Part> movingParts, Plate plate, double angle)
        {
            var obstacleParts = plate
                .Parts.Where(p => !movingParts.Contains(p) && !IntersectsAny(p, movingParts))
                .ToList();

            var direction = new Vector(System.Math.Cos(angle), System.Math.Sin(angle));
            return Push(movingParts, obstacleParts, plate.WorkArea(), plate.PartSpacing, direction);
        }

        /// <summary>
        /// Pushes movingParts along an arbitrary angle (radians, 0 = right, π/2 = up).
        /// </summary>
        public static double Push(
            List<Part> movingParts,
            List<Part> obstacleParts,
            Box workArea,
            double partSpacing,
            Vector direction
        )
        {
            var opposite = -direction;

            var obstacleBoxes = new Box[obstacleParts.Count];
            var obstacleSpacingBoxes = new Box[obstacleParts.Count];
            var obstacleEntities = new List<Entity>[obstacleParts.Count];
            var halfSpacing = System.Math.Max(0, partSpacing) / 2;

            for (var i = 0; i < obstacleParts.Count; i++)
            {
                obstacleBoxes[i] = obstacleParts[i].BoundingBox;
                obstacleSpacingBoxes[i] = SpacingBounds(obstacleBoxes[i], halfSpacing);
            }

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

                // Broad-phase bounds must enclose the spacing-offset contours.
                // Raw bounds can miss near passes and overestimate the safe travel.
                var movingSpacingBox = SpacingBounds(movingBox, halfSpacing);
                for (var i = 0; i < obstacleBoxes.Length; i++)
                {
                    var obstacleSpacingBox = obstacleSpacingBoxes[i];
                    var reverseGap = SpatialQuery.DirectionalGap(
                        movingSpacingBox,
                        obstacleSpacingBox,
                        opposite
                    );
                    if (reverseGap > 0)
                        continue;

                    var gap = SpatialQuery.DirectionalGap(
                        movingSpacingBox,
                        obstacleSpacingBox,
                        direction
                    );
                    if (gap >= distance)
                        continue;

                    if (
                        !SpatialQuery.PerpendicularOverlap(
                            movingSpacingBox,
                            obstacleSpacingBox,
                            direction
                        )
                    )
                        continue;

                    movingEntities ??=
                        halfSpacing > 0
                            ? (
                                needCutouts
                                    ? PartGeometry.GetOffsetPartEntities(moving, halfSpacing)
                                    : PartGeometry.GetOffsetPerimeterEntities(moving, halfSpacing)
                            )
                            : (
                                needCutouts
                                    ? PartGeometry.GetPartEntities(moving)
                                    : PartGeometry.GetPerimeterEntities(moving)
                            );

                    // A moving part can be inside an obstacle's cutout. Omitting that
                    // loop would let it cross the inner wall before seeing the perimeter.
                    obstacleEntities[i] ??=
                        halfSpacing > 0
                            ? PartGeometry.GetOffsetPartEntities(obstacleParts[i], halfSpacing)
                            : PartGeometry.GetPartEntities(obstacleParts[i]);

                    // Contacts left by a previous push only block directions that would
                    // push material into material; the kernel classifies them.
                    var d = SpatialQuery.DirectionalDistance(
                        movingEntities,
                        obstacleEntities[i],
                        direction
                    );

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

        private static Box SpacingBounds(Box box, double spacing)
        {
            return new Box(
                box.Left - spacing,
                box.Bottom - spacing,
                box.Length + 2 * spacing,
                box.Width + 2 * spacing
            );
        }

        private static bool IntersectsAny(Part candidate, List<Part> parts)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (!candidate.Intersects(parts[i], out _))
                    continue;

                // Part.Intersects compares outer perimeters only. A valid insert in a
                // cutout must remain an obstacle, not be discarded as already overlapping.
                var a = new ShapeProfile(PartGeometry.GetPartEntities(candidate));
                var b = new ShapeProfile(PartGeometry.GetPartEntities(parts[i]));
                if (a.Cutouts.Count == 0 && b.Cutouts.Count == 0)
                    return true;
                if (Collision.HasOverlap(
                    a.Perimeter.ToPolygonWithTolerance(0.001),
                    b.Perimeter.ToPolygonWithTolerance(0.001),
                    a.Cutouts.Select(hole => hole.ToPolygonWithTolerance(0.001)).ToList(),
                    b.Cutouts.Select(hole => hole.ToPolygonWithTolerance(0.001)).ToList()))
                    return true;
            }
            return false;
        }

        public static double Push(
            List<Part> movingParts,
            List<Part> obstacleParts,
            Box workArea,
            double partSpacing,
            PushDirection direction
        )
        {
            var vector = SpatialQuery.DirectionToOffset(direction, 1.0);
            return Push(movingParts, obstacleParts, workArea, partSpacing, vector);
        }

        /// <summary>
        /// Pushes movingParts using bounding-box distances only (no geometry lines).
        /// Much faster but less precise — use as a coarse positioning pass before
        /// a full geometry Push.
        /// </summary>
        public static double PushBoundingBox(
            List<Part> movingParts,
            Plate plate,
            PushDirection direction
        )
        {
            var obstacleParts = plate
                .Parts.Where(p => !movingParts.Contains(p) && !IntersectsAny(p, movingParts))
                .ToList();

            return PushBoundingBox(
                movingParts,
                obstacleParts,
                plate.WorkArea(),
                plate.PartSpacing,
                direction
            );
        }

        public static double PushBoundingBox(
            List<Part> movingParts,
            List<Part> obstacleParts,
            Box workArea,
            double partSpacing,
            PushDirection direction
        )
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
                    var reverseGap = SpatialQuery.DirectionalGap(
                        movingBox,
                        obstacleBoxes[i],
                        opposite
                    );
                    if (reverseGap > 0)
                        continue;

                    var perpOverlap = isHorizontal
                        ? movingBox.IsHorizontalTo(obstacleBoxes[i], out _)
                        : movingBox.IsVerticalTo(obstacleBoxes[i], out _);

                    if (!perpOverlap)
                        continue;

                    var gap = SpatialQuery.DirectionalGap(movingBox, obstacleBoxes[i], direction);
                    var d = gap - partSpacing - 0.002;
                    if (d < 0)
                        d = 0;
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
        /// Settles a copied placement against the plate in both axis orders, choosing
        /// the group closest to the quadrant's work-area corner. A coarse box pass
        /// runs only when no moving box starts inside an existing part's box.
        /// </summary>
        public static void SettlePlacement(
            List<Part> movingParts,
            Plate plate,
            PushDirection horizontal,
            PushDirection vertical,
            int maxIterations = 20
        )
        {
            if (movingParts.Count == 0)
                return;

            var workArea = plate.WorkArea();
            var skipBoxes = movingParts.Any(moving =>
                plate.Parts.Any(obstacle => moving.BoundingBox.Intersects(obstacle.BoundingBox))
            );
            var bestScore = double.MaxValue;
            Vector[] best = null;

            foreach (var first in new[] { horizontal, vertical })
            {
                var second = first == horizontal ? vertical : horizontal;
                var trial = movingParts.Select(p => (Part)p.Clone()).ToList();
                if (!skipBoxes)
                {
                    PushBoundingBox(trial, plate, first);
                    PushBoundingBox(trial, plate, second);
                }

                for (var i = 0; i < maxIterations; i++)
                {
                    var moved = Push(trial, plate, first) + Push(trial, plate, second);
                    if (moved < 0.01)
                        break;
                }

                var bounds = trial.GetBoundingBox();
                var dx = horizontal == PushDirection.Left
                    ? bounds.Left - workArea.Left : workArea.Right - bounds.Right;
                var dy = vertical == PushDirection.Down
                    ? bounds.Bottom - workArea.Bottom : workArea.Top - bounds.Top;
                var score = dx * dx + dy * dy;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = trial.Select(p => p.Location).ToArray();
                }
            }

            for (var i = 0; i < movingParts.Count; i++)
                movingParts[i].Location = best[i];
        }

        /// <summary>
        /// Repeatedly pushes parts left then down until total movement per
        /// iteration falls below the given threshold.
        /// </summary>
        public static void Settle(
            List<Part> parts,
            Box workArea,
            double partSpacing,
            double threshold = 0.01,
            int maxIterations = 20
        )
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
