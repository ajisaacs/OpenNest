# Iterative Shrink-Fill Design

## Problem

`StripNestEngine` currently picks a single "strip" drawing (the highest-area item), shrink-fills it into the tightest sub-region, then fills remnants with remaining drawings. This wastes potential density — every drawing benefits from shrink-filling into its tightest sub-region, not just the first one.

## Design

### 1. IterativeShrinkFiller

New static class in `OpenNest.Engine/Fill/IterativeShrinkFiller.cs`.

**Responsibility:** Given an ordered list of multi-quantity `NestItem` and a work area, iteratively shrink-fill each item into the tightest sub-region using `RemnantFiller` + `ShrinkFiller`, returning placed parts and leftovers.

**Algorithm:**

1. Create a `RemnantFiller` with the work area and spacing.
2. Build a single fill function (closure) that wraps the caller-provided raw fill function with dual-direction shrink logic:
   - Calls `ShrinkFiller.Shrink` with `ShrinkAxis.Height` (bottom strip direction).
   - Calls `ShrinkFiller.Shrink` with `ShrinkAxis.Width` (left strip direction).
   - Compares results using `FillScore.Compute(parts, box)` where `box` is the remnant box passed by `RemnantFiller`. Since `FillScore` density is derived from placed parts' bounding box (not the work area parameter), the comparison is valid regardless of which box is used.
   - Returns the parts from whichever direction scores better.
3. Pass this wrapper function and all items to `RemnantFiller.FillItems`, which drives the iteration — discovering free rectangles, iterating over items and boxes, and managing obstacle tracking.
4. After `RemnantFiller.FillItems` returns, collect any unfilled quantities (including `Quantity <= 0` items which mean "unlimited") into a leftovers list.
5. Return placed parts and leftovers. Remaining free space for the pack pass is reconstructed from placed parts by the caller (existing pattern), not by returning `RemnantFinder` state.

**Data flow:** Caller provides a raw single-item fill function (e.g., `DefaultNestEngine.Fill`) → `IterativeShrinkFiller` wraps it in a dual-direction shrink closure → passes the wrapper to `RemnantFiller.FillItems` which drives the loop.

**Note on quantities:** `Quantity <= 0` means "fill as many as possible" (unlimited). These items are included in the fill bucket (qty != 1), not the pack bucket.

**Interface:**

```csharp
public class IterativeShrinkResult
{
    public List<Part> Parts { get; set; }
    public List<NestItem> Leftovers { get; set; }
}

public static class IterativeShrinkFiller
{
    public static IterativeShrinkResult Fill(
        List<NestItem> items,
        Box workArea,
        Func<NestItem, Box, List<Part>> fillFunc,
        double spacing,
        CancellationToken token);
}
```

The class composes `RemnantFiller` and `ShrinkFiller` — it does not duplicate their logic.

### 2. Revised StripNestEngine.Nest

**Note:** The rotating calipers angle is already included via `RotationAnalysis.FindBestRotation`, which calls `RotatingCalipers.MinimumBoundingRectangle` and feeds the result as `bestRotation` into `AngleCandidateBuilder.Build`. No changes needed to the angle pipeline.

The `Nest` override becomes a thin orchestrator:

1. Separate items into multi-quantity (qty != 1) and singles (qty == 1).
2. Sort multi-quantity items by `Priority` ascending, then `Drawing.Area` descending.
3. Call `IterativeShrinkFiller.Fill` with the sorted multi-quantity items.
4. Collect leftovers: unfilled multi-quantity remainders + all singles.
5. If leftovers exist and free space remains, run `PackArea` into the remaining area.
6. Deduct placed quantities from the original items. Return all parts.

**Deleted code:**
- `SelectStripItemIndex` method
- `EstimateStripDimension` method
- `TryOrientation` method
- `ShrinkFill` method

**Deleted files:**
- `StripNestResult.cs`
- `StripDirection.cs`

## Files Changed

| File | Change |
|------|--------|
| `OpenNest.Engine/Fill/IterativeShrinkFiller.cs` | New — orchestrates RemnantFiller + ShrinkFiller with dual-direction selection |
| `OpenNest.Engine/StripNestEngine.cs` | Rewrite Nest to use IterativeShrinkFiller + pack leftovers |
| `OpenNest.Engine/StripNestResult.cs` | Delete |
| `OpenNest.Engine/StripDirection.cs` | Delete |

## Not In Scope

- Trying multiple item orderings and picking the best overall `FillScore` — future follow-up once we confirm the iterative approach is fast enough.
- Changes to `NestEngineBase`, `DefaultNestEngine`, `RemnantFiller`, `ShrinkFiller`, `RemnantFinder`, `AngleCandidateBuilder`, `NestItem`, or UI code.
