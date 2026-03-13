# NFP Strategy in FindBestFill

## Problem

`NestEngine.FindBestFill()` currently runs three rectangle-based strategies (Linear, RectBestFit, Pairs) that treat parts as bounding boxes. For non-rectangular parts (L-shapes, circles, irregular profiles), this wastes significant plate area because the strategies can't interlock actual part geometry.

The NFP infrastructure already exists (used by `AutoNest`) but is completely separate from the single-drawing fill path.

## Solution

Add `FillNfpBestFit` as a new competing strategy in `FindBestFill()`. It uses the existing NFP/BLF infrastructure to place many copies of a single drawing using actual part geometry instead of bounding boxes. It only runs when the part is non-rectangular (where it can actually improve on grid packing).

## Design

### New method: `FillNfpBestFit(NestItem item, Box workArea)`

Located in `NestEngine.cs`, private method alongside `FillRectangleBestFit` and `FillWithPairs`.

**Algorithm:**

1. Compute `halfSpacing = Plate.PartSpacing / 2.0`
2. Extract the offset perimeter polygon via `ExtractPerimeterPolygon(drawing, halfSpacing)` (already exists as a private static method in NestEngine)
3. **Rectangularity gate:** compute `polygon.Area() / polygon.BoundingBox().Area()`. If ratio > 0.95, return empty list — grid strategies already handle rectangular parts optimally
4. Compute candidate rotation angles:
   - Start with hull edge angles via `ComputeHullEdgeAngles(polygon)` (already exists in NestEngine)
   - Always include 0° and 90°
   - Filter by `NestItem.RotationStart` / `NestItem.RotationEnd` window (keep angles where `RotationStart <= angle <= RotationEnd`; if both are 0, treat as unconstrained 0–360°)
5. Build an `NfpCache`:
   - For each candidate rotation, rotate the polygon and register it via `nfpCache.RegisterPolygon(drawing.Id, rotation, rotatedPolygon)`
   - Call `nfpCache.PreComputeAll()` — since all entries share the same drawing ID, this computes NFPs between all rotation pairs of the single part shape
6. For each candidate rotation, run `BottomLeftFill.Fill()`:
   - Build a sequence of N copies of `(drawing.Id, rotation, drawing)` where N = `(int)(workArea.Area() / polygon.Area())` capped to a reasonable max
   - Place via BLF which uses IFP minus NFP unions to find valid positions
7. Score each rotation's result via `FillScore.Compute(parts, workArea)`
8. Return the parts list from the highest-scoring rotation, converted via `BottomLeftFill.ToNestParts()`

### Integration into FindBestFill

Insert after the Pairs phase, before remainder improvement, in both overloads of `FindBestFill`:

```csharp
// NFP phase (non-rectangular parts only)
var nfpResult = FillNfpBestFit(item, workArea);
Debug.WriteLine($"[FindBestFill] NFP: {nfpResult?.Count ?? 0} parts");

if (IsBetterFill(nfpResult, best, workArea))
{
    best = nfpResult;
    ReportProgress(progress, NestPhase.Nfp, PlateNumber, best, workArea);
}
```

The progress-reporting overload also adds `token.ThrowIfCancellationRequested()` before the NFP phase.

### NestPhase enum

Add `Nfp` after `Pairs`:

```csharp
public enum NestPhase
{
    Linear,
    RectBestFit,
    Pairs,
    Nfp,
    Remainder
}
```

## Files Modified

| File | Change |
|------|--------|
| `OpenNest.Engine/NestEngine.cs` | Add `FillNfpBestFit()` method; call from both `FindBestFill` overloads after Pairs phase |
| `OpenNest.Engine/NestProgress.cs` | Add `Nfp` to `NestPhase` enum |

## What Doesn't Change

- `FillBestFit`, `FillLinear`, `FillWithPairs` — untouched
- `AutoNest` — separate code path, untouched
- `BottomLeftFill`, `NfpCache`, `NoFitPolygon`, `InnerFitPolygon` — reused as-is, no modifications
- UI callers (`ActionFillArea`, `ActionClone`, `PlateView.FillWithProgress`) — no changes, they call `NestEngine.Fill()` which calls `FindBestFill()` internally
- MCP tools (`NestingTools`) — no changes

## Edge Cases

- **Part with no valid perimeter polygon:** `ExtractPerimeterPolygon` returns null → return empty list
- **All rotations filtered out by constraints:** no BLF runs → return empty list
- **BLF places zero parts at a rotation:** skip that rotation, try others
- **Very small work area where part doesn't fit:** IFP computation returns invalid polygon → BLF places nothing → return empty list
