# NFP-Based Best-Fit Strategy

## Problem

The current best-fit pair generation uses `RotationSlideStrategy`, which samples Part2 positions by sliding it toward Part1 from 4 directions at discrete step sizes. This is brute-force: more precision requires more samples, it can miss optimal interlocking positions between steps, and it generates hundreds of candidates per rotation angle.

## Solution

Replace the slide-based sampling with NFP (No-Fit Polygon) computation. The NFP of two polygons gives the exact mathematical boundary of all valid positions where Part2 can touch Part1 without overlapping. Every point on that boundary is a guaranteed-valid candidate offset.

## Approach

Implement `NfpSlideStrategy : IBestFitStrategy` that plugs into the existing `BestFitFinder` pipeline. No changes to `PairEvaluator`, `BestFitFilter`, `BestFitResult`, tiling, or caching.

## Design

### New class: `NfpSlideStrategy`

**Location:** `OpenNest.Engine/BestFit/NfpSlideStrategy.cs`

**Implements:** `IBestFitStrategy`

**Constructor parameters:**
- `double part2Rotation` — rotation angle for Part2 (same as `RotationSlideStrategy`)
- `int type` — strategy type id (same as `RotationSlideStrategy`)
- `string description` — human-readable description

**`GenerateCandidates(Drawing drawing, double spacing, double stepSize)`:**

1. Extract perimeter polygon from the drawing inflated by `spacing / 2` using `PolygonHelper.ExtractPerimeterPolygon` (shared helper, extracted from `AutoNester`)
2. Create a rotated copy of the polygon at `part2Rotation` using `PolygonHelper.RotatePolygon` (also extracted)
3. Compute `NoFitPolygon.Compute(stationaryPoly, orbitingPoly)` — single call
4. If the NFP is null or has fewer than 3 vertices, return empty list
5. Walk the NFP boundary:
   - Each vertex becomes a `PairCandidate` with that vertex as `Part2Offset`
   - For edges longer than `stepSize`, add intermediate sample points at `stepSize` intervals along the edge (catches optimal positions on long straight NFP edges)
6. Return the candidates list

### Shared helper: `PolygonHelper`

**Location:** `OpenNest.Engine/BestFit/PolygonHelper.cs`

**Static methods extracted from `AutoNester`:**
- `ExtractPerimeterPolygon(Drawing drawing, double halfSpacing)` — extracts and inflates the perimeter polygon
- `RotatePolygon(Polygon polygon, double angle)` — creates a rotated copy normalized to origin

After extraction, `AutoNester` delegates to these methods to avoid duplication.

### Changes to `BestFitFinder.BuildStrategies`

Replace `RotationSlideStrategy` instances with `NfpSlideStrategy` instances. Same rotation angles from `GetRotationAngles(drawing)`, different strategy class. No `ISlideComputer` dependency needed.

```csharp
private List<IBestFitStrategy> BuildStrategies(Drawing drawing)
{
    var angles = GetRotationAngles(drawing);
    var strategies = new List<IBestFitStrategy>();
    var type = 1;

    foreach (var angle in angles)
    {
        var desc = $"{Angle.ToDegrees(angle):F1} deg NFP";
        strategies.Add(new NfpSlideStrategy(angle, type++, desc));
    }

    return strategies;
}
```

### No changes required

- `PairEvaluator` — still evaluates candidates (overlap check becomes redundant but harmless and fast)
- `BestFitFilter` — still filters results by aspect ratio, plate fit, etc.
- `BestFitResult` — unchanged
- `BestFitCache` — unchanged
- Tiling pipeline — unchanged
- `PairsFillStrategy` — unchanged

## Edge Sampling

NFP vertices alone may miss optimal positions along long straight edges. For each edge of the NFP polygon where `edgeLength > stepSize`, interpolate additional points at `stepSize` intervals. This reuses the existing `stepSize` parameter meaningfully — it controls resolution along NFP edges rather than grid spacing.

## Files Changed

| File | Change |
|------|--------|
| `OpenNest.Engine/BestFit/NfpSlideStrategy.cs` | New — `IBestFitStrategy` implementation |
| `OpenNest.Engine/BestFit/PolygonHelper.cs` | New — shared polygon extraction/rotation |
| `OpenNest.Engine/Nfp/AutoNester.cs` | Delegate to `PolygonHelper` methods |
| `OpenNest.Engine/BestFit/BestFitFinder.cs` | Swap `RotationSlideStrategy` for `NfpSlideStrategy` in `BuildStrategies` |

## What This Does NOT Change

- The `RotationSlideStrategy` class stays in the codebase (not deleted) in case GPU slide computation is still wanted
- The `ISlideComputer` / GPU pipeline remains available
- `BestFitFinder` constructor still accepts `ISlideComputer` but it won't be passed to NFP strategies (they don't need it)
