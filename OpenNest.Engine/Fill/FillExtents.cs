using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace OpenNest.Engine.Fill
{
    public class FillExtents
    {
        private const int MaxIterations = 10;

        private readonly Box workArea;
        private readonly double partSpacing;
        private readonly double halfSpacing;

        public FillExtents(Box workArea, double partSpacing)
        {
            this.workArea = workArea;
            this.partSpacing = partSpacing;
            halfSpacing = partSpacing / 2;
        }

        public List<Part> Fill(Drawing drawing, double rotationAngle = 0,
            int plateNumber = 0,
            CancellationToken token = default,
            IProgress<NestProgress> progress = null)
        {
            var pair = BuildPair(drawing, rotationAngle);
            if (pair == null)
                return new List<Part>();

            var column = BuildColumn(pair.Value.part1, pair.Value.part2, pair.Value.pairBbox);
            if (column.Count == 0)
                return new List<Part>();

            NestEngineBase.ReportProgress(progress, NestPhase.Extents, plateNumber,
                column, workArea, $"Extents: initial column {column.Count} parts");

            var adjusted = AdjustColumn(pair.Value, column, token);

            NestEngineBase.ReportProgress(progress, NestPhase.Extents, plateNumber,
                adjusted, workArea, $"Extents: adjusted column {adjusted.Count} parts");

            var result = RepeatColumns(adjusted, token);

            NestEngineBase.ReportProgress(progress, NestPhase.Extents, plateNumber,
                result, workArea, $"Extents: {result.Count} parts total");

            return result;
        }

        // --- Step 1: Pair Construction ---

        private (Part part1, Part part2, Box pairBbox)? BuildPair(Drawing drawing, double rotationAngle)
        {
            var part1 = Part.CreateAtOrigin(drawing, rotationAngle);
            var part2 = Part.CreateAtOrigin(drawing, rotationAngle + System.Math.PI);

            // Check that each part fits in the work area individually.
            if (part1.BoundingBox.Width > workArea.Width + Tolerance.Epsilon ||
                part1.BoundingBox.Length > workArea.Length + Tolerance.Epsilon)
                return null;

            // Slide part2 toward part1 from the right using geometry-aware distance.
            var boundary1 = new PartBoundary(part1, halfSpacing);
            var boundary2 = new PartBoundary(part2, halfSpacing);

            // Position part2 to the right of part1 at bounding box width distance.
            var startOffset = part1.BoundingBox.Width + part2.BoundingBox.Width + partSpacing;
            part2.Offset(startOffset, 0);
            part2.UpdateBounds();

            // Slide part2 left toward part1.
            var movingLines = boundary2.GetLines(part2.Location, PushDirection.Left);
            var stationaryLines = boundary1.GetLines(part1.Location, PushDirection.Right);
            var dist = SpatialQuery.DirectionalDistance(movingLines, stationaryLines, PushDirection.Left);

            if (dist < double.MaxValue && dist > 0)
            {
                part2.Offset(-dist, 0);
                part2.UpdateBounds();
            }

            // Re-anchor pair to work area origin.
            var pairBbox = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
            var anchor = new Vector(workArea.X - pairBbox.Left, workArea.Y - pairBbox.Bottom);
            part1.Offset(anchor);
            part2.Offset(anchor);
            part1.UpdateBounds();
            part2.UpdateBounds();

            pairBbox = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();

            // Verify pair fits in work area.
            if (pairBbox.Width > workArea.Width + Tolerance.Epsilon ||
                pairBbox.Length > workArea.Length + Tolerance.Epsilon)
                return null;

            return (part1, part2, pairBbox);
        }

        // --- Step 2: Build Column (tile vertically) ---

        private List<Part> BuildColumn(Part part1, Part part2, Box pairBbox)
        {
            var column = new List<Part> { (Part)part1.Clone(), (Part)part2.Clone() };

            // Find geometry-aware copy distance for the pair vertically.
            var boundary1 = new PartBoundary(part1, halfSpacing);
            var boundary2 = new PartBoundary(part2, halfSpacing);

            // Compute vertical copy distance using bounding boxes as starting point,
            // then slide down to find true geometry distance.
            var pairHeight = pairBbox.Length;
            var testOffset = new Vector(0, pairHeight);

            // Create test parts for slide distance measurement.
            var testPart1 = part1.CloneAtOffset(testOffset);
            var testPart2 = part2.CloneAtOffset(testOffset);

            // Find minimum distance from test pair sliding down toward original pair.
            var copyDistance = FindVerticalCopyDistance(
                part1, part2, testPart1, testPart2,
                boundary1, boundary2, pairHeight);

            if (copyDistance <= 0)
                return column;

            var count = 1;
            while (true)
            {
                var nextBottom = pairBbox.Bottom + copyDistance * count;
                if (nextBottom + pairHeight > workArea.Top + Tolerance.Epsilon)
                    break;

                var offset = new Vector(0, copyDistance * count);
                column.Add(part1.CloneAtOffset(offset));
                column.Add(part2.CloneAtOffset(offset));
                count++;
            }

            return column;
        }

        private double FindVerticalCopyDistance(
            Part origPart1, Part origPart2,
            Part testPart1, Part testPart2,
            PartBoundary boundary1, PartBoundary boundary2,
            double pairHeight)
        {
            // Check all 4 combinations: test parts sliding down toward original parts.
            var minSlide = double.MaxValue;

            // Test1 -> Orig1
            var d = SlideDistance(boundary1, testPart1.Location, boundary1, origPart1.Location, PushDirection.Down);
            if (d < minSlide) minSlide = d;

            // Test1 -> Orig2
            d = SlideDistance(boundary1, testPart1.Location, boundary2, origPart2.Location, PushDirection.Down);
            if (d < minSlide) minSlide = d;

            // Test2 -> Orig1
            d = SlideDistance(boundary2, testPart2.Location, boundary1, origPart1.Location, PushDirection.Down);
            if (d < minSlide) minSlide = d;

            // Test2 -> Orig2
            d = SlideDistance(boundary2, testPart2.Location, boundary2, origPart2.Location, PushDirection.Down);
            if (d < minSlide) minSlide = d;

            if (minSlide >= double.MaxValue || minSlide < 0)
                return pairHeight + partSpacing;

            // Boundaries are inflated by halfSpacing, so when inflated edges touch
            // the actual parts have partSpacing gap. Match FillLinear's pattern:
            // startOffset = pairHeight (no extra spacing), copyDist = height - slide.
            var copyDist = pairHeight - minSlide;

            // Boundaries are inflated by halfSpacing, so the geometry-aware
            // distance already guarantees partSpacing gap. Only fall back to
            // bounding-box distance if the calculation produced a non-positive value.
            if (copyDist <= Tolerance.Epsilon)
                return pairHeight + partSpacing;

            return copyDist;
        }

        private static double SlideDistance(
            PartBoundary movingBoundary, Vector movingLocation,
            PartBoundary stationaryBoundary, Vector stationaryLocation,
            PushDirection direction)
        {
            var opposite = SpatialQuery.OppositeDirection(direction);
            var movingEdges = movingBoundary.GetEdges(direction);
            var stationaryEdges = stationaryBoundary.GetEdges(opposite);

            return SpatialQuery.DirectionalDistance(
                movingEdges, movingLocation,
                stationaryEdges, stationaryLocation,
                direction);
        }

        // --- Step 3: Iterative Adjustment ---

        private List<Part> AdjustColumn(
            (Part part1, Part part2, Box pairBbox) pair,
            List<Part> column,
            CancellationToken token)
        {
            var originalPairWidth = pair.pairBbox.Width;

            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                if (token.IsCancellationRequested)
                    break;

                // Measure current gap.
                var topEdge = double.MinValue;
                foreach (var p in column)
                    if (p.BoundingBox.Top > topEdge)
                        topEdge = p.BoundingBox.Top;

                var gap = workArea.Top - topEdge;

                if (gap <= Tolerance.Epsilon)
                    break;

                var pairCount = column.Count / 2;
                if (pairCount <= 0)
                    break;

                var adjustment = gap / pairCount;
                if (adjustment <= Tolerance.Epsilon)
                    break;

                // Try adjusting the pair and rebuilding the column.
                var adjusted = TryAdjustPair(pair, adjustment, originalPairWidth);
                if (adjusted == null)
                    break;

                var newColumn = BuildColumn(adjusted.Value.part1, adjusted.Value.part2, adjusted.Value.pairBbox);
                if (newColumn.Count == 0)
                    break;

                column = newColumn;
                pair = adjusted.Value;
            }

            return column;
        }

        private (Part part1, Part part2, Box pairBbox)? TryAdjustPair(
            (Part part1, Part part2, Box pairBbox) pair,
            double adjustment, double originalPairWidth)
        {
            // Try shifting part2 up first.
            var result = TryShiftDirection(pair, adjustment, originalPairWidth);

            if (result != null)
                return result;

            // Up made the pair wider — try down instead.
            return TryShiftDirection(pair, -adjustment, originalPairWidth);
        }

        private (Part part1, Part part2, Box pairBbox)? TryShiftDirection(
            (Part part1, Part part2, Box pairBbox) pair,
            double verticalShift, double originalPairWidth)
        {
            // Clone parts so we don't mutate the originals.
            var p1 = (Part)pair.part1.Clone();
            var p2 = (Part)pair.part2.Clone();

            // Separate: shift part2 right so bounding boxes don't touch.
            p2.Offset(partSpacing, 0);
            p2.UpdateBounds();

            // Apply the vertical shift.
            p2.Offset(0, verticalShift);
            p2.UpdateBounds();

            // Compact part2 left toward part1.
            var moving = new List<Part> { p2 };
            var obstacles = new List<Part> { p1 };
            Compactor.Push(moving, obstacles, workArea, partSpacing, PushDirection.Left);

            // Check if the pair got wider.
            var newBbox = ((IEnumerable<IBoundable>)new IBoundable[] { p1, p2 }).GetBoundingBox();

            if (newBbox.Width > originalPairWidth + Tolerance.Epsilon)
                return null;

            // Re-anchor to work area origin.
            var anchor = new Vector(workArea.X - newBbox.Left, workArea.Y - newBbox.Bottom);
            p1.Offset(anchor);
            p2.Offset(anchor);
            p1.UpdateBounds();
            p2.UpdateBounds();

            newBbox = ((IEnumerable<IBoundable>)new IBoundable[] { p1, p2 }).GetBoundingBox();
            return (p1, p2, newBbox);
        }

        // --- Step 4: Horizontal Repetition ---

        private List<Part> RepeatColumns(List<Part> column, CancellationToken token)
        {
            if (column.Count == 0)
                return column;

            var columnBbox = ((IEnumerable<IBoundable>)column).GetBoundingBox();
            var columnWidth = columnBbox.Width;

            // Create a test column shifted right by columnWidth + spacing.
            var testOffset = columnWidth + partSpacing;
            var testColumn = new List<Part>(column.Count);
            foreach (var part in column)
                testColumn.Add(part.CloneAtOffset(new Vector(testOffset, 0)));

            // Compact the test column left against the original column.
            var distanceMoved = Compactor.Push(testColumn, column, workArea, partSpacing, PushDirection.Left);

            // Derive the true copy distance from where the test column ended up.
            var testBbox = ((IEnumerable<IBoundable>)testColumn).GetBoundingBox();
            var copyDistance = testBbox.Left - columnBbox.Left;

            if (copyDistance <= Tolerance.Epsilon)
                copyDistance = columnWidth + partSpacing;

            Debug.WriteLine($"[FillExtents] Column copy distance: {copyDistance:F2} (bbox width: {columnWidth:F2}, spacing: {partSpacing:F2})");

            // Build all columns.
            var result = new List<Part>(column);

            // Add the test column we already computed as column 2.
            foreach (var part in testColumn)
            {
                if (IsWithinWorkArea(part))
                    result.Add(part);
            }

            // Tile additional columns at the copy distance.
            var colIndex = 2;
            while (!token.IsCancellationRequested)
            {
                var offset = new Vector(copyDistance * colIndex, 0);
                var anyFit = false;

                foreach (var part in column)
                {
                    var clone = part.CloneAtOffset(offset);
                    if (IsWithinWorkArea(clone))
                    {
                        result.Add(clone);
                        anyFit = true;
                    }
                }

                if (!anyFit)
                    break;

                colIndex++;
            }

            return result;
        }

        private bool IsWithinWorkArea(Part part)
        {
            return part.BoundingBox.Right <= workArea.Right + Tolerance.Epsilon &&
                   part.BoundingBox.Top <= workArea.Top + Tolerance.Epsilon &&
                   part.BoundingBox.Left >= workArea.Left - Tolerance.Epsilon &&
                   part.BoundingBox.Bottom >= workArea.Bottom - Tolerance.Epsilon;
        }
    }
}
