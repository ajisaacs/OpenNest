using System.Collections.Generic;
using System.Diagnostics;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Engine.Fill
{
    public class FillLinear
    {
        // Owned by one public call: FillHelpers can use this filler concurrently.
        // Cached local entities never escape; only translated clones reach spatial queries.
        private sealed class OffsetPerimeterCache
        {
            private readonly Dictionary<CNC.Program, List<Entity>> perimeters =
                new Dictionary<CNC.Program, List<Entity>>(ReferenceEqualityComparer.Instance);
            private readonly double spacing;

            public OffsetPerimeterCache(double spacing) => this.spacing = spacing;

            public List<Entity> AtLocation(CNC.Program program, Vector location)
            {
                if (!perimeters.TryGetValue(program, out var local))
                {
                    local = PartGeometry.GetOffsetPerimeterEntities(program, spacing);
                    perimeters.Add(program, local);
                }

                var result = new List<Entity>(local.Count);
                foreach (var entity in local)
                {
                    var clone = entity.Clone();
                    clone.Offset(location);
                    result.Add(clone);
                }
                return result;
            }
        }

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

        /// <summary>
        /// When positive, grid fills stop adding perpendicular rows once they hold at least
        /// this many parts; a first row that already holds them is returned on its own. The
        /// first row is always complete. Zero fills the whole work area.
        /// </summary>
        public int MaxParts { get; init; }

        /// <summary>
        /// When true (the default), a multi-part tiling may move off the work area's leading
        /// edge to add parts of the copy before it (see <see cref="AddLeadingParts"/>). Only
        /// characterization against fillers frozen before that change turns it off.
        /// </summary>
        internal bool LeadingParts { get; init; } = true;

        private static Vector MakeOffset(NestDirection direction, double distance)
        {
            return direction == NestDirection.Horizontal
                ? new Vector(distance, 0)
                : new Vector(0, distance);
        }

        private static PushDirection GetPushDirection(NestDirection direction)
        {
            return direction == NestDirection.Horizontal ? PushDirection.Left : PushDirection.Down;
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

        /// <summary>
        /// The gap between two boxes across the given axis; negative when they overlap there.
        /// </summary>
        private static double SideGap(Box a, Box b, NestDirection direction)
        {
            return direction == NestDirection.Horizontal
                ? System.Math.Max(a.Bottom - b.Top, b.Bottom - a.Top)
                : System.Math.Max(a.Left - b.Right, b.Left - a.Right);
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
        private double FindCopyDistance(Part partA, NestDirection direction, OffsetPerimeterCache cache)
        {
            var bboxDim = GetDimension(partA.BoundingBox, direction);
            var pushDir = GetPushDirection(direction);
            var startOffset = bboxDim + PartSpacing + Tolerance.Epsilon;
            var offset = MakeOffset(direction, startOffset);

            var stationaryEntities = cache.AtLocation(partA.Program, partA.Location);
            var movingEntities = cache.AtLocation(partA.Program, partA.Location + offset);

            var slideDistance = SpatialQuery.DirectionalDistance(
                movingEntities,
                stationaryEntities,
                pushDir
            );

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
        /// <param name="useful">Farthest distance the caller can use. The search stops with
        /// positive infinity as soon as one part pair proves the copy must move farther; the
        /// exact distance is returned whenever it is within this bound.</param>
        private double FindPatternCopyDistance(Pattern patternA, NestDirection direction, OffsetPerimeterCache cache,
            double useful = double.PositiveInfinity)
        {
            if (patternA.Parts.Count == 1)
                return FindCopyDistance(patternA.Parts[0], direction, cache);

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
            // Each list slides against several others; extract its vertices once.
            var stationaryVertices = new Vector[parts.Count][];
            var movingVertices = new Vector[parts.Count][];

            for (var i = 0; i < parts.Count; i++)
            {
                stationaryBoxes[i] = parts[i].BoundingBox;
                movingBoxes[i] = stationaryBoxes[i].Translate(offset);
            }

            // The copy distance is the maximum over every pair. Pairs whose bboxes overlap
            // across the push are kept apart from those only beside the copy's path: see below.
            var maxCopyDistance = 0.0;
            var maxBesideDistance = 0.0;

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

                    // Skip a part the copy cannot come within the spacing of: one whose bbox
                    // lies beside the copy's path by the spacing or more. One nearer than
                    // that is measured even though the bboxes never overlap, or the copy
                    // would stop clear of the parts in its path and pass this one too close.
                    var sideGap = SideGap(movingBox, stationaryBox, direction);
                    var beside = sideGap > Tolerance.Epsilon;
                    if (beside && sideGap >= PartSpacing - Tolerance.Epsilon)
                        continue;

                    if (stationaryEntities[i] == null)
                    {
                        stationaryEntities[i] = cache.AtLocation(parts[i].Program, parts[i].Location);
                        stationaryVertices[i] = SpatialQuery.ExtractEntityVertices(stationaryEntities[i]);
                    }

                    if (movingEntities[j] == null)
                    {
                        movingEntities[j] = cache.AtLocation(parts[j].Program, parts[j].Location + offset);
                        movingVertices[j] = SpatialQuery.ExtractEntityVertices(movingEntities[j]);
                    }

                    var slideDistance = SpatialQuery.DirectionalDistance(
                        movingEntities[j],
                        movingVertices[j],
                        stationaryEntities[i],
                        stationaryVertices[i],
                        dirVec,
                        new SlideContactClassifier(movingEntities[j], stationaryEntities[i])
                    );

                    if (slideDistance >= double.MaxValue || slideDistance < 0)
                        continue;

                    var copyDist = startOffset - slideDistance;

                    if (beside)
                        maxBesideDistance = System.Math.Max(maxBesideDistance, copyDist);
                    else
                        maxCopyDistance = System.Math.Max(maxCopyDistance, copyDist);

                    // Either maximum can only grow, and so can the distance they give.
                    if (maxCopyDistance > useful || maxBesideDistance > useful + Tolerance.Epsilon)
                        return double.PositiveInfinity;
                }
            }

            // A part beside the path often meets the copy corner to corner at the very
            // distance the parts in the path set, and that contact can measure up to
            // Epsilon longer. Only one that needs more than that moves the copy.
            if (maxBesideDistance > maxCopyDistance + Tolerance.Epsilon)
                maxCopyDistance = maxBesideDistance;

            return maxCopyDistance > useful ? double.PositiveInfinity : maxCopyDistance;
        }

        /// <summary>
        /// Geometry-aware distance between a pattern and its next copy along an axis at
        /// this filler's part spacing. A copy is a translation, so the distance is the
        /// same in the opposite direction.
        /// </summary>
        public double CopyDistance(Pattern pattern, NestDirection direction) =>
            FindPatternCopyDistance(pattern, direction, new OffsetPerimeterCache(HalfSpacing));

        /// <summary>
        /// Tiles a pattern along the given axis, returning the whole tiling: the pattern's own
        /// parts first, then the copies. For multi-part patterns, also adds the parts of the
        /// copies just before and after the whole ones that fit within the work area (see
        /// <see cref="AddLeadingParts"/>). The pattern's own parts are returned as given unless
        /// leading parts moved the tiling.
        /// </summary>
        private List<Part> Tile(
            Pattern basePattern,
            NestDirection direction,
            OffsetPerimeterCache cache,
            int maxParts = 0
        )
        {
            var tiling = new List<Part>(basePattern.Parts);
            tiling.AddRange(TilePattern(basePattern, direction, cache, maxParts, out var copyDistance));
            if (LeadingParts && basePattern.Parts.Count > 1 && !(maxParts > 0 && tiling.Count >= maxParts)
                && copyDistance > Tolerance.Epsilon && !double.IsPositiveInfinity(copyDistance))
                tiling = AddLeadingParts(basePattern, tiling, direction, copyDistance);
            return tiling;
        }

        /// <summary>
        /// Adds the parts of the copies before the pattern that fit once the whole tiling moves
        /// forward into the room left past its far end. A tiling always starts with the
        /// pattern's leading edge on the work area's, so the copy before it never fits, though
        /// some of its parts may: when an interlocked pair of reversed parts can only be tiled
        /// with one row first, its partner row below it fits with that row raised slightly. The
        /// tiling moves the least distance that adds the most parts; with none added it stays.
        /// </summary>
        private List<Part> AddLeadingParts(
            Pattern basePattern,
            List<Part> tiling,
            NestDirection direction,
            double copyDistance
        )
        {
            var start = GetStart(WorkArea, direction);
            var end = double.MinValue;
            foreach (var part in tiling)
                end = System.Math.Max(end, GetStart(part.BoundingBox, direction)
                    + GetDimension(part.BoundingBox, direction));
            var room = GetLimit(direction) - end;
            if (room < -Tolerance.Epsilon)
                return tiling;

            var leading = new List<(Part Part, double Back, double Shift)>();
            foreach (var part in basePattern.Parts)
            {
                var ahead = GetStart(part.BoundingBox, direction) - start;
                for (var copies = 1; ; copies++)
                {
                    var back = copies * copyDistance;
                    var shift = back - ahead;
                    if (shift > room + Tolerance.Epsilon)
                        break;
                    leading.Add((part, back, shift));
                }
            }

            if (leading.Count == 0)
                return tiling;

            var move = 0.0;
            foreach (var candidate in leading)
                move = System.Math.Max(move, candidate.Shift);
            move = System.Math.Min(move, System.Math.Max(room, 0));

            var moved = new List<Part>(tiling.Count + leading.Count);
            var offset = MakeOffset(direction, move);
            foreach (var part in tiling)
                moved.Add(move > 0 ? part.CloneAtOffset(offset) : part);
            foreach (var (part, back, _) in leading)
                moved.Add(part.CloneAtOffset(MakeOffset(direction, move - back)));
            return moved;
        }

        /// <summary>
        /// Tiles a pattern along the given axis, returning the cloned parts
        /// (does not include the original pattern's parts). For multi-part
        /// patterns, also adds individual parts from the next incomplete copy
        /// that still fit within the work area.
        /// </summary>
        private List<Part> TilePattern(
            Pattern basePattern,
            NestDirection direction,
            OffsetPerimeterCache cache,
            int maxParts,
            out double copyDistance
        )
        {
            copyDistance = FindPatternCopyDistance(basePattern, direction, cache,
                UsefulCopyDistance(basePattern, direction, maxParts));

            if (copyDistance <= 0 || double.IsPositiveInfinity(copyDistance))
                return new List<Part>();

            var dim = GetDimension(basePattern.BoundingBox, direction);
            var start = GetStart(basePattern.BoundingBox, direction);
            var limit = GetLimit(direction);

            var estimatedCopies = (int)((limit - start - dim) / copyDistance);
            var result = new List<Part>(estimatedCopies * basePattern.Parts.Count);

            var count = 1;

            // maxParts counts the base pattern too: stop once the tiling holds that many.
            bool Full() => maxParts > 0 && basePattern.Parts.Count + result.Count >= maxParts;

            while (!Full())
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
            if (basePattern.Parts.Count > 1 && !Full())
            {
                var offset = MakeOffset(direction, copyDistance * count);

                foreach (var basePart in basePattern.Parts)
                {
                    var part = basePart.CloneAtOffset(offset);

                    if (
                        part.BoundingBox.Right <= WorkArea.Right + Tolerance.Epsilon
                        && part.BoundingBox.Top <= WorkArea.Top + Tolerance.Epsilon
                        && part.BoundingBox.Left >= WorkArea.Left - Tolerance.Epsilon
                        && part.BoundingBox.Bottom >= WorkArea.Bottom - Tolerance.Epsilon
                    )
                    {
                        result.Add(part);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Farthest copy distance at which <see cref="TilePattern"/> can still add a part: the
        /// first whole copy must end within the limit, or, for a multi-part pattern, one part of
        /// it within the work area. Padded by <see cref="Tolerance.Epsilon"/> beyond the
        /// placement checks' own tolerance, so rounding in the clones' bounds never matters.
        /// A row tiled across a strip it already fills (Stripe's estimate rows) needs no exact
        /// copy distance: one slide past this bound settles that nothing fits.
        /// </summary>
        private double UsefulCopyDistance(Pattern basePattern, NestDirection direction, int maxParts)
        {
            var box = basePattern.BoundingBox;
            var useful = GetLimit(direction) - GetStart(box, direction) - GetDimension(box, direction);
            var partial = basePattern.Parts.Count > 1 && !(maxParts > 0 && basePattern.Parts.Count >= maxParts);
            if (partial)
            {
                // A part of the copy before the pattern fits once the tiling moves forward
                // into the room past the pattern, which only shrinks as copies are added.
                var room = GetLimit(direction) - GetStart(box, direction) - GetDimension(box, direction);
                foreach (var part in basePattern.Parts)
                {
                    useful = System.Math.Max(useful, direction == NestDirection.Horizontal
                        ? WorkArea.Right - part.BoundingBox.Right
                        : WorkArea.Top - part.BoundingBox.Top);
                    if (LeadingParts)
                        useful = System.Math.Max(useful,
                            room + GetStart(part.BoundingBox, direction) - GetStart(WorkArea, direction));
                }
            }
            return useful + 2 * Tolerance.Epsilon;
        }

        /// <summary>
        /// Fallback tiling using bounding-box spacing when geometry-aware tiling
        /// produces overlapping parts.
        /// </summary>
        private List<Part> TilePatternBbox(Pattern basePattern, NestDirection direction)
        {
            var copies = BboxCopies(basePattern, direction);
            var copyDistance = GetDimension(basePattern.BoundingBox, direction) + PartSpacing;
            var result = new List<Part>();

            for (var count = 1; count <= copies; count++)
            {
                var offset = MakeOffset(direction, copyDistance * count);

                foreach (var part in basePattern.Parts)
                    result.Add(part.CloneAtOffset(offset));
            }

            return result;
        }

        /// <summary>
        /// The number of whole copies <see cref="TilePatternBbox"/> adds, found without cloning.
        /// </summary>
        private int BboxCopies(Pattern basePattern, NestDirection direction)
        {
            var copyDistance = GetDimension(basePattern.BoundingBox, direction) + PartSpacing;

            if (copyDistance <= 0)
                return 0;

            var dim = GetDimension(basePattern.BoundingBox, direction);
            var start = GetStart(basePattern.BoundingBox, direction);
            var limit = GetLimit(direction);
            var count = 1;

            while (true)
            {
                var nextPos = start + copyDistance * count;

                if (nextPos + dim > limit + Tolerance.Epsilon)
                    break;

                count++;
            }

            return count - 1;
        }

        private static bool HasOverlappingParts(
            List<Part> parts,
            out int overlapA,
            out int overlapB
        )
        {
            var checker = new PartOverlapChecker();

            for (var i = 0; i < parts.Count; i++)
            {
                var b1 = parts[i].BoundingBox;

                for (var j = i + 1; j < parts.Count; j++)
                {
                    var b2 = parts[j].BoundingBox;

                    var overlapX =
                        System.Math.Min(b1.Right, b2.Right) - System.Math.Max(b1.Left, b2.Left);
                    var overlapY =
                        System.Math.Min(b1.Top, b2.Top) - System.Math.Max(b1.Bottom, b2.Bottom);

                    if (overlapX <= Tolerance.Epsilon || overlapY <= Tolerance.Epsilon)
                        continue;

                    if (checker.Overlaps(parts[i], parts[j]))
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

            if (
                template.BoundingBox.Width > WorkArea.Width + Tolerance.Epsilon
                || template.BoundingBox.Length > WorkArea.Length + Tolerance.Epsilon
            )
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
        /// <remarks>
        /// Step 1's check runs here, as it decides how the grid is built. The final check, on the
        /// whole grid, is left to the returned grid's <see cref="DeferredGrid.Resolve"/>.
        /// </remarks>
        private DeferredGrid FillGrid(Pattern pattern, NestDirection direction, OffsetPerimeterCache cache)
        {
            var perpAxis = PerpendicularAxis(direction);

            // Set only when Step 1's check executed, passed, and no fallback replaced the row.
            var rowIsVerified = false;

            // Step 1: Tile along primary axis
            var row = Tile(pattern, direction, cache);

            if (pattern.Parts.Count > 1 && HasOverlappingParts(row, out var a1, out var b1))
            {
                LogOverlap("Step1-Primary", direction, pattern, row, a1, b1);
                row = new List<Part>(pattern.Parts);
                row.AddRange(TilePatternBbox(pattern, direction));
            }
            else if (pattern.Parts.Count > 1)
            {
                rowIsVerified = true;
            }

            // If primary tiling didn't produce copies, just tile along perpendicular
            if (row.Count <= pattern.Parts.Count)
            {
                row = Tile(pattern, perpAxis, cache, MaxParts);

                return pattern.Parts.Count > 1
                    ? new DeferredGrid(this, row, "Step1-PerpOnly", perpAxis, pattern)
                    : new DeferredGrid(row);
            }

            // A capped fill whose first row already holds enough parts needs no more rows.
            // Step 2's check could only switch perpendicular tiling to the bbox fallback; it
            // never repairs the row itself, so returning the row loses no protection.
            if (MaxParts > 0 && row.Count >= MaxParts)
                return new DeferredGrid(row);

            // Step 2: Build row pattern and tile along perpendicular axis
            var rowPattern = new Pattern();
            rowPattern.Parts.AddRange(row);
            rowPattern.UpdateBounds();

            var gridResult = Tile(rowPattern, perpAxis, cache, MaxParts);

            // Only the unchanged row is covered by Step 1's clean verdict: skip Step 2
            // only when the perpendicular tiling appended zero parts, so gridResult
            // still contains the same Part objects, in the same order and poses.
            return !rowIsVerified || gridResult.Count != row.Count
                ? new DeferredGrid(this, gridResult, "Step2-Perp", perpAxis, rowPattern)
                : new DeferredGrid(gridResult);
        }

        /// <summary>
        /// A grid tiled by <see cref="FillGrid"/> whose final overlap check may not have run yet.
        /// Until <see cref="Resolve"/> runs it, the grid holds either its tiling, when no two
        /// parts overlap, or the fallback that tiles the same pattern by bounding boxes
        /// (<see cref="TilePatternBbox"/>); both part counts are known without the check.
        /// <see cref="Resolve"/> returns exactly what the eager fill returned, so a caller may
        /// skip the check only on a grid whose outcome it never uses. Not thread-safe.
        /// </summary>
        internal sealed class DeferredGrid
        {
            private readonly FillLinear owner;
            private readonly string step;
            private readonly NestDirection axis;
            private Pattern basePattern;
            private List<Part> parts;
            private int? fallbackCount;

            /// <summary>A grid with no check left to run.</summary>
            internal DeferredGrid(List<Part> parts) => this.parts = parts;

            /// <summary>
            /// A grid whose tiling <paramref name="parts"/> of <paramref name="basePattern"/> along
            /// <paramref name="axis"/> still has to pass the overlap check.
            /// </summary>
            internal DeferredGrid(
                FillLinear owner,
                List<Part> parts,
                string step,
                NestDirection axis,
                Pattern basePattern
            )
            {
                this.owner = owner;
                this.parts = parts;
                this.step = step;
                this.axis = axis;
                this.basePattern = basePattern;
            }

            /// <summary>True until <see cref="Resolve"/> has run the overlap check.</summary>
            public bool IsPending => basePattern != null;

            /// <summary>
            /// The part count of the grid as it stands: its tiling while the check is pending, the
            /// checked parts once resolved.
            /// </summary>
            public int Count => parts.Count;

            /// <summary>
            /// While <see cref="IsPending"/>, the part count of the bounding-box fallback, should
            /// the check fail.
            /// </summary>
            public int FallbackCount =>
                fallbackCount ??= (1 + owner.BboxCopies(basePattern, axis)) * basePattern.Parts.Count;

            /// <summary>Runs the overlap check if it is pending and returns the grid's parts.</summary>
            public List<Part> Resolve()
            {
                if (basePattern == null)
                    return parts;

                if (HasOverlappingParts(parts, out var a, out var b))
                {
                    owner.LogOverlap(step, axis, basePattern, parts, a, b);
                    var fallback = new List<Part>(basePattern.Parts);
                    fallback.AddRange(owner.TilePatternBbox(basePattern, axis));
                    parts = fallback;
                }

                basePattern = null;
                return parts;
            }
        }

        private void LogOverlap(
            string step,
            NestDirection tilingDir,
            Pattern pattern,
            List<Part> parts,
            int idxA,
            int idxB
        )
        {
            var pa = parts[idxA];
            var pb = parts[idxB];
            var ba = pa.BoundingBox;
            var bb = pb.BoundingBox;

            Debug.WriteLine($"[FillLinear] OVERLAP FALLBACK ({Label ?? "unknown"})");
            Debug.WriteLine($"  Step: {step}, TilingDir: {tilingDir}");
            Debug.WriteLine(
                $"  WorkArea: ({WorkArea.X:F4},{WorkArea.Y:F4}) {WorkArea.Width:F4}x{WorkArea.Length:F4}, Spacing: {PartSpacing}"
            );
            Debug.WriteLine(
                $"  Pattern: {pattern.Parts.Count} parts, bbox {pattern.BoundingBox.Width:F4}x{pattern.BoundingBox.Length:F4}"
            );
            Debug.WriteLine($"  Total parts after tiling: {parts.Count}");
            Debug.WriteLine($"  Overlapping pair [{idxA}] vs [{idxB}]:");
            Debug.WriteLine(
                $"    [{idxA}]: drawing={pa.BaseDrawing?.Name ?? "?"} rot={Angle.ToDegrees(pa.Rotation):F2}° "
                    + $"loc=({pa.Location.X:F4},{pa.Location.Y:F4}) bbox=({ba.Left:F4},{ba.Bottom:F4})-({ba.Right:F4},{ba.Top:F4})"
            );
            Debug.WriteLine(
                $"    [{idxB}]: drawing={pb.BaseDrawing?.Name ?? "?"} rot={Angle.ToDegrees(pb.Rotation):F2}° "
                    + $"loc=({pb.Location.X:F4},{pb.Location.Y:F4}) bbox=({bb.Left:F4},{bb.Bottom:F4})-({bb.Right:F4},{bb.Top:F4})"
            );

            // Log all pattern seed parts for reproduction
            Debug.WriteLine($"  Pattern seed parts:");
            for (var i = 0; i < pattern.Parts.Count; i++)
            {
                var p = pattern.Parts[i];
                Debug.WriteLine(
                    $"    [{i}]: drawing={p.BaseDrawing?.Name ?? "?"} rot={Angle.ToDegrees(p.Rotation):F2}° "
                        + $"loc=({p.Location.X:F4},{p.Location.Y:F4}) bbox={p.BoundingBox.Width:F4}x{p.BoundingBox.Length:F4}"
                );
            }
        }

        /// <summary>
        /// Fills a single row of identical parts along one axis using geometry-aware spacing.
        /// </summary>
        public Pattern FillRow(Drawing drawing, double rotationAngle, NestDirection direction)
        {
            var cache = new OffsetPerimeterCache(HalfSpacing);
            var seed = MakeSeedPattern(drawing, rotationAngle);

            if (seed.Parts.Count == 0)
                return seed;

            var template = seed.Parts[0];

            var copyDistance = FindCopyDistance(template, direction, cache);

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
        public List<Part> Fill(Pattern pattern, NestDirection primaryAxis) =>
            FillDeferred(pattern, primaryAxis).Resolve();

        /// <summary>
        /// <see cref="Fill(Pattern, NestDirection)"/> with the grid's final overlap check left to
        /// the caller: <see cref="DeferredGrid.Resolve"/> returns exactly what Fill returns.
        /// </summary>
        internal DeferredGrid FillDeferred(Pattern pattern, NestDirection primaryAxis)
        {
            var cache = new OffsetPerimeterCache(HalfSpacing);
            if (pattern.Parts.Count == 0)
                return new DeferredGrid(new List<Part>());

            var offset = WorkArea.Location - pattern.BoundingBox.Location;
            var basePattern = pattern.Clone(offset);

            if (
                basePattern.BoundingBox.Width > WorkArea.Width + Tolerance.Epsilon
                || basePattern.BoundingBox.Length > WorkArea.Length + Tolerance.Epsilon
            )
                return new DeferredGrid(new List<Part>());

            return FillGrid(basePattern, primaryAxis, cache);
        }

        /// <summary>
        /// Fills the work area by creating a seed part, then recursively tiling
        /// along the primary axis and then the perpendicular axis.
        /// </summary>
        public List<Part> Fill(Drawing drawing, double rotationAngle, NestDirection primaryAxis)
        {
            var cache = new OffsetPerimeterCache(HalfSpacing);
            var seed = MakeSeedPattern(drawing, rotationAngle);

            if (seed.Parts.Count == 0)
                return new List<Part>();

            return FillGrid(seed, primaryAxis, cache).Resolve();
        }
    }
}
