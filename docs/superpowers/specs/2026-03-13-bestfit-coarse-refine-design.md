# Coarse-then-Refine Sweep in RotationSlideStrategy

## Problem

`RotationSlideStrategy.GenerateCandidatesForAxis` sweeps the full perpendicular range at `stepSize` (default 0.25"), calling `Helper.DirectionalDistance` at every step. Profiling shows `DirectionalDistance` accounts for 62% of CPU during best-fit computation. For parts with large bounding boxes, this produces hundreds of steps per direction, making the Pairs phase take 2.5+ minutes.

## Solution

Replace the single fine sweep with a two-phase coarse-then-refine sweep inside `GenerateCandidatesForAxis`. The coarse pass identifies promising offsets using slide distance as a cheap quality signal, then the fine pass generates candidates only in narrow windows around those promising regions.

## Design

### Modified method: `GenerateCandidatesForAxis`

Located in `OpenNest.Engine/BestFit/RotationSlideStrategy.cs`. The public `GenerateCandidates` method and all other code remain unchanged.

**Current flow:**
1. Sweep `alignedStart` to `perpMax` at `stepSize`
2. At each offset: clone part2, position, compute offset lines, call `DirectionalDistance`, build `PairCandidate`

**New flow:**

1. Compute `coarseStep = stepSize * 4`
2. **Coarse sweep** from `alignedStart` (aligned to `coarseStep`) to `perpMax` at `coarseStep`:
   - Clone part2, position at offset, compute offset lines
   - Call `DirectionalDistance` to get `slideDist`
   - If `slideDist >= double.MaxValue || slideDist < 0`, skip
   - Store `(offset, slideDist)` in a list
3. **Select refinement regions:**
   - Sort coarse hits by `slideDist` ascending (tightest fit first)
   - Take up to 5 hits, skipping any whose offset is within `coarseStep` of an already-selected hit (deduplicates overlapping windows)
4. **Fine sweep** each selected region from `offset - coarseStep` to `offset + coarseStep` at `stepSize`:
   - Clamp to `[alignedStart, perpMax]` so we don't sweep outside the original range
   - Align the region start to a multiple of `stepSize` (ensures offset=0 is always tested when present in the region)
   - At each fine offset: clone part2, position, compute offset lines, call `DirectionalDistance`, build `PairCandidate` (same logic as current code)
   - This produces ~8 fine steps per region (2 × coarseStep / stepSize = 2 × 1.0 / 0.25 = 8)

**Coarse alignment:** `alignedStart` for the coarse pass is `Math.Ceiling(perpMin / coarseStep) * coarseStep`, ensuring offset=0 is always included (since `coarseStep` is a multiple of `stepSize`).

**Constants:** The coarse multiplier (4) and max refinement regions (5) are local constants in the method, not configurable. These values balance coverage vs speed for the typical part sizes in this application.

### Performance

- Current: ~160 `DirectionalDistance` calls per direction (20" range / 0.25 step)
- Coarse pass: ~40 calls (20" / 1.0 step) — 75% reduction
- Refinement: ~40 calls total (5 regions × 8 steps)
- Net: ~80 calls vs ~160 = ~50% reduction per direction, with the coarse pass being much cheaper since it only stores tuples rather than building full candidates
- Total across 4 directions × N angles: proportional reduction throughout

## Files Modified

| File | Change |
|------|--------|
| `OpenNest.Engine/BestFit/RotationSlideStrategy.cs` | Replace single sweep in `GenerateCandidatesForAxis` with coarse-then-refine two-phase sweep |

## What Doesn't Change

- `RotationSlideStrategy.GenerateCandidates` — unchanged, calls `GenerateCandidatesForAxis` as before
- `BestFitFinder` — unchanged, calls `strategy.GenerateCandidates` as before
- `BestFitCache` — unchanged
- `PairEvaluator` / `IPairEvaluator` — unchanged
- `PairCandidate`, `BestFitResult`, `BestFitFilter` — unchanged
- `Helper.DirectionalDistance`, `Helper.GetOffsetPartLines` — reused as-is
- `NestEngine.FillWithPairs` — unchanged caller

## Edge Cases

- **Part smaller than coarseStep:** The coarse sweep still works — it just produces fewer hits, and refinement still covers the full range around those hits
- **Refinement regions overlap:** Deduplication in region selection prevents redundant fine sweeps
- **No coarse hits (all offsets produce invalid slides):** No refinement regions selected, method returns empty list — same as current behavior when no valid candidates exist
- **Fine sweep region extends past bounds:** Clamped to `[alignedStart, perpMax]`
- **stepSize not evenly divisible into coarseStep:** `coarseStep` is always `stepSize * 4`, so it's always a clean multiple
