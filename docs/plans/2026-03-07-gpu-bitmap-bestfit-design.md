# GPU Bitmap Best Fit Evaluation

## Overview

Add GPU-accelerated bitmap-based overlap testing to the best fit pair evaluation pipeline using ILGPU. Parts are rasterized to integer grids; overlap detection becomes cell comparison on the GPU. Runs alongside the existing geometry-based evaluator, selectable via flag.

## Architecture

New project `OpenNest.Gpu` (class library, `net8.0-windows`). References `OpenNest.Core` and `OpenNest.Engine`. NuGet: `ILGPU`, `ILGPU.Algorithms`.

## Components

### 1. `Polygon.ContainsPoint(Vector pt)` (Core)

Ray-cast from point rightward past bounding box. Count edge intersections with polygon segments. Odd = inside, even = outside.

### 2. `PartBitmap` (OpenNest.Gpu)

- Rasterizes a `Drawing` to `int[]` grid
- Pipeline: `ConvertProgram.ToGeometry()` -> `Helper.GetShapes()` -> `Shape.ToPolygonWithTolerance()` -> `Polygon.ContainsPoint()` per cell center
- Dilates filled cells by `spacing / 2 / cellSize` pixels to bake in part spacing
- Default cell size: 0.05"
- Cached per drawing (rasterize once, reuse across all candidates)

### 3. `IPairEvaluator` (Engine)

```csharp
interface IPairEvaluator
{
    List<BestFitResult> EvaluateAll(List<PairCandidate> candidates);
}
```

- `PairEvaluator` — existing geometry path (CPU parallel)
- `GpuPairEvaluator` — bitmap path (GPU batch)

### 4. `GpuPairEvaluator` (OpenNest.Gpu)

- Constructor takes `Drawing`, `cellSize`, `spacing`. Rasterizes `PartBitmap` once.
- `EvaluateAll()` uploads bitmap + candidate params to GPU, one kernel per candidate
- Kernel: for each cell, transform to part2 space (rotation + offset), check overlap, track bounding extent
- Results: overlap count (0 = valid), bounding width/height from min/max occupied cells
- `IDisposable` — owns ILGPU `Context` + `Accelerator`

### 5. `BestFitFinder` modification (Engine)

- Constructor accepts optional `IPairEvaluator`
- Falls back to `PairEvaluator` if none provided
- Candidate generation (strategies, rotation angles, slide) unchanged
- Calls `IPairEvaluator.EvaluateAll(candidates)` instead of inline `Parallel.ForEach`

### 6. Integration in `NestEngine`

- `FillWithPairs()` creates finder with either evaluator based on `UseGpu` flag
- UI layer toggles the flag

## Data Flow

```
Drawing -> PartBitmap (rasterize once, dilate for spacing)
               |
Strategies -> PairCandidates[] (rotation angles x slide offsets)
               |
GpuPairEvaluator.EvaluateAll():
  - Upload bitmap + candidate float4[] to GPU
  - Kernel per candidate: overlap check + bounding box
  - Download results
               |
BestFitFilter -> sort -> BestFitResults
```

## Unchanged

- `RotationSlideStrategy` and candidate generation
- `BestFitFilter`, `BestFitResult`, `TileEvaluator`
- `NestEngine.FillWithPairs()` flow (just swaps evaluator)
