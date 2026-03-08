using System;
using System.Collections.Generic;
using System.Linq;
using ILGPU;
using ILGPU.Runtime;
using OpenNest.Converters;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Gpu
{
    public class GpuPairEvaluator : IPairEvaluator, IDisposable
    {
        private readonly Context _context;
        private readonly Accelerator _accelerator;
        private readonly Drawing _drawing;
        private readonly double _spacing;
        private readonly double _cellSize;

        public GpuPairEvaluator(Drawing drawing, double spacing, double cellSize = PartBitmap.DefaultCellSize)
        {
            _drawing = drawing;
            _spacing = spacing;
            _cellSize = cellSize;
            _context = Context.CreateDefault();
            _accelerator = _context.GetPreferredDevice(preferCPU: false)
                .CreateAccelerator(_context);
        }

        public List<BestFitResult> EvaluateAll(List<PairCandidate> candidates)
        {
            if (candidates.Count == 0)
                return new List<BestFitResult>();

            // No dilation — candidate positions already include spacing
            // (baked in by RotationSlideStrategy via half-spacing offset lines).
            var bitmapA = PartBitmap.FromDrawing(_drawing, _cellSize);

            if (bitmapA.Width == 0 || bitmapA.Height == 0)
                return candidates.Select(c => MakeEmptyResult(c)).ToList();

            // Group candidates by Part2Rotation so we rasterize B once per unique rotation
            var groups = candidates
                .Select((c, i) => new { Candidate = c, OriginalIndex = i })
                .GroupBy(x => System.Math.Round(x.Candidate.Part2Rotation, 6));

            var allResults = new BestFitResult[candidates.Count];
            var trueArea = _drawing.Area * 2;

            foreach (var group in groups)
            {
                var rotation = group.Key;
                var groupItems = group.ToList();

                // Rasterize B at this rotation
                var bitmapB = PartBitmap.FromDrawingRotated(_drawing, rotation, _cellSize);

                if (bitmapB.Width == 0 || bitmapB.Height == 0)
                {
                    foreach (var item in groupItems)
                        allResults[item.OriginalIndex] = MakeEmptyResult(item.Candidate);
                    continue;
                }

                // Use the max dimensions so both bitmaps fit on the same grid
                var gridWidth = System.Math.Max(bitmapA.Width, bitmapB.Width);
                var gridHeight = System.Math.Max(bitmapA.Height, bitmapB.Height);

                var paddedA = PadBitmap(bitmapA, gridWidth, gridHeight);
                var paddedB = PadBitmap(bitmapB, gridWidth, gridHeight);

                // Pack candidate offsets: convert world offset to cell offset
                var candidateCount = groupItems.Count;
                var offsets = new float[candidateCount * 3];

                for (var i = 0; i < candidateCount; i++)
                {
                    var c = groupItems[i].Candidate;
                    // Convert world-space offset to cell-space offset relative to bitmapA origin
                    offsets[i * 3 + 0] = (float)((c.Part2Offset.X - bitmapA.OriginX + bitmapB.OriginX) / _cellSize);
                    offsets[i * 3 + 1] = (float)((c.Part2Offset.Y - bitmapA.OriginY + bitmapB.OriginY) / _cellSize);
                    offsets[i * 3 + 2] = (float)c.Part2Rotation;
                }

                var resultScores = new float[candidateCount];

                using var gpuPaddedA = _accelerator.Allocate1D(paddedA);
                using var gpuPaddedB = _accelerator.Allocate1D(paddedB);
                using var gpuOffsets = _accelerator.Allocate1D(offsets);
                using var gpuResults = _accelerator.Allocate1D(resultScores);

                var kernel = _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D,
                    ArrayView1D<int, Stride1D.Dense>,
                    ArrayView1D<int, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>,
                    int, int>(NestingKernel);

                kernel(candidateCount, gpuPaddedA.View, gpuPaddedB.View,
                    gpuOffsets.View, gpuResults.View, gridWidth, gridHeight);

                _accelerator.Synchronize();
                gpuResults.CopyToCPU(resultScores);

                // Map results back — compute proper bounding metrics for valid candidates
                for (var i = 0; i < candidateCount; i++)
                {
                    var item = groupItems[i];
                    var score = resultScores[i];
                    var hasOverlap = score <= 0f;

                    if (hasOverlap)
                    {
                        allResults[item.OriginalIndex] = new BestFitResult
                        {
                            Candidate = item.Candidate,
                            RotatedArea = 0,
                            BoundingWidth = 0,
                            BoundingHeight = 0,
                            OptimalRotation = 0,
                            TrueArea = trueArea,
                            Keep = false,
                            Reason = "Overlap detected"
                        };
                    }
                    else
                    {
                        allResults[item.OriginalIndex] = ComputeBoundingResult(item.Candidate, trueArea);
                    }
                }
            }

            return allResults.ToList();
        }

        private static void NestingKernel(
            Index1D index,
            ArrayView1D<int, Stride1D.Dense> partBitmapA,
            ArrayView1D<int, Stride1D.Dense> partBitmapB,
            ArrayView1D<float, Stride1D.Dense> candidateOffsets,
            ArrayView1D<float, Stride1D.Dense> results,
            int gridWidth,
            int gridHeight)
        {
            var candidateIdx = index * 3;
            var offsetX = candidateOffsets[candidateIdx];
            var offsetY = candidateOffsets[candidateIdx + 1];
            // rotation is already baked into partBitmapB, offset is what matters

            var overlapCount = 0;
            var totalOccupied = 0;

            for (var y = 0; y < gridHeight; y++)
            {
                for (var x = 0; x < gridWidth; x++)
                {
                    var cellA = partBitmapA[y * gridWidth + x];

                    // Apply offset to look up part B's cell
                    var bx = (int)(x - offsetX);
                    var by = (int)(y - offsetY);

                    var cellB = 0;
                    if (bx >= 0 && bx < gridWidth && by >= 0 && by < gridHeight)
                        cellB = partBitmapB[by * gridWidth + bx];

                    if (cellA == 1 && cellB == 1)
                        overlapCount++;
                    if (cellA == 1 || cellB == 1)
                        totalOccupied++;
                }
            }

            if (overlapCount > 0)
                results[index] = 0f;
            else
                results[index] = (float)totalOccupied / (gridWidth * gridHeight);
        }

        private static int[] PadBitmap(PartBitmap bitmap, int targetWidth, int targetHeight)
        {
            if (bitmap.Width == targetWidth && bitmap.Height == targetHeight)
                return bitmap.Cells;

            var padded = new int[targetWidth * targetHeight];

            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    padded[y * targetWidth + x] = bitmap.Cells[y * bitmap.Width + x];
                }
            }

            return padded;
        }

        private const double ChordTolerance = 0.01;

        private BestFitResult ComputeBoundingResult(PairCandidate candidate, double trueArea)
        {
            var part1 = new Part(_drawing);
            var bbox1 = part1.Program.BoundingBox();
            part1.Offset(-bbox1.Location.X, -bbox1.Location.Y);
            part1.UpdateBounds();

            var part2 = new Part(_drawing);
            if (!candidate.Part2Rotation.IsEqualTo(0))
                part2.Rotate(candidate.Part2Rotation);
            var bbox2 = part2.Program.BoundingBox();
            part2.Offset(-bbox2.Location.X, -bbox2.Location.Y);
            part2.Location = candidate.Part2Offset;
            part2.UpdateBounds();

            var allPoints = GetPartVertices(part1);
            allPoints.AddRange(GetPartVertices(part2));

            double bestArea, bestWidth, bestHeight, bestRotation;

            if (allPoints.Count >= 3)
            {
                var hull = ConvexHull.Compute(allPoints);
                var result = RotatingCalipers.MinimumBoundingRectangle(hull);
                bestArea = result.Area;
                bestWidth = result.Width;
                bestHeight = result.Height;
                bestRotation = result.Angle;
            }
            else
            {
                var combinedBox = ((IEnumerable<IBoundable>)new IBoundable[] { part1, part2 }).GetBoundingBox();
                bestArea = combinedBox.Area();
                bestWidth = combinedBox.Width;
                bestHeight = combinedBox.Height;
                bestRotation = 0;
            }

            return new BestFitResult
            {
                Candidate = candidate,
                RotatedArea = bestArea,
                BoundingWidth = bestWidth,
                BoundingHeight = bestHeight,
                OptimalRotation = bestRotation,
                TrueArea = trueArea,
                Keep = true,
                Reason = "Valid"
            };
        }

        private static List<Vector> GetPartVertices(Part part)
        {
            var entities = ConvertProgram.ToGeometry(part.Program)
                .Where(e => e.Layer != SpecialLayers.Rapid);
            var shapes = Helper.GetShapes(entities);
            var points = new List<Vector>();

            foreach (var shape in shapes)
            {
                var polygon = shape.ToPolygonWithTolerance(ChordTolerance);
                polygon.Offset(part.Location);

                foreach (var vertex in polygon.Vertices)
                    points.Add(vertex);
            }

            return points;
        }

        private static BestFitResult MakeEmptyResult(PairCandidate candidate)
        {
            return new BestFitResult
            {
                Candidate = candidate,
                RotatedArea = 0,
                BoundingWidth = 0,
                BoundingHeight = 0,
                OptimalRotation = 0,
                TrueArea = 0,
                Keep = false,
                Reason = "No geometry"
            };
        }

        public void Dispose()
        {
            _accelerator?.Dispose();
            _context?.Dispose();
        }
    }
}
