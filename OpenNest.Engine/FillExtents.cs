using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
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

            // Clamp: never let geometry quirks produce a distance smaller than
            // the bounding box height (which would overlap).
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

        // --- Step 3: Iterative Adjustment (stub for Task 3) ---

        private List<Part> AdjustColumn(
            (Part part1, Part part2, Box pairBbox) pair,
            List<Part> column,
            CancellationToken token)
        {
            return column;
        }

        // --- Step 4: Horizontal Repetition (stub for Task 4) ---

        private List<Part> RepeatColumns(List<Part> column, CancellationToken token)
        {
            return column;
        }
    }
}
