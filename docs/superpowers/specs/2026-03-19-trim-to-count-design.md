# Trim-to-Count: Replace ShrinkFiller Loop with Edge-Sorted Trim

## Problem

When a fill produces more parts than needed, `ShrinkFiller` iteratively shrinks the work area and re-fills from scratch until the count drops below target. Each iteration runs the full fill pipeline (pairs, bestfit, linear), making this expensive. Meanwhile, `DefaultNestEngine.Fill` trims excess parts with a blind `Take(N)` that ignores spatial position.

## Solution

Add `ShrinkFiller.TrimToCount` — a static method that sorts parts by their trailing edge and removes from the far end until the target count is reached. Replace the shrink loop and the blind `Take(N)` with calls to this method.

## Design

### New method: `ShrinkFiller.TrimToCount`

```csharp
internal static List<Part> TrimToCount(List<Part> parts, int targetCount, ShrinkAxis axis)
```

- Returns input unchanged if `parts.Count <= targetCount`
- Sorts ascending by trailing edge, takes the first `targetCount` parts (keeps parts nearest to origin, discards farthest):
  - `ShrinkAxis.Width` → sort ascending by `BoundingBox.Right`
  - `ShrinkAxis.Height` → sort ascending by `BoundingBox.Top`
- Returns a new list (does not mutate input)

### Changes to `ShrinkFiller.Shrink`

Replace the iterative shrink loop:

1. Fill once using existing `EstimateStartBox` + fallback logic (unchanged)
2. If count exceeds `shrinkTarget`, call `TrimToCount(parts, shrinkTarget, axis)`
3. Measure dimension from trimmed result via existing `MeasureDimension`
4. Report progress once after trim
5. Return `ShrinkResult`

Parameters removed from `Shrink`: `maxIterations` (no loop). The `spacing` parameter is kept (used by `EstimateStartBox`). `CancellationToken` is kept in the signature for API consistency even though the loop no longer uses it.

### Changes to `DefaultNestEngine.Fill`

Replace line 55-56:

```csharp
// Before:
if (item.Quantity > 0 && best.Count > item.Quantity)
    best = best.Take(item.Quantity).ToList();

// After:
if (item.Quantity > 0 && best.Count > item.Quantity)
    best = ShrinkFiller.TrimToCount(best, item.Quantity, ShrinkAxis.Width);
```

Defaults to `ShrinkAxis.Width` (trim by right edge) since this is the natural "end of nest" direction outside of a shrink context.

## Design Decisions

- **Axis-aware trimming**: Height shrink trims by top edge, width shrink trims by right edge. This respects the strip direction.
- **No pair integrity**: Trimming may split interlocking pairs. This is acceptable because if the layout is suboptimal, a better candidate will replace it during evaluation.
- **No edge spacing concerns**: The new dimension is simply the max edge of remaining parts. No snapping to spacing increments.
- **`MeasureDimension` unchanged**: It measures the occupied extent of remaining parts relative to `box.X`/`box.Y` (the work area origin). This works correctly after trimming.
- **`EstimateStartBox` preserved**: It was designed to accelerate the iterative loop, which is now gone. It still helps by producing a smaller starting fill, but could be simplified in a future pass.
- **Behavioral trade-off**: The shrink loop found the smallest box fitting N parts; trim-to-count reports the actual extent of the N nearest parts, which may be slightly less tight if there are gaps. In practice this is negligible since fill algorithms pack densely.

## Files Changed

- `OpenNest.Engine/Fill/ShrinkFiller.cs` — add `TrimToCount`, replace shrink loop, remove `maxIterations`
- `OpenNest.Engine/DefaultNestEngine.cs` — replace `Take(N)` with `TrimToCount`
- `OpenNest.Tests/ShrinkFillerTests.cs` — delete `Shrink_RespectsMaxIterations` test (concept no longer exists), update remaining tests, add `TrimToCount` tests
