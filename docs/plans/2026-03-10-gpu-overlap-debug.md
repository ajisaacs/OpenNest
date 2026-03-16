# GPU Pair Evaluator — Overlap Detection Bug

**Date**: 2026-03-10
**Status**: RESOLVED — commit b55aa7a

## Problem

The `GpuPairEvaluator` reports "Overlap detected" for ALL best-fit candidates, even though the parts are clearly not overlapping. The CPU `PairEvaluator` works correctly (screenshot comparison: GPU = all red/overlap, CPU = blue with valid results like 93.9% utilization).

## Root Cause (identified but not yet fully fixed)

The bitmap coordinate system doesn't match the `Part2Offset` coordinate system.

### How Part2Offset is computed
`RotationSlideStrategy` creates parts using `Part.CreateAtOrigin(drawing, rotation)` which:
1. Clones the drawing's program
2. Rotates it
3. Calls `Program.BoundingBox()` to get the bbox
4. Offsets by `-bbox.Location` to normalize to origin

`Part2Offset` is the final position of Part2 in this **normalized** coordinate space.

### How bitmaps are rasterized
`PartBitmap.FromDrawing` / `FromDrawingRotated`:
1. Extracts closed polygons from the drawing (filters out rapids, open shapes)
2. Rotates them (for B)
3. Rasterizes with `OriginX/Y = polygon min`

### The mismatch
`Program.BoundingBox()` initializes `minX=0, minY=0, maxX=0, maxY=0` (line 289-292 in Program.cs), so (0,0) is **always** included in the bbox. This means:
- For geometry at (5,3)-(10,8): bbox.Location = (0,0), CreateAtOrigin shifts by (0,0) = no change
- But polygon min = (5,3), so bitmap OriginX=5, OriginY=3
- Part2Offset is in the (0,0)-based normalized space, bitmap is in the (5,3)-based polygon space

For rotated geometry, the discrepancy is even worse because rotation changes the polygon min dramatically while the bbox may or may not include (0,0).

## What we tried

### Attempt 1: BlitPair approach (correct but too slow)
- Added `PartBitmap.BlitPair()` that places both bitmaps into a shared world-space grid
- Eliminated all offset math from the kernel (trivial element-wise AND)
- **Problem**: Per-candidate grid allocation. 21K candidates × large grids = massive memory + GPU transfer. Took minutes instead of seconds.

### Attempt 2: Integer offsets with gap correction
- Kept shared-bitmap approach (one A + one B per rotation group)
- Changed offsets from `float` to `int` with `Math.Round()` on CPU
- Added gap correction: `offset = (Part2Offset - gapA + gapB) / cellSize` where `gapA = bitmapOriginA - bboxA.Location`, `gapB = bitmapOriginB - bboxB.Location`
- **Problem**: Still false positives. The formula is mathematically correct in derivation but something is wrong in practice.

### Attempt 3: Normalize bitmaps to match CreateAtOrigin (current state)
- Added `PartBitmap.FromDrawingAtOrigin()` and `FromDrawingAtOriginRotated()`
- These shift polygons by `-bbox.Location` before rasterizing, exactly like `CreateAtOrigin`
- Offset formula: `(Part2Offset.X - bitmapA.OriginX + bitmapB.OriginX) / cellSize`
- **Problem**: STILL showing false overlaps for all candidates (see gpu.png). 33.8s compute, 3942 kept but all marked overlap.

## Current state of code

### Files modified

**`OpenNest.Gpu/PartBitmap.cs`**:
- Added `BlitPair()` static method (from attempt 1, still present but unused)
- Added `FromDrawingAtOrigin()` — normalizes polygons by `-bbox.Location` before rasterize
- Added `FromDrawingAtOriginRotated()` — rotates polygons, clones+rotates program for bbox, normalizes, rasterizes

**`OpenNest.Gpu/GpuPairEvaluator.cs`**:
- Uses `FromDrawingAtOrigin` / `FromDrawingAtOriginRotated` instead of raw `FromDrawing` / `FromDrawingRotated`
- Offsets are `int[]` (not `float[]`) computed with `Math.Round()` on CPU
- Kernel is `OverlapKernel` — uses integer offsets, early-exit on `cellA != 1`
- `PadBitmap` helper restored
- Removed the old `NestingKernel` with float offsets

**`OpenNest/Forms/MainForm.cs`**:
- Added `using OpenNest.Engine.BestFit;`
- Wired up GPU evaluator: `BestFitCache.CreateEvaluator = (drawing, spacing) => GpuEvaluatorFactory.Create(drawing, spacing);`

## Next steps to debug

1. **Add diagnostic logging** to compare GPU vs CPU for a single candidate:
   - Print bitmapA: OriginX, OriginY, Width, Height
   - Print bitmapB: OriginX, OriginY, Width, Height
   - Print the computed integer offset
   - Print the overlap count from the kernel
   - Compare with CPU `PairEvaluator.CheckOverlap()` result for the same candidate

2. **Verify Program.Clone() + Rotate() produces same geometry as Polygon.Rotate()**:
   - `FromDrawingAtOriginRotated` rotates polygons with `poly.Rotate(rotation)` then normalizes using `prog.Clone().Rotate(rotation).BoundingBox()`
   - If `Program.Rotate` and `Polygon.Rotate` use different rotation centers or conventions, the normalization would be wrong
   - Check: does `Program.Rotate` rotate around (0,0)? Does `Polygon.Rotate` rotate around (0,0)?

3. **Try rasterizing from the Part directly**: Instead of extracting polygons from the raw drawing and manually rotating/normalizing, create `Part.CreateAtOrigin(drawing, rotation)` and extract polygons from the Part's already-normalized program. This guarantees exact coordinate system match.

4. **Consider that the kernel grid might be too small**: `gridWidth = max(A.Width, B.Width)` only works if offset is small. If Part2Offset places B far from A, the B cells at `bx = x - offset` could all be out of bounds (negative), leading the kernel to find zero overlaps (false negative). But we're seeing false POSITIVES, so this isn't the issue unless the offset sign is wrong.

5. **Check offset sign**: Verify that when offset is positive, `bx = x - offset` correctly maps A cells to B cells. A positive offset should mean B is shifted right relative to A.

## Performance notes
- CPU evaluator: 25.0s compute, 5954 kept, correct results
- GPU evaluator (current): 33.8s compute, 3942 kept, all false overlaps
- GPU is actually SLOWER because `FromDrawingAtOriginRotated` clones+rotates the full program per rotation group
- Once overlap detection is fixed, performance optimization should focus on avoiding the Program.Clone().Rotate() per rotation group

## Key files to reference
- `OpenNest.Gpu/GpuPairEvaluator.cs` — the GPU evaluator
- `OpenNest.Gpu/PartBitmap.cs` — bitmap rasterization
- `OpenNest.Engine/BestFit/PairEvaluator.cs` — CPU evaluator (working reference)
- `OpenNest.Engine/BestFit/RotationSlideStrategy.cs` — generates Part2Offset values
- `OpenNest.Core/Part.cs:109` — `Part.CreateAtOrigin()`
- `OpenNest.Core/CNC/Program.cs:281-342` — `Program.BoundingBox()` (note min init at 0,0)
- `OpenNest.Engine/BestFit/BestFitCache.cs` — where evaluator is plugged in
- `OpenNest/Forms/MainForm.cs` — where GPU evaluator is wired up
