using OpenNest.Geometry;
using OpenNest.Math;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

namespace OpenNest.Engine.Fill
{
    public class FillLinear
    {
        public FillLinear(Box workArea, double partSpacing)
        {
            PartSpacing = partSpacing;
            WorkArea = new Box(workArea.X, workArea.Y, workArea.Length, workArea.Width);
        }

        public Box WorkArea { get; }

        public double PartSpacing { get; }

        public double HalfSpacing => PartSpacing / 2;

        /// <summary>
        /// Diagnostic label set by callers to identify the engine/context in overlap logs.
        /// </summary>
        public string Label { get; set; }

        private static Vector MakeOffset(NestDirection direction, double distance)
        {
            return direction == NestDirection.Horizontal
                ? new Vector(distance, 0)
                : new Vector(0, distance);
        }

        private static PushDirection GetPushDirection(NestDirection direction)
        {
            return direction == NestDirection.Horizontal
                ? PushDirection.Left
                : PushDirection.Down;
        }

        private static double GetDimension(Box box, NestDirection direction)
        {
            return direction == NestDirection.Horizontal ? box.Length : box.Width;
        }

        private static double GetStart(Box box, NestDirection direction)
        {
            return direction == NestDirection.Horizontal ? box.Left : box.Bottom;
        }

        private double GetLimit(NestDirection direction)
        {
            return direction == NestDirection.Horizontal ? WorkArea.Right : WorkArea.Top;
        }

        private static NestDirection PerpendicularAxis(NestDirection direction)
        {
            return direction == NestDirection.Horizontal
                ? NestDirection.Vertical
                : NestDirection.Horizontal;
        }

        /// <summary>
        /// Finds the geometry-aware copy distance between two identical parts along an axis.
        /// Uses native Line/Arc entities (inflated by half-spacing) so curves are handled
        /// exactly without polygon sampling error.
        /// </summary>
        private double FindCopyDistance(Part partA, NestDirection direction)
        {
            var bboxDim = GetDimension(partA.BoundingBox, direction);
            var pushDir = GetPushDirection(direction);
            var startOffset = bboxDim + PartSpacing + Tolerance.Epsilon;
            var offset = MakeOffset(direction, startOffset);

            var stationaryEntities = PartGeometry.GetOffsetPerimeterEntities(partA, HalfSpacing);
            var movingEntities = PartGeometry.GetOffsetPerimeterEntities(
                partA.CloneAtOffset(offset), HalfSpacing);

            var slideDistance = SpatialQuery.DirectionalDistance(
                movingEntities, stationaryEntities, pushDir);

            if (slideDistance >= double.MaxValue || slideDistance < 0)
                return bboxDim + PartSpacing;

            return startOffset - slideDistance;
        }

        /// <summary>
        /// Finds the geometry-aware copy distance between two identical patterns along an axis.
        /// Checks every pair of parts across adjacent pattern copies so multi-part patterns
        /// (e.g. interlocking pairs) maintain spacing between ALL parts. Uses native entity
        /// geometry inflated by half-spacing — same primitive the Compactor uses — so arcs
        /// are exact and no bbox clamp is needed.
        /// </summary>
        private double FindPatternCopyDistance(Pattern patternA, NestDirection direction)
        {
            if (patternA.Parts.Count == 1)
                return FindCopyDistance(patternA.Parts[0], direction);

            var bboxDim = GetDimension(patternA.BoundingBox, direction);
            var pushDir = GetPushDirection(direction);
            var opposite = SpatialQuery.OppositeDirection(pushDir);
            var dirVec = SpatialQuery.DirectionToOffset(pushDir, 1.0);

            // bboxDim already spans max(upper) - min(lower) across all parts,
            // so the start offset just needs to push beyond that plus spacing.
            var startOffset = bboxDim + PartSpacing + Tolerance.Epsilon;
            var offset = MakeOffset(direction, startOffset);

            var parts = patternA.Parts;
            var stationaryBoxes = new Box[parts.Count];
            var movingBoxes = new Box[parts.Count];
            var stationaryEntities = new List<Entity>[parts.Count];
            var movingEntities = new List<Entity>[parts.Count];

            for (var i = 0; i < parts.Count; i++)
            {
                stationaryBoxes[i] = parts[i].BoundingBox;
                movingBoxes[i] = stationaryBoxes[i].Translate(offset);
            }

            var maxCopyDistance = 0.0;

            for (var j = 0; j < parts.Count; j++)
            {
                var movingBox = movingBoxes[j];

                for (var i = 0; i < parts.Count; i++)
                {
                    var stationaryBox = stationaryBoxes[i];

                    // Skip if stationary is already ahead of moving in the push direction
                    // (sliding forward would take them further apart).
                    if (SpatialQuery.DirectionalGap(movingBox, stationaryBox, opposite) > 0)
                        continue;

                    // Skip if bboxes can't overlap along the axis perpendicular to the push.
                    if (!SpatialQuery.PerpendicularOverlap(movingBox, stationaryBox, dirVec))
                        continue;

                    stationaryEntities[i] ??= PartGeometry.GetOffsetPerimeterEntities(
                        parts[i], HalfSpacing);
                    movingEntities[j] ??= PartGeometry.GetOffsetPerimeterEntities(
                        parts[j].CloneAtOffset(offset), HalfSpacing);

                    var slideDistance = SpatialQuery.DirectionalDistance(
                        movingEntities[j], stationaryEntities[i], pushDir);

                    if (slideDistance >= double.MaxValue || slideDistance < 0)
                        continue;

                    var copyDist = startOffset - slideDistance;

                    if (copyDist > maxCopyDistance)
                        maxCopyDistance = copyDist;
                }
            }

            return maxCopyDistance;
        }

        /// <summary>
        /// Tiles a pattern along the given axis, returning the cloned parts
        /// (does not include the original pattern's parts). For multi-part
        /// patterns, also adds individual parts from the next incomplete copy
        /// that still fit within the work area.
        /// </summary>
        private List<Part> TilePattern(Pattern basePattern, NestDirection direction)
        {
            var copyDistance = FindPatternCopyDistance(basePattern, direction);

            if (copyDistance <= 0)
                return new List<Part>();

            var dim = GetDimension(basePattern.BoundingBox, direction);
            var start = GetStart(basePattern.BoundingBox, direction);
            var limit = GetLimit(direction);

            var estimatedCopies = (int)((limit - start - dim) / copyDistance);
            var result = new List<Part>(estimatedCopies * basePattern.Parts.Count);

            var count = 1;

            while (true)
            {
                var nextPos = start + copyDistance * count;

                if (nextPos + dim > limit + Tolerance.Epsilon)
                    break;

                var offset = MakeOffset(direction, copyDistance * count);

                foreach (var part in basePattern.Parts)
                    result.Add(part.CloneAtOffset(offset));

                count++;
            }

            // For multi-part patterns, try to place individual parts from the
            // next copy that didn't fit as a whole. This handles cases where
            // e.g. a 2-part pair only partially fits — one part may still be
            // within the work area even though the full pattern exceeds it.
            if (basePattern.Parts.Count > 1)
            {
                var offset = MakeOffset(direction, copyDistance * count);

                foreach (var basePart in basePattern.Parts)
                {
                    var part = basePart.CloneAtOffset(offset);

                    if (part.BoundingBox.Right <= WorkArea.Right + Tolerance.Epsilon &&
                        part.BoundingBox.Top <= WorkArea.Top + Tolerance.Epsilon &&
                        part.BoundingBox.Left >= WorkArea.Left - Tolerance.Epsilon &&
                        part.BoundingBox.Bottom >= WorkArea.Bottom - Tolerance.Epsilon)
                    {
                        result.Add(part);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Fallback tiling using bounding-box spacing when geometry-aware tiling
        /// produces overlapping parts.
        /// </summary>
        private List<Part> TilePatternBbox(Pattern basePattern, NestDirection direction)
        {
            var copyDistance = GetDimension(basePattern.BoundingBox, direction) + PartSpacing;

            if (copyDistance <= 0)
                return new List<Part>();

            var dim = GetDimension(basePattern.BoundingBox, direction);
            var start = GetStart(basePattern.BoundingBox, direction);
            var limit = GetLimit(direction);

            var result = new List<Part>();
            var count = 1;

            while (true)
            {
                var nextPos = start + copyDistance * count;

                if (nextPos + dim > limit + Tolerance.Epsilon)
                    break;

                var offset = MakeOffset(direction, copyDistance * count);

                foreach (var part in basePattern.Parts)
                    result.Add(part.CloneAtOffset(offset));

                count++;
            }

            return result;
        }

        private static bool HasOverlappingParts(List<Part> parts, out int overlapA, out int overlapB)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                var b1 = parts[i].BoundingBox;

                for (var j = i + 1; j < parts.Count; j++)
                {
                    var b2 = parts[j].BoundingBox;

                    var overlapX = System.Math.Min(b1.Right, b2.Right)
                                 - System.Math.Max(b1.Left, b2.Left);
                    var overlapY = System.Math.Min(b1.Top, b2.Top)
                                 - System.Math.Max(b1.Bottom, b2.Bottom);

                    if (overlapX <= Tolerance.Epsilon || overlapY <= Tolerance.Epsilon)
                        continue;

                    if (parts[i].Intersects(parts[j], out _))
                    {
                        overlapA = i;
                        overlapB = j;
                        return true;
                    }
                }
            }

            overlapA = -1;
            overlapB = -1;
            return false;
        }

        /// <summary>
        /// Creates a seed pattern containing a single part positioned at the work area origin.
        /// Returns an empty pattern if the part does not fit.
        /// </summary>
        private Pattern MakeSeedPattern(Drawing drawing, double rotationAngle)
        {
            var pattern = new Pattern();

            var template = new Part(drawing);

            if (!rotationAngle.IsEqualTo(0))
                template.Rotate(rotationAngle);

            template.Offset(WorkArea.Location - template.BoundingBox.Location);

            if (template.BoundingBox.Width > WorkArea.Width + Tolerance.Epsilon ||
                template.BoundingBox.Length > WorkArea.Length + Tolerance.Epsilon)
                return pattern;

            pattern.Parts.Add(template);
            pattern.UpdateBounds();
            return pattern;
        }

        /// <summary>
        /// Fills the work area by tiling the pattern along the primary axis to form
        /// a row, then tiling that row along the perpendicular axis to form a grid.
        /// After the grid is formed, fills the remaining strip with individual parts.
        /// </summary>
        private List<Part> FillGrid(Pattern pattern, NestDirection direction)
        {
            var perpAxis = PerpendicularAxis(direction);

            // Step 1: Tile along primary axis
            var row = new List<Part>(pattern.Parts);
            row.AddRange(TilePattern(pattern, direction));

            if (pattern.Parts.Count > 1 && HasOverlappingParts(row, out var a1, out var b1))
            {
                LogOverlap("Step1-Primary", direction, pattern, row, a1, b1);
                row = new List<Part>(pattern.Parts);
                row.AddRange(TilePatternBbox(pattern, direction));
            }

            // If primary tiling didn't produce copies, just tile along perpendicular
            if (row.Count <= pattern.Parts.Count)
            {
                row.AddRange(TilePattern(pattern, perpAxis));

                if (pattern.Parts.Count > 1 && HasOverlappingParts(row, out var a2, out var b2))
                {
                    LogOverlap("Step1-PerpOnly", perpAxis, pattern, row, a2, b2);
                    row = new List<Part>(pattern.Parts);
                    row.AddRange(TilePatternBbox(pattern, perpAxis));
                }

                return row;
            }

            // Step 2: Build row pattern and tile along perpendicular axis
            var rowPattern = new Pattern();
            rowPattern.Parts.AddRange(row);
            rowPattern.UpdateBounds();

            var gridResult = new List<Part>(rowPattern.Parts);
            gridResult.AddRange(TilePattern(rowPattern, perpAxis));

            if (HasOverlappingParts(gridResult, out var a3, out var b3))
            {
                LogOverlap("Step2-Perp", perpAxis, rowPattern, gridResult, a3, b3);
                gridResult = new List<Part>(rowPattern.Parts);
                gridResult.AddRange(TilePatternBbox(rowPattern, perpAxis));
            }

            return gridResult;
        }

        private void LogOverlap(string step, NestDirection tilingDir,
            Pattern pattern, List<Part> parts, int idxA, int idxB)
        {
            var pa = parts[idxA];
            var pb = parts[idxB];
            var ba = pa.BoundingBox;
            var bb = pb.BoundingBox;

            Debug.WriteLine($"[FillLinear] OVERLAP FALLBACK ({Label ?? "unknown"})");
            Debug.WriteLine($"  Step: {step}, TilingDir: {tilingDir}");
            Debug.WriteLine($"  WorkArea: ({WorkArea.X:F4},{WorkArea.Y:F4}) {WorkArea.Width:F4}x{WorkArea.Length:F4}, Spacing: {PartSpacing}");
            Debug.WriteLine($"  Pattern: {pattern.Parts.Count} parts, bbox {pattern.BoundingBox.Width:F4}x{pattern.BoundingBox.Length:F4}");
            Debug.WriteLine($"  Total parts after tiling: {parts.Count}");
            Debug.WriteLine($"  Overlapping pair [{idxA}] vs [{idxB}]:");
            Debug.WriteLine($"    [{idxA}]: drawing={pa.BaseDrawing?.Name ?? "?"} rot={Angle.ToDegrees(pa.Rotation):F2}° " +
                $"loc=({pa.Location.X:F4},{pa.Location.Y:F4}) bbox=({ba.Left:F4},{ba.Bottom:F4})-({ba.Right:F4},{ba.Top:F4})");
            Debug.WriteLine($"    [{idxB}]: drawing={pb.BaseDrawing?.Name ?? "?"} rot={Angle.ToDegrees(pb.Rotation):F2}° " +
                $"loc=({pb.Location.X:F4},{pb.Location.Y:F4}) bbox=({bb.Left:F4},{bb.Bottom:F4})-({bb.Right:F4},{bb.Top:F4})");

            // Log all pattern seed parts for reproduction
            Debug.WriteLine($"  Pattern seed parts:");
            for (var i = 0; i < pattern.Parts.Count; i++)
            {
                var p = pattern.Parts[i];
                Debug.WriteLine($"    [{i}]: drawing={p.BaseDrawing?.Name ?? "?"} rot={Angle.ToDegrees(p.Rotation):F2}° " +
                    $"loc=({p.Location.X:F4},{p.Location.Y:F4}) bbox={p.BoundingBox.Width:F4}x{p.BoundingBox.Length:F4}");
            }
        }

        /// <summary>
        /// Fills a single row of identical parts along one axis using geometry-aware spacing.
        /// </summary>
        public Pattern FillRow(Drawing drawing, double rotationAngle, NestDirection direction)
        {
            var seed = MakeSeedPattern(drawing, rotationAngle);

            if (seed.Parts.Count == 0)
                return seed;

            var template = seed.Parts[0];

            var copyDistance = FindCopyDistance(template, direction);

            if (copyDistance <= 0)
                return seed;

            var dim = GetDimension(template.BoundingBox, direction);
            var start = GetStart(template.BoundingBox, direction);
            var limit = GetLimit(direction);

            var count = 1;

            while (true)
            {
                var nextPos = start + copyDistance * count;

                if (nextPos + dim > limit + Tolerance.Epsilon)
                    break;

                var clone = template.CloneAtOffset(MakeOffset(direction, copyDistance * count));
                seed.Parts.Add(clone);
                count++;
            }

            seed.UpdateBounds();
            return seed;
        }

        /// <summary>
        /// Fills the work area by tiling a pre-built pattern along both axes.
        /// </summary>
        public List<Part> Fill(Pattern pattern, NestDirection primaryAxis)
        {
            if (pattern.Parts.Count == 0)
                return new List<Part>();

            var offset = WorkArea.Location - pattern.BoundingBox.Location;
            var basePattern = pattern.Clone(offset);

            if (basePattern.BoundingBox.Width > WorkArea.Width + Tolerance.Epsilon ||
                basePattern.BoundingBox.Length > WorkArea.Length + Tolerance.Epsilon)
                return new List<Part>();

            return FillGrid(basePattern, primaryAxis);
        }

        /// <summary>
        /// Fills the work area by creating a seed part, then recursively tiling
        /// along the primary axis and then the perpendicular axis.
        /// </summary>
        public List<Part> Fill(Drawing drawing, double rotationAngle, NestDirection primaryAxis)
        {
            var seed = MakeSeedPattern(drawing, rotationAngle);

            if (seed.Parts.Count == 0)
                return new List<Part>();

            return FillGrid(seed, primaryAxis);
        }
    }
}
