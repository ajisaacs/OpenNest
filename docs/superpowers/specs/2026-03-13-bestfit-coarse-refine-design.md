# Iterative Halving Sweep in RotationSlideStrategy

## Problem

`RotationSlideStrategy.GenerateCandidatesForAxis` sweeps the full perpendicular range at `stepSize` (default 0.25"), calling `Helper.DirectionalDistance` at every step. Profiling shows `DirectionalDistance` accounts for 62% of CPU during best-fit computation. For parts with large bounding boxes, this produces hundreds of steps per direction, making the Pairs phase take 2.5+ minutes.

## Solution

Replace the single fine sweep with an iterative halving search inside `GenerateCandidatesForAxis`. Starting at a coarse step size (16× the fine step), each iteration identifies the best offset regions by slide distance, then halves the step and re-sweeps only within narrow windows around those regions. This converges to the optimal offsets in ~85 `DirectionalDistance` calls vs ~160 for a full fine sweep.

## Design

### Modified method: `GenerateCandidatesForAxis`

Located in `OpenNest.Engine/BestFit/RotationSlideStrategy.cs`. The public `GenerateCandidates` method and all other code remain unchanged.

**Current flow:**
1. Sweep `alignedStart` to `perpMax` at `stepSize`
2. At each offset: clone part2, position, compute offset lines, call `DirectionalDistance`, build `PairCandidate`

**New flow:**

**Constants (local to the method):**
- `CoarseMultiplier = 16` — initial step is `stepSize * 16`
- `MaxRegions = 5` — top-N regions to keep per iteration

**Algorithm:**

1. Compute `currentStep = stepSize * CoarseMultiplier`
2. Set the initial sweep range to `[alignedStart, perpMax]` where `alignedStart = Math.Ceiling(perpMin / currentStep) * currentStep`
3. **Iteration loop** — while `currentStep > stepSize`:
   a. Sweep all active regions at `currentStep`, collecting `(offset, slideDist)` tuples:
      - For each offset in each region: clone part2, position, compute offset lines, call `DirectionalDistance`
      - Skip if `slideDist >= double.MaxValue || slideDist < 0`
   b. Select top `MaxRegions` hits by `slideDist` ascending (tightest fit first), deduplicating any hits within `currentStep` of an already-selected hit
   c. Build new regions: for each selected hit, the new region is `[offset - currentStep, offset + currentStep]`, clamped to `[perpMin, perpMax]`
   d. Halve: `currentStep /= 2`
   e. Align each region's start to a multiple of `currentStep`
4. **Final pass** — sweep all active regions at `stepSize`, generating full `PairCandidate` objects (same logic as current code: clone part2, position, compute offset lines, `DirectionalDistance`, build candidate)

**Iteration trace for a 20" range with `stepSize = 0.25`:**

| Pass | Step | Regions | Samples per region | Total samples |
|------|------|---------|--------------------|---------------|
| 1 | 4.0 | 1 (full range) | ~5 | ~5 |
| 2 | 2.0 | up to 5 | ~4 | ~20 |
| 3 | 1.0 | up to 5 | ~4 | ~20 |
| 4 | 0.5 | up to 5 | ~4 | ~20 |
| 5 (final) | 0.25 | up to 5 | ~4 | ~20 (generates candidates) |
| **Total** | | | | **~85** vs **~160 current** |

**Alignment:** Each pass aligns its sweep start to a multiple of `currentStep`. Since `currentStep` is always a power-of-two multiple of `stepSize`, offset=0 is always a sample point when it falls within a region. This preserves perfect grid arrangements for rectangular parts.

**Region deduplication:** When selecting top hits, any hit whose offset is within `currentStep` of a previously selected hit is skipped. This prevents overlapping refinement windows from wasting samples on the same area.

### Integration points

The changes are entirely within the private method `GenerateCandidatesForAxis`. The method signature, parameters, and return type (`List<PairCandidate>`) are unchanged. The only behavioral difference is that it generates fewer candidates overall (only from the promising regions), but those candidates cover the same quality range because the iterative search converges on the best offsets.

### Performance

- Current: ~160 `DirectionalDistance` calls per direction (20" range / 0.25 step)
- Iterative halving: ~85 calls (5 + 20 + 20 + 20 + 20)
- ~47% reduction in `DirectionalDistance` calls per direction
- Coarse passes are cheaper per-call since they only store `(offset, slideDist)` tuples rather than building full `PairCandidate` objects
- Total across 4 directions × N angles: proportional reduction throughout
- For larger parts (40"+ range), the savings are even greater since the coarse pass covers the range in very few samples

## Files Modified

| File | Change |
|------|--------|
| `OpenNest.Engine/BestFit/RotationSlideStrategy.cs` | Replace single sweep in `GenerateCandidatesForAxis` with iterative halving sweep |

## What Doesn't Change

- `RotationSlideStrategy.GenerateCandidates` — unchanged, calls `GenerateCandidatesForAxis` as before
- `BestFitFinder` — unchanged, calls `strategy.GenerateCandidates` as before
- `BestFitCache` — unchanged
- `PairEvaluator` / `IPairEvaluator` — unchanged
- `PairCandidate`, `BestFitResult`, `BestFitFilter` — unchanged
- `Helper.DirectionalDistance`, `Helper.GetOffsetPartLines` — reused as-is
- `NestEngine.FillWithPairs` — unchanged caller

## Edge Cases

- **Part smaller than initial coarseStep:** The first pass produces very few samples (possibly 1-2), but each subsequent halving still narrows correctly. For tiny parts, the total range may be smaller than `coarseStep`, so the algorithm effectively skips to finer passes quickly.
- **Refinement regions overlap after halving:** Deduplication at each iteration prevents selecting nearby hits. Even if two regions share a boundary after halving, at worst one offset is evaluated twice — negligible cost.
- **No valid hits at any pass:** If all offsets at a given step produce invalid slide distances, the hit list is empty, no regions are generated, and subsequent passes produce no candidates. This matches current behavior for parts that can't pair in the given direction.
- **Sweep region extends past bounds:** All regions are clamped to `[perpMin, perpMax]` at each iteration.
- **Only one valid region found:** The algorithm works correctly with 1 region — it just refines a single window instead of 5. This is common for simple rectangular parts where there's one clear best offset.
- **stepSize is not a power of two:** The halving produces steps like 4.0 → 2.0 → 1.0 → 0.5 → 0.25 regardless of whether `stepSize` is a power of two. The loop condition `currentStep > stepSize` terminates correctly because `currentStep` will eventually equal `stepSize` after enough halvings (since `CoarseMultiplier` is a power of 2).
