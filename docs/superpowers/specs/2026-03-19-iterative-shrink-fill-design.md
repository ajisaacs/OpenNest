# Iterative Shrink-Fill Design

## Problem

`StripNestEngine` currently picks a single "strip" drawing (the highest-area item), shrink-fills it into the tightest sub-region, then fills remnants with remaining drawings. This wastes potential density — every drawing benefits from shrink-filling into its tightest sub-region, not just the first one.

Additionally, `AngleCandidateBuilder` does not include the rotating calipers minimum bounding rectangle angle, despite it being the mathematically optimal tight-fit rotation for rectangular work areas.

## Design

### 1. IterativeShrinkFiller

New static class in `OpenNest.Engine/Fill/IterativeShrinkFiller.cs`.

**Responsibility:** Given an ordered list of multi-quantity `NestItem` and a work area, iteratively shrink-fill each item into the tightest sub-region using `RemnantFiller` + `ShrinkFiller`, returning placed parts and leftovers.

**Algorithm:**

1. Create a `RemnantFiller` with the work area and spacing.
2. For each multi-quantity item (sorted by priority ascending, then area descending), provide a fill function to `RemnantFiller` that internally:
   - Calls `ShrinkFiller.Shrink` with `ShrinkAxis.Height` (bottom strip direction).
   - Calls `ShrinkFiller.Shrink` with `ShrinkAxis.Width` (left strip direction).
   - Returns the parts from whichever direction produces a better `FillScore`.
3. Collect any unfilled quantities into a leftovers list.
4. Return placed parts, leftovers, and the `RemnantFinder` state for a subsequent pack pass.

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

### 2. Rotating Calipers Angle in AngleCandidateBuilder

Add the rotating calipers minimum bounding rectangle angle to the base angles in `AngleCandidateBuilder.Build`.

**Current:** `baseAngles = [bestRotation, bestRotation + 90°]`

**Proposed:** `baseAngles = [bestRotation, bestRotation + 90°, caliperAngle, caliperAngle + 90°]` (deduplicated)

The caliper angle is pre-computed and cached on `NestItem.CaliperAngle` to avoid recomputing the pipeline (`Program.ToGeometry()` → `ShapeProfile` → `ToPolygonWithTolerance` → `RotatingCalipers.MinimumBoundingRectangle`) on every fill call.

This feeds into every downstream path (pruned known-good list, sweep, ML prediction) since they all start from `baseAngles`.

### 3. CaliperAngle on NestItem

Add a `double? CaliperAngle` property to `NestItem`. Pre-computed by the caller before passing items to the engine. When null, `AngleCandidateBuilder` skips the caliper angles (backward compatible).

Computation pipeline:
```csharp
var geometry = item.Drawing.Program.ToGeometry();
var shapeProfile = new ShapeProfile(geometry);
var polygon = shapeProfile.Perimeter.ToPolygonWithTolerance(0.001, circumscribe: true);
var result = RotatingCalipers.MinimumBoundingRectangle(polygon);
item.CaliperAngle = result.Angle;
```

### 4. Revised StripNestEngine.Nest

The `Nest` override becomes a thin orchestrator:

1. Separate items into multi-quantity (qty != 1) and singles (qty == 1).
2. Pre-compute and cache `CaliperAngle` on each item's `NestItem`.
3. Sort multi-quantity items by `Priority` ascending, then `Drawing.Area` descending.
4. Call `IterativeShrinkFiller.Fill` with the sorted multi-quantity items.
5. Collect leftovers: unfilled multi-quantity remainders + all singles.
6. If leftovers exist and free space remains, run `PackArea` into the remaining area.
7. Deduct placed quantities from the original items. Return all parts.

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
| `OpenNest.Engine/Fill/AngleCandidateBuilder.cs` | Add caliper angle + 90° to base angles from NestItem.CaliperAngle |
| `OpenNest.Engine/NestItem.cs` | Add `double? CaliperAngle` property |
| `OpenNest.Engine/StripNestEngine.cs` | Rewrite Nest to use IterativeShrinkFiller + pack leftovers |
| `OpenNest.Engine/StripNestResult.cs` | Delete |
| `OpenNest.Engine/StripDirection.cs` | Delete |

## Not In Scope

- Trying multiple item orderings and picking the best overall `FillScore` — future follow-up once we confirm the iterative approach is fast enough.
- Changes to `NestEngineBase`, `DefaultNestEngine`, `RemnantFiller`, `ShrinkFiller`, `RemnantFinder`, or UI code.
