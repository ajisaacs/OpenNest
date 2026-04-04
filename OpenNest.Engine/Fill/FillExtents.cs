using OpenNest.Engine.Strategies;
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
            IProgress<NestProgress> progress = null,
            List<Engine.BestFit.BestFitResult> bestFits = null)
        {
            var pair = BuildPair(drawing, rotationAngle);
            if (pair == null)
                return new List<Part>();

            var column = BuildColumn(pair.Value);
            if (column.Count == 0)
                return new List<Part>();

            NestEngineBase.ReportProgress(progress, new ProgressReport
            {
                Phase = NestPhase.Extents,
                PlateNumber = plateNumber,
                Parts = column,
                WorkArea = workArea,
                Description = $"Extents: initial column {column.Count} parts",
            });

            var adjusted = AdjustColumn(pair.Value, column, token);

            // The iterative pair adjustment can shift parts enough to cause
            // genuine overlap. Fall back to the unadjusted column when this happens.
            if (HasOverlappingParts(adjusted))
            {
                Debug.WriteLine("[FillExtents] Adjusted column has overlaps, using unadjusted");
                adjusted = column;
            }

            NestEngineBase.ReportProgress(progress, new ProgressReport
            {
                Phase = NestPhase.Extents,
                PlateNumber = plateNumber,
                Parts = adjusted,
                WorkArea = workArea,
                Description = $"Extents: column {adjusted.Count} parts",
            });

            var result = RepeatColumns(adjusted, token);

            NestEngineBase.ReportProgress(progress, new ProgressReport
            {
                Phase = NestPhase.Extents,
                PlateNumber = plateNumber,
                Parts = result,
                WorkArea = workArea,
                Description = $"Extents: {result.Count} parts total",
            });

            return result;
        }

        // --- Step 1: Pair Construction ---

        private PartPair? BuildPair(Drawing drawing, double rotationAngle)
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
            var startOffset = part1.BoundingBox.Length + part2.BoundingBox.Length + partSpacing;
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

            var pair = AnchorToWorkArea(part1, part2);
            if (pair == null)
                return null;

            // Verify pair fits in work area.
            if (pair.Value.Bbox.Width > workArea.Width + Tolerance.Epsilon ||
                pair.Value.Bbox.Length > workArea.Length + Tolerance.Epsilon)
                return null;

            return pair;
        }

        // --- Step 2: Build Column (tile vertically) ---

        private List<Part> BuildColumn(PartPair pair)
        {
            var column = new List<Part> { (Part)pair.Part1.Clone(), (Part)pair.Part2.Clone() };

            // Find geometry-aware copy distance for the pair vertically.
            var boundary1 = new PartBoundary(pair.Part1, halfSpacing);
            var boundary2 = new PartBoundary(pair.Part2, halfSpacing);

            // Compute vertical copy distance using bounding boxes as starting point,
            // then slide down to find true geometry distance.
            var pairHeight = pair.Bbox.Width;
            var testOffset = new Vector(0, pairHeight);

            // Create test parts for slide distance measurement.
            var testPart1 = pair.Part1.CloneAtOffset(testOffset);
            var testPart2 = pair.Part2.CloneAtOffset(testOffset);

            // Find minimum distance from test pair sliding down toward original pair.
            var copyDistance = FindVerticalCopyDistance(
                pair.Part1, pair.Part2, testPart1, testPart2,
                boundary1, boundary2, pairHeight);

            if (copyDistance <= 0)
                return column;

            var count = 1;
            while (true)
            {
                var nextBottom = pair.Bbox.Bottom + copyDistance * count;
                if (nextBottom + pairHeight > workArea.Top + Tolerance.Epsilon)
                    break;

                var offset = new Vector(0, copyDistance * count);
                column.Add(pair.Part1.CloneAtOffset(offset));
                column.Add(pair.Part2.CloneAtOffset(offset));
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
            var slidePairs = new[]
            {
                (moving: boundary1, movingLoc: testPart1.Location, stationary: boundary1, stationaryLoc: origPart1.Location),
                (moving: boundary1, movingLoc: testPart1.Location, stationary: boundary2, stationaryLoc: origPart2.Location),
                (moving: boundary2, movingLoc: testPart2.Location, stationary: boundary1, stationaryLoc: origPart1.Location),
                (moving: boundary2, movingLoc: testPart2.Location, stationary: boundary2, stationaryLoc: origPart2.Location),
            };

            var minSlide = double.MaxValue;
            foreach (var (moving, movingLoc, stationary, stationaryLoc) in slidePairs)
            {
                var d = SlideDistance(moving, movingLoc, stationary, stationaryLoc, PushDirection.Down);
                if (d < minSlide) minSlide = d;
            }

            if (minSlide >= double.MaxValue || minSlide < 0)
                return pairHeight + partSpacing;

            // Match FillLinear.ComputeCopyDistance: copyDist = startOffset - slide,
            // clamped so it never goes below pairHeight + partSpacing to prevent
            // bounding-box overlap from spurious slide values.
            var copyDist = pairHeight - minSlide;

            return System.Math.Max(copyDist, pairHeight + partSpacing);
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

        private List<Part> AdjustColumn(PartPair pair, List<Part> column, CancellationToken token)
        {
            var originalPairWidth = pair.Bbox.Length;

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

                var newColumn = BuildColumn(adjusted.Value);
                if (newColumn.Count == 0)
                    break;

                column = newColumn;
                pair = adjusted.Value;
            }

            return column;
        }

        private PartPair? TryAdjustPair(PartPair pair, double adjustment, double originalPairWidth)
        {
            // Try shifting part2 up first.
            var result = TryShiftDirection(pair, adjustment, originalPairWidth);

            if (result != null)
                return result;

            // Up made the pair wider — try down instead.
            return TryShiftDirection(pair, -adjustment, originalPairWidth);
        }

        private PartPair? TryShiftDirection(PartPair pair, double verticalShift, double originalPairWidth)
        {
            // Clone parts so we don't mutate the originals.
            var p1 = (Part)pair.Part1.Clone();
            var p2 = (Part)pair.Part2.Clone();

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
            var newBbox = PairBbox(p1, p2);

            if (newBbox.Length > originalPairWidth + Tolerance.Epsilon)
                return null;

            return AnchorToWorkArea(p1, p2);
        }

        // --- Step 4: Horizontal Repetition ---

        private List<Part> RepeatColumns(List<Part> column, CancellationToken token)
        {
            if (column.Count == 0)
                return column;

            var pattern = new Pattern();
            pattern.Parts.AddRange(column);
            pattern.UpdateBounds();

            var linear = new FillLinear(workArea, partSpacing);
            return linear.Fill(pattern, NestDirection.Horizontal);
        }

        // --- Helpers ---

        private PartPair? AnchorToWorkArea(Part part1, Part part2)
        {
            var bbox = PairBbox(part1, part2);
            var anchor = new Vector(workArea.X - bbox.Left, workArea.Y - bbox.Bottom);
            part1.Offset(anchor);
            part2.Offset(anchor);
            part1.UpdateBounds();
            part2.UpdateBounds();

            bbox = PairBbox(part1, part2);
            return new PartPair(part1, part2, bbox);
        }

        private static Box PairBbox(Part part1, Part part2) =>
            ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();

        private static bool HasOverlappingParts(List<Part> parts) =>
            FillHelpers.HasOverlappingParts(parts);

        private readonly record struct PartPair(Part Part1, Part Part2, Box Bbox);
    }
}
