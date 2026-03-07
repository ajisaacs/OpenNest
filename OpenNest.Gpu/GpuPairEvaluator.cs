using System;
using System.Collections.Generic;
using System.Linq;
using ILGPU;
using ILGPU.Runtime;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;

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

            var dilation = _spacing / 2.0;
            var bitmapA = PartBitmap.FromDrawing(_drawing, _cellSize, dilation);

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
                var bitmapB = PartBitmap.FromDrawingRotated(_drawing, rotation, _cellSize, dilation);

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

                // Map results back
                for (var i = 0; i < candidateCount; i++)
                {
                    var item = groupItems[i];
                    var score = resultScores[i];
                    var hasOverlap = score <= 0f;

                    var combinedWidth = gridWidth * _cellSize;
                    var combinedHeight = gridHeight * _cellSize;

                    allResults[item.OriginalIndex] = new BestFitResult
                    {
                        Candidate = item.Candidate,
                        RotatedArea = hasOverlap ? 0 : combinedWidth * combinedHeight,
                        BoundingWidth = combinedWidth,
                        BoundingHeight = combinedHeight,
                        OptimalRotation = 0,
                        TrueArea = trueArea,
                        Keep = !hasOverlap,
                        Reason = hasOverlap ? "Overlap detected" : "Valid"
                    };
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
