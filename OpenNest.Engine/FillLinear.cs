using System.Collections.Generic;
using System.Threading.Tasks;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public class FillLinear
    {
        public FillLinear(Box workArea, double partSpacing)
        {
            PartSpacing = partSpacing;
            WorkArea = new Box(workArea.X, workArea.Y, workArea.Width, workArea.Length);
        }

        public Box WorkArea { get; }

        public double PartSpacing { get; }
        
        public double HalfSpacing => PartSpacing / 2;

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
            return direction == NestDirection.Horizontal ? box.Width : box.Length;
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
        /// Computes the slide distance for the push algorithm, returning the
        /// geometry-aware copy distance along the given axis.
        /// </summary>
        private double ComputeCopyDistance(double bboxDim, double slideDistance)
        {
            if (slideDistance >= double.MaxValue || slideDistance < 0)
                return bboxDim + PartSpacing;

            // The geometry-aware slide can produce a copy distance smaller than
            // the part itself when inflated corner/arc vertices interact spuriously.
            // Clamp to bboxDim + PartSpacing to prevent bounding box overlap.
            return System.Math.Max(bboxDim - slideDistance, bboxDim + PartSpacing);
        }

        /// <summary>
        /// Finds the geometry-aware copy distance between two identical parts along an axis.
        /// Both parts are inflated by half-spacing for symmetric spacing.
        /// </summary>
        private double FindCopyDistance(Part partA, NestDirection direction, PartBoundary boundary)
        {
            var bboxDim = GetDimension(partA.BoundingBox, direction);
            var pushDir = GetPushDirection(direction);

            var locationBOffset = MakeOffset(direction, bboxDim);

            // Use the most efficient array-based overload to avoid all allocations.
            var slideDistance = Helper.DirectionalDistance(
                boundary.GetEdges(pushDir), partA.Location + locationBOffset,
                boundary.GetEdges(Helper.OppositeDirection(pushDir)), partA.Location,
                pushDir);

            return ComputeCopyDistance(bboxDim, slideDistance);
        }

        /// <summary>
        /// Finds the geometry-aware copy distance between two identical patterns along an axis.
        /// Checks every pair of parts across adjacent patterns so that multi-part
        /// patterns (e.g. interlocking pairs) maintain spacing between ALL parts.
        /// Both sides are inflated by half-spacing for symmetric spacing.
        /// </summary>
        private double FindPatternCopyDistance(Pattern patternA, NestDirection direction, PartBoundary[] boundaries)
        {
            if (patternA.Parts.Count <= 1)
                return FindSinglePartPatternCopyDistance(patternA, direction, boundaries[0]);

            var bboxDim = GetDimension(patternA.BoundingBox, direction);
            var pushDir = GetPushDirection(direction);
            var opposite = Helper.OppositeDirection(pushDir);

            // Compute a starting offset large enough that every part-pair in
            // patternB has its offset geometry beyond patternA's offset geometry.
            var maxUpper = double.MinValue;
            var minLower = double.MaxValue;

            for (var i = 0; i < patternA.Parts.Count; i++)
            {
                var bb = patternA.Parts[i].BoundingBox;
                var upper = direction == NestDirection.Horizontal ? bb.Right : bb.Top;
                var lower = direction == NestDirection.Horizontal ? bb.Left : bb.Bottom;

                if (upper > maxUpper) maxUpper = upper;
                if (lower < minLower) minLower = lower;
            }

            var startOffset = System.Math.Max(bboxDim,
                maxUpper - minLower + PartSpacing + Tolerance.Epsilon);

            var offset = MakeOffset(direction, startOffset);

            // Pre-cache edge arrays.
            var movingEdges = new (Vector start, Vector end)[patternA.Parts.Count][];
            var stationaryEdges = new (Vector start, Vector end)[patternA.Parts.Count][];

            for (var i = 0; i < patternA.Parts.Count; i++)
            {
                movingEdges[i] = boundaries[i].GetEdges(pushDir);
                stationaryEdges[i] = boundaries[i].GetEdges(opposite);
            }

            var maxCopyDistance = 0.0;

            for (var j = 0; j < patternA.Parts.Count; j++)
            {
                var locationB = patternA.Parts[j].Location + offset;

                for (var i = 0; i < patternA.Parts.Count; i++)
                {
                    var slideDistance = Helper.DirectionalDistance(
                        movingEdges[j], locationB,
                        stationaryEdges[i], patternA.Parts[i].Location,
                        pushDir);

                    if (slideDistance >= double.MaxValue || slideDistance < 0)
                        continue;

                    var copyDist = startOffset - slideDistance;

                    if (copyDist > maxCopyDistance)
                        maxCopyDistance = copyDist;
                }
            }

            if (maxCopyDistance < Tolerance.Epsilon)
                return bboxDim + PartSpacing;

            return maxCopyDistance;
        }

        /// <summary>
        /// Fast path for single-part patterns — no cross-part conflicts possible.
        /// </summary>
        private double FindSinglePartPatternCopyDistance(Pattern patternA, NestDirection direction, PartBoundary boundary)
        {
            var template = patternA.Parts[0];
            return FindCopyDistance(template, direction, boundary);
        }

        /// <summary>
        /// Gets offset boundary lines for all parts in a pattern using a shared boundary.
        /// </summary>
        private static List<Line> GetPatternLines(Pattern pattern, PartBoundary boundary, PushDirection direction)
        {
            var lines = new List<Line>();

            foreach (var part in pattern.Parts)
                lines.AddRange(boundary.GetLines(part.Location, direction));

            return lines;
        }

        /// <summary>
        /// Gets boundary lines for all parts in a pattern, with an additional
        /// location offset applied. Avoids cloning the pattern.
        /// </summary>
        private static List<Line> GetOffsetPatternLines(Pattern pattern, Vector offset, PartBoundary boundary, PushDirection direction)
        {
            var lines = new List<Line>();

            foreach (var part in pattern.Parts)
                lines.AddRange(boundary.GetLines(part.Location + offset, direction));

            return lines;
        }

        /// <summary>
        /// Creates boundaries for all parts in a pattern. Parts that share the same
        /// program geometry (same drawing and rotation) reuse the same boundary instance.
        /// </summary>
        private PartBoundary[] CreateBoundaries(Pattern pattern)
        {
            var boundaries = new PartBoundary[pattern.Parts.Count];
            var cache = new List<(Drawing drawing, double rotation, PartBoundary boundary)>();

            for (var i = 0; i < pattern.Parts.Count; i++)
            {
                var part = pattern.Parts[i];
                PartBoundary found = null;

                foreach (var entry in cache)
                {
                    if (entry.drawing == part.BaseDrawing && entry.rotation.IsEqualTo(part.Rotation))
                    {
                        found = entry.boundary;
                        break;
                    }
                }

                if (found == null)
                {
                    found = new PartBoundary(part, HalfSpacing);
                    cache.Add((part.BaseDrawing, part.Rotation, found));
                }

                boundaries[i] = found;
            }

            return boundaries;
        }

        /// <summary>
        /// Tiles a pattern along the given axis, returning the cloned parts
        /// (does not include the original pattern's parts). For multi-part
        /// patterns, also adds individual parts from the next incomplete copy
        /// that still fit within the work area.
        /// </summary>
        private List<Part> TilePattern(Pattern basePattern, NestDirection direction, PartBoundary[] boundaries)
        {
            var copyDistance = FindPatternCopyDistance(basePattern, direction, boundaries);

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
            var boundaries = CreateBoundaries(pattern);

            // Step 1: Tile along primary axis
            var row = new List<Part>(pattern.Parts);
            row.AddRange(TilePattern(pattern, direction, boundaries));

            // If primary tiling didn't produce copies, just tile along perpendicular
            if (row.Count <= pattern.Parts.Count)
            {
                row.AddRange(TilePattern(pattern, perpAxis, boundaries));
                return row;
            }

            // Step 2: Build row pattern and tile along perpendicular axis
            var rowPattern = new Pattern();
            rowPattern.Parts.AddRange(row);
            rowPattern.UpdateBounds();

            var rowBoundaries = CreateBoundaries(rowPattern);
            var gridResult = new List<Part>(rowPattern.Parts);
            gridResult.AddRange(TilePattern(rowPattern, perpAxis, rowBoundaries));

            // Step 3: Fill remaining strip
            var remaining = FillRemainingStrip(gridResult, pattern, perpAxis, direction);
            if (remaining.Count > 0)
                gridResult.AddRange(remaining);

            // Step 4: Try fewer rows optimization
            var fewerResult = TryFewerRows(gridResult, rowPattern, pattern, perpAxis, direction);
            if (fewerResult != null && fewerResult.Count > gridResult.Count)
                return fewerResult;

            return gridResult;
        }

        /// <summary>
        /// Tries removing the last row/column from the grid and re-filling the
        /// larger remainder strip. Returns null if this doesn't improve the total.
        /// </summary>
        private List<Part> TryFewerRows(
            List<Part> fullResult, Pattern rowPattern, Pattern seedPattern,
            NestDirection tiledAxis, NestDirection primaryAxis)
        {
            var rowPartCount = rowPattern.Parts.Count;

            if (fullResult.Count < rowPartCount * 2)
                return null;

            var fewerParts = new List<Part>(fullResult.Count - rowPartCount);

            for (var i = 0; i < fullResult.Count - rowPartCount; i++)
                fewerParts.Add(fullResult[i]);

            var remaining = FillRemainingStrip(fewerParts, seedPattern, tiledAxis, primaryAxis);

            if (remaining.Count <= rowPartCount)
                return null;

            fewerParts.AddRange(remaining);
            return fewerParts;
        }

        /// <summary>
        /// After tiling full rows/columns, fills the remaining strip with individual
        /// parts. The strip is the leftover space along the tiled axis between the
        /// last full row/column and the work area boundary. Each unique drawing and
        /// rotation from the seed pattern is tried in both directions.
        /// </summary>
        private List<Part> FillRemainingStrip(
            List<Part> placedParts, Pattern seedPattern,
            NestDirection tiledAxis, NestDirection primaryAxis)
        {
            var placedEdge = FindPlacedEdge(placedParts, tiledAxis);
            var remainingStrip = BuildRemainingStrip(placedEdge, tiledAxis);

            if (remainingStrip == null)
                return new List<Part>();

            var rotations = BuildRotationSet(seedPattern);
            return FindBestFill(rotations, remainingStrip);
        }

        private static double FindPlacedEdge(List<Part> placedParts, NestDirection tiledAxis)
        {
            var placedEdge = double.MinValue;

            foreach (var part in placedParts)
            {
                var edge = tiledAxis == NestDirection.Vertical
                    ? part.BoundingBox.Top
                    : part.BoundingBox.Right;

                if (edge > placedEdge)
                    placedEdge = edge;
            }

            return placedEdge;
        }

        private Box BuildRemainingStrip(double placedEdge, NestDirection tiledAxis)
        {
            if (tiledAxis == NestDirection.Vertical)
            {
                var bottom = placedEdge + PartSpacing;
                var height = WorkArea.Top - bottom;

                if (height <= Tolerance.Epsilon)
                    return null;

                return new Box(WorkArea.X, bottom, WorkArea.Width, height);
            }
            else
            {
                var left = placedEdge + PartSpacing;
                var width = WorkArea.Right - left;

                if (width <= Tolerance.Epsilon)
                    return null;

                return new Box(left, WorkArea.Y, width, WorkArea.Length);
            }
        }

        /// <summary>
        /// Builds a set of (drawing, rotation) candidates: cardinal orientations
        /// (0° and 90°) for each unique drawing, plus any seed pattern rotations
        /// not already covered.
        /// </summary>
        private static List<(Drawing drawing, double rotation)> BuildRotationSet(Pattern seedPattern)
        {
            var rotations = new List<(Drawing drawing, double rotation)>();
            var drawings = new List<Drawing>();

            foreach (var seedPart in seedPattern.Parts)
            {
                var found = false;

                foreach (var d in drawings)
                {
                    if (d == seedPart.BaseDrawing)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    drawings.Add(seedPart.BaseDrawing);
            }

            foreach (var drawing in drawings)
            {
                rotations.Add((drawing, 0));
                rotations.Add((drawing, Angle.HalfPI));
            }

            foreach (var seedPart in seedPattern.Parts)
            {
                var skip = false;

                foreach (var (d, r) in rotations)
                {
                    if (d == seedPart.BaseDrawing && r.IsEqualTo(seedPart.Rotation))
                    {
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                    rotations.Add((seedPart.BaseDrawing, seedPart.Rotation));
            }

            return rotations;
        }

        /// <summary>
        /// Tries all rotation candidates in both directions in parallel, returns the
        /// fill with the most parts.
        /// </summary>
        private List<Part> FindBestFill(List<(Drawing drawing, double rotation)> rotations, Box strip)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<List<Part>>();

            Parallel.ForEach(rotations, entry =>
            {
                var filler = new FillLinear(strip, PartSpacing);
                var h = filler.Fill(entry.drawing, entry.rotation, NestDirection.Horizontal);
                var v = filler.Fill(entry.drawing, entry.rotation, NestDirection.Vertical);

                if (h != null && h.Count > 0)
                    bag.Add(h);

                if (v != null && v.Count > 0)
                    bag.Add(v);
            });

            List<Part> best = null;

            foreach (var candidate in bag)
            {
                if (best == null || candidate.Count > best.Count)
                    best = candidate;
            }

            return best ?? new List<Part>();
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
            var boundary = new PartBoundary(template, HalfSpacing);

            var copyDistance = FindCopyDistance(template, direction, boundary);

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
