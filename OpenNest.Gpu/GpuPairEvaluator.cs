using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

            // Rasterize A from a Part created at the origin — guarantees the
            // bitmap coordinate system exactly matches Part.CreateAtOrigin.
            var partA = Part.CreateAtOrigin(_drawing);
            var bitmapA = PartBitmap.FromPart(partA, _cellSize);

            if (bitmapA.Width == 0 || bitmapA.Height == 0)
                return candidates.Select(c => MakeEmptyResult(c)).ToList();

            // Pre-compute A vertices once for all rotation groups
            var verticesA = GetPartVertices(partA);

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

                // Rasterize B at this rotation from a Part created at origin
                var partB = Part.CreateAtOrigin(_drawing, rotation);
                var bitmapB = PartBitmap.FromPart(partB, _cellSize);

                if (bitmapB.Width == 0 || bitmapB.Height == 0)
                {
                    foreach (var item in groupItems)
                        allResults[item.OriginalIndex] = MakeEmptyResult(item.Candidate);
                    continue;
                }

                // Pre-compute B vertices at origin for this rotation group
                var verticesB = GetPartVertices(partB);
                var locationB = partB.Location;

                // Use the max dimensions so both bitmaps fit on the same grid
                var gridWidth = System.Math.Max(bitmapA.Width, bitmapB.Width);
                var gridHeight = System.Math.Max(bitmapA.Height, bitmapB.Height);

                var paddedA = PadBitmap(bitmapA, gridWidth, gridHeight);
                var paddedB = PadBitmap(bitmapB, gridWidth, gridHeight);

                // The CPU evaluator replaces partB.Location with Part2Offset,
                // so the world-space shift from B's bitmap position is
                // (Part2Offset - partB.Location). We convert that shift to
                // pixel coordinates, adjusting for the bitmap origin difference.
                var candidateCount = groupItems.Count;
                var offsets = new int[candidateCount * 2];

                for (var i = 0; i < candidateCount; i++)
                {
                    var c = groupItems[i].Candidate;
                    var shiftX = c.Part2Offset.X - locationB.X;
                    var shiftY = c.Part2Offset.Y - locationB.Y;
                    offsets[i * 2 + 0] = (int)System.Math.Round((shiftX + bitmapB.OriginX - bitmapA.OriginX) / _cellSize);
                    offsets[i * 2 + 1] = (int)System.Math.Round((shiftY + bitmapB.OriginY - bitmapA.OriginY) / _cellSize);
                }

                var resultScores = new int[candidateCount];

                using var gpuPaddedA = _accelerator.Allocate1D(paddedA);
                using var gpuPaddedB = _accelerator.Allocate1D(paddedB);
                using var gpuOffsets = _accelerator.Allocate1D(offsets);
                using var gpuResults = _accelerator.Allocate1D(resultScores);

                var kernel = _accelerator.LoadAutoGroupedStreamKernel<
                    Index1D,
                    ArrayView1D<int, Stride1D.Dense>,
                    ArrayView1D<int, Stride1D.Dense>,
                    ArrayView1D<int, Stride1D.Dense>,
                    ArrayView1D<int, Stride1D.Dense>,
                    int, int>(OverlapKernel);

                kernel(candidateCount, gpuPaddedA.View, gpuPaddedB.View,
                    gpuOffsets.View, gpuResults.View, gridWidth, gridHeight);

                _accelerator.Synchronize();
                gpuResults.CopyToCPU(resultScores);

                // Process results in parallel — pre-computed vertices avoid
                // per-candidate Part creation, and Parallel.For matches the
                // CPU evaluator's Parallel.ForEach concurrency.
                Parallel.For(0, candidateCount, i =>
                {
                    var item = groupItems[i];
                    var hasOverlap = resultScores[i] > 0;

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
                        allResults[item.OriginalIndex] = ComputeBoundingResult(
                            item.Candidate, trueArea, verticesA, verticesB, locationB);
                    }
                });
            }

            return allResults.ToList();
        }

        /// <summary>
        /// Overlap kernel using integer cell offsets pre-rounded on the CPU.
        /// Shares one A and one B bitmap across all candidates — only the
        /// integer offset varies per candidate.
        /// </summary>
        private static void OverlapKernel(
            Index1D index,
            ArrayView1D<int, Stride1D.Dense> partBitmapA,
            ArrayView1D<int, Stride1D.Dense> partBitmapB,
            ArrayView1D<int, Stride1D.Dense> candidateOffsets,
            ArrayView1D<int, Stride1D.Dense> results,
            int gridWidth,
            int gridHeight)
        {
            var offsetX = candidateOffsets[index * 2];
            var offsetY = candidateOffsets[index * 2 + 1];

            var overlapCount = 0;

            for (var y = 0; y < gridHeight; y++)
            {
                for (var x = 0; x < gridWidth; x++)
                {
                    var cellA = partBitmapA[y * gridWidth + x];
                    if (cellA != 1) continue;

                    var bx = x - offsetX;
                    var by = y - offsetY;

                    if (bx >= 0 && bx < gridWidth && by >= 0 && by < gridHeight)
                    {
                        if (partBitmapB[by * gridWidth + bx] == 1)
                            overlapCount++;
                    }
                }
            }

            results[index] = overlapCount;
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

        private static BestFitResult ComputeBoundingResult(
            PairCandidate candidate, double trueArea,
            List<Vector> verticesA, List<Vector> verticesB, Vector locationB)
        {
            var shift = candidate.Part2Offset - locationB;

            var allPoints = new List<Vector>(verticesA.Count + verticesB.Count);
            allPoints.AddRange(verticesA);

            foreach (var v in verticesB)
                allPoints.Add(v + shift);

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
                bestArea = 0;
                bestWidth = 0;
                bestHeight = 0;
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
            var shapes = ShapeBuilder.GetShapes(entities);
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
