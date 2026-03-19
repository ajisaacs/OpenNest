using OpenNest.Geometry;
using OpenNest.Math;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace OpenNest.Engine.Fill
{
    public class FillExtents
    {
        private const int MaxIterations = 10;

        private readonly Box workArea;
        private readonly double partSpacing;

        public FillExtents(Box workArea, double partSpacing)
        {
            this.workArea = workArea;
            this.partSpacing = partSpacing;
        }

        public List<Part> Fill(Drawing drawing, double rotationAngle = 0,
            int plateNumber = 0,
            CancellationToken token = default,
            IProgress<NestProgress> progress = null)
        {
            var initialPair = CreatePair(drawing, rotationAngle, rotationAngle + System.Math.PI);
            if (initialPair == null)
                return new List<Part>();

            var column = BuildColumn(initialPair.Value.part1, initialPair.Value.part2, initialPair.Value.pairBbox);
            if (column.Count == 0)
                return new List<Part>();

            NestEngineBase.ReportProgress(progress, NestPhase.Extents, plateNumber,
                column, workArea, $"Extents: initial column {column.Count} parts");

            var adjusted = AdjustColumn(initialPair.Value, column, token);

            NestEngineBase.ReportProgress(progress, NestPhase.Extents, plateNumber,
                adjusted, workArea, $"Extents: adjusted column {adjusted.Count} parts");

            var result = RepeatColumns(adjusted, token);

            NestEngineBase.ReportProgress(progress, NestPhase.Extents, plateNumber,
                result, workArea, $"Extents: {result.Count} parts total");

            return result;
        }

        // --- Step 1: Pair Construction ---

        private (Part part1, Part part2, Box pairBbox)? CreatePair(
            Drawing drawing, double rotation1, double rotation2, double verticalShift2 = 0)
        {
            var p1 = Part.CreateAtOrigin(drawing, rotation1);
            var p2 = Part.CreateAtOrigin(drawing, rotation2);

            // Initial positioning: p2 to the right of p1, with optional vertical shift.
            p2.Offset(p1.BoundingBox.Width + partSpacing, verticalShift2);

            // Compact p2 left toward p1 using geometry-aware distance.
            Compactor.Push(new List<Part> { p2 }, new List<Part> { p1 }, workArea, partSpacing, PushDirection.Left);

            var pairBbox = ((IEnumerable<IBoundable>)new IBoundable[] { p1, p2 }).GetBoundingBox();

            // Re-anchor pair to work area origin (bottom-left).
            var anchor = new Vector(workArea.X - pairBbox.Left, workArea.Y - pairBbox.Bottom);
            p1.Offset(anchor);
            p2.Offset(anchor);

            pairBbox = ((IEnumerable<IBoundable>)new IBoundable[] { p1, p2 }).GetBoundingBox();

            // Verify pair fits in work area.
            if (pairBbox.Width > workArea.Width + Tolerance.Epsilon ||
                pairBbox.Length > workArea.Length + Tolerance.Epsilon)
                return null;

            return (p1, p2, pairBbox);
        }

        // --- Step 2: Build Column (tile vertically) ---

        private List<Part> BuildColumn(Part part1, Part part2, Box pairBbox)
        {
            var column = new List<Part> { (Part)part1.Clone(), (Part)part2.Clone() };

            var copyDistance = ComputeVerticalCopyDistance(part1, part2, pairBbox);
            if (copyDistance <= 0)
                return column;

            var pairHeight = pairBbox.Length;
            var currentY = pairBbox.Bottom + copyDistance;

            while (currentY + pairHeight <= workArea.Top + Tolerance.Epsilon)
            {
                var offset = new Vector(0, currentY - pairBbox.Bottom);
                column.Add(part1.CloneAtOffset(offset));
                column.Add(part2.CloneAtOffset(offset));
                currentY += copyDistance;
            }

            return column;
        }

        private double ComputeVerticalCopyDistance(Part p1, Part p2, Box pairBbox)
        {
            var pairHeight = pairBbox.Length;
            // Start the test pair high enough so it doesn't overlap the original pair's bounding box initially.
            var startOffset = pairHeight + partSpacing;
            var testParts = new List<Part> { p1.CloneAtOffset(new Vector(0, startOffset)), p2.CloneAtOffset(new Vector(0, startOffset)) };
            var obstacles = new List<Part> { p1, p2 };

            // Use a large work area to prevent edge-clamping during distance measurement.
            var largeWorkArea = new Box(workArea.X, workArea.Y - pairHeight, workArea.Width, workArea.Length + pairHeight * 3);
            var slide = Compactor.Push(testParts, obstacles, largeWorkArea, partSpacing, PushDirection.Down);

            // True copy distance = start - slide. Clamp to BB height + spacing to prevent BB overlap.
            var copyDist = startOffset - slide;
            return System.Math.Max(copyDist, pairHeight + partSpacing);
        }

        // --- Step 3: Iterative Adjustment ---

        private List<Part> AdjustColumn(
            (Part part1, Part part2, Box pairBbox) initialPair,
            List<Part> initialColumn,
            CancellationToken token)
        {
            var currentPair = initialPair;
            var currentColumn = initialColumn;
            var originalWidth = initialPair.pairBbox.Width;

            for (var iteration = 0; iteration < MaxIterations; iteration++)
            {
                if (token.IsCancellationRequested)
                    break;

                var columnBbox = ((IEnumerable<IBoundable>)currentColumn).GetBoundingBox();
                var gap = workArea.Top - columnBbox.Top;

                if (gap <= Tolerance.Epsilon)
                    break;

                var pairCount = currentColumn.Count / 2;
                var adjustment = gap / pairCount;
                if (adjustment <= Tolerance.Epsilon)
                    break;

                // Try shifting p2 up or down relative to p1 to see if we can close the gap
                // without making the pair wider than its initial horizontal footprint.
                var adjusted = TryAdjustPair(currentPair, adjustment, originalWidth);
                if (adjusted == null)
                    break;

                var newColumn = BuildColumn(adjusted.Value.part1, adjusted.Value.part2, adjusted.Value.pairBbox);
                if (newColumn.Count <= currentColumn.Count)
                    break; // No improvement in part count.

                currentColumn = newColumn;
                currentPair = adjusted.Value;
            }

            return currentColumn;
        }

        private (Part part1, Part part2, Box pairBbox)? TryAdjustPair(
            (Part part1, Part part2, Box pairBbox) pair,
            double adjustment, double maxWidth)
        {
            // Try shifting part2 up first.
            var result = CreatePair(pair.part1.BaseDrawing, pair.part1.Rotation, pair.part2.Rotation,
                (pair.part2.Location.Y - pair.part1.Location.Y) + adjustment);

            if (result != null && result.Value.pairBbox.Width <= maxWidth + Tolerance.Epsilon)
                return result;

            // Up made it wider or didn't fit — try down instead.
            result = CreatePair(pair.part1.BaseDrawing, pair.part1.Rotation, pair.part2.Rotation,
                (pair.part2.Location.Y - pair.part1.Location.Y) - adjustment);

            if (result != null && result.Value.pairBbox.Width <= maxWidth + Tolerance.Epsilon)
                return result;

            return null;
        }

        // --- Step 4: Horizontal Repetition ---

        private List<Part> RepeatColumns(List<Part> column, CancellationToken token)
        {
            if (column.Count == 0)
                return column;

            var columnBbox = ((IEnumerable<IBoundable>)column).GetBoundingBox();
            var columnWidth = columnBbox.Width;

            // Create a test column shifted right and compact it left to find the true copy distance.
            var startOffset = columnWidth + partSpacing;
            var testColumn = column.Select(p => p.CloneAtOffset(new Vector(startOffset, 0))).ToList();

            var slide = Compactor.Push(testColumn, column, workArea, partSpacing, PushDirection.Left);
            var copyDistance = startOffset - slide;

            if (copyDistance <= Tolerance.Epsilon)
                copyDistance = columnWidth + partSpacing;

            Debug.WriteLine($"[FillExtents] Column copy distance: {copyDistance:F2} (bbox width: {columnWidth:F2}, spacing: {partSpacing:F2})");

            var result = new List<Part>(column);
            var colIndex = 1;

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
