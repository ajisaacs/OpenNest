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
        /// <summary>
        /// The plate's clamp keep-outs (zones grown square by the clamp clearance) as static push
        /// obstacles (C1), held at zero spacing rather than the part spacing. Unlike ordinary already-overlapping parts,
        /// a clamp stays a wall even when a selected member straddles it: dropping it would
        /// let other clear members move into the zone. Empty zone lists change every
        /// caller's arithmetic by nothing (C2).
        /// </summary>
        private static ObstacleSet ZoneObstacles(Plate plate) =>
            new(Jobs.InteractiveClamps.ObstacleParts(plate), 0);

        public static double Push(List<Part> movingParts, Plate plate, PushDirection direction) =>
            Push(movingParts, plate, SpatialQuery.DirectionToOffset(direction, 1.0));

        /// <summary>
        /// Pushes movingParts along an arbitrary angle (radians, 0 = right, π/2 = up).
        /// </summary>
        public static double Push(List<Part> movingParts, Plate plate, double angle) =>
            Push(movingParts, plate, new Vector(System.Math.Cos(angle), System.Math.Sin(angle)));

        private static double Push(List<Part> movingParts, Plate plate, Vector direction)
        {
            var obstacleParts = plate.Parts
                .Where(p => !movingParts.Contains(p) && !IntersectsAny(p, movingParts))
                .ToList();

            return Push(movingParts, plate.WorkArea(), direction,
                new ObstacleSet(obstacleParts, plate.PartSpacing), ZoneObstacles(plate));
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
        ) => Push(movingParts, workArea, direction, new ObstacleSet(obstacleParts, partSpacing));

        /// <summary>
        /// Pushes movingParts to the first contact with the work-area edge or an obstacle,
        /// keeping each obstacle set's own spacing.
        /// </summary>
        private static double Push(
            List<Part> movingParts,
            Box workArea,
            Vector direction,
            params ObstacleSet[] obstacleSets
        )
        {
            var opposite = -direction;
            var distance = double.MaxValue;

            foreach (var moving in movingParts)
            {
                var edgeDist = SpatialQuery.EdgeDistance(moving.BoundingBox, workArea, direction);
                if (edgeDist <= 0)
                    distance = 0;
                else if (edgeDist < distance)
                    distance = edgeDist;

                foreach (var set in obstacleSets)
                    distance = set.Travel(moving, direction, opposite, distance);
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

        /// <summary>
        /// Obstacles kept at one spacing, with their bounds and spacing-offset contours
        /// cached for one push.
        /// </summary>
        private sealed class ObstacleSet
        {
            private readonly List<Part> parts;
            private readonly Box[] spacingBoxes;
            private readonly List<Entity>[] entities;
            private readonly double halfSpacing;

            public ObstacleSet(List<Part> parts, double spacing)
            {
                this.parts = parts;
                Spacing = System.Math.Max(0, spacing);
                halfSpacing = Spacing / 2;
                Boxes = new Box[parts.Count];
                spacingBoxes = new Box[parts.Count];
                entities = new List<Entity>[parts.Count];
                for (var i = 0; i < parts.Count; i++)
                {
                    Boxes[i] = parts[i].BoundingBox;
                    spacingBoxes[i] = SpacingBounds(Boxes[i], halfSpacing);
                }
            }

            public double Spacing { get; }

            public Box[] Boxes { get; }

            /// <summary>The shorter of <paramref name="distance"/> and how far
            /// <paramref name="moving"/> travels before it comes within the spacing.</summary>
            public double Travel(Part moving, Vector direction, Vector opposite, double distance)
            {
                var movingBox = moving.BoundingBox;
                List<Entity> movingEntities = null;

                // Check if any obstacle is inside the moving part — only then
                // do we need cutout entities on the moving part.
                var needCutouts = false;
                for (var i = 0; i < Boxes.Length; i++)
                {
                    if (movingBox.Contains(Boxes[i]))
                    {
                        needCutouts = true;
                        break;
                    }
                }

                // Broad-phase bounds must enclose the spacing-offset contours.
                // Raw bounds can miss near passes and overestimate the safe travel.
                var movingSpacingBox = SpacingBounds(movingBox, halfSpacing);
                for (var i = 0; i < Boxes.Length; i++)
                {
                    var obstacleSpacingBox = spacingBoxes[i];
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
                    entities[i] ??=
                        halfSpacing > 0
                            ? PartGeometry.GetOffsetPartEntities(parts[i], halfSpacing)
                            : PartGeometry.GetPartEntities(parts[i]);

                    // Contacts left by a previous push only block directions that would
                    // push material into material; the kernel classifies them.
                    var d = SpatialQuery.DirectionalDistance(
                        movingEntities,
                        entities[i],
                        direction
                    );

                    if (d < distance)
                        distance = d;
                }

                return distance;
            }
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
            var obstacleParts = plate.Parts
                .Where(p => !movingParts.Contains(p) && !IntersectsAny(p, movingParts))
                .ToList();
            var zones = ZoneObstacles(plate);

            return PushBoundingBox(
                movingParts,
                plate.WorkArea(),
                direction,
                (BoundingBoxes(obstacleParts), plate.PartSpacing),
                (zones.Boxes, zones.Spacing)
            );
        }

        public static double PushBoundingBox(
            List<Part> movingParts,
            List<Part> obstacleParts,
            Box workArea,
            double partSpacing,
            PushDirection direction
        ) => PushBoundingBox(movingParts, workArea, direction, (BoundingBoxes(obstacleParts), partSpacing));

        private static Box[] BoundingBoxes(List<Part> parts)
        {
            var boxes = new Box[parts.Count];
            for (var i = 0; i < parts.Count; i++)
                boxes[i] = parts[i].BoundingBox;
            return boxes;
        }

        private static double PushBoundingBox(
            List<Part> movingParts,
            Box workArea,
            PushDirection direction,
            params (Box[] Boxes, double Spacing)[] obstacleSets
        )
        {
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

                foreach (var (obstacleBoxes, spacing) in obstacleSets)
                {
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
                        var d = gap - spacing - 0.002;
                        if (d < 0)
                            d = 0;
                        if (d < distance)
                            distance = d;
                    }
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

        /// <summary>
        /// As <see cref="Settle(List{Part}, Box, double, double, int)"/>, settling inside the
        /// plate's work area with its clamp zones as static obstacles: settling never parks
        /// a part inside a keep-out (C1), and a clear cluster still compacts against zones
        /// like any other obstacle. With no zones this behaves exactly like settling in the
        /// plate's work area (C2).
        /// </summary>
        public static void Settle(
            List<Part> parts,
            Plate plate,
            double threshold = 0.01,
            int maxIterations = 20
        ) => Settle(parts, plate, plate.WorkArea(), threshold, maxIterations);

        /// <summary>Keep a fill group's own work-area bounds while excluding plate clamps.</summary>
        public static void Settle(
            List<Part> parts,
            Plate plate,
            Box workArea,
            double threshold = 0.01,
            int maxIterations = 20
        )
        {
            if (parts.Count < 2)
                return;

            var obstacles = plate.Parts.Where(part => !parts.Contains(part)
                && !part.BaseDrawing.IsCutOff).ToList();
            var zones = Jobs.InteractiveClamps.ObstacleParts(plate);

            for (var i = 0; i < maxIterations; i++)
            {
                var moved = 0.0;
                foreach (var direction in new[] { PushDirection.Left, PushDirection.Down })
                    moved += Push(parts, workArea, SpatialQuery.DirectionToOffset(direction, 1.0),
                        new ObstacleSet(obstacles, plate.PartSpacing),
                        new ObstacleSet(zones, 0));

                if (moved < threshold)
                    break;
            }
        }
    }
}
