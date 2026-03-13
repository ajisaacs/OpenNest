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
2. Extract the offset perimeter polygon via `ExtractPerimeterPolygon(drawing, halfSpacing)` (already exists as a private static method in NestEngine). Returns null if invalid — return empty list.
3. **Rectangularity gate:** compute `polygon.Area() / polygon.BoundingBox.Area()`. If ratio > 0.95, return empty list — grid strategies already handle rectangular parts optimally. Note: `BoundingBox` is a property (set by `UpdateBounds()` which `ExtractPerimeterPolygon` calls before returning).
4. Compute candidate rotation angles via `ComputeCandidateRotations(item, polygon, workArea)` (already exists in NestEngine — computes hull edge angles, adds 0° and 90°, adds narrow-area sweep). Then filter the results by `NestItem.RotationStart` / `NestItem.RotationEnd` window (keep angles where `RotationStart <= angle <= RotationEnd`; if both are 0, treat as unconstrained). This filtering is applied locally after `ComputeCandidateRotations` returns — the shared method is not modified, so `AutoNest` behavior is unchanged.
5. Build an `NfpCache`:
   - For each candidate rotation, rotate the polygon via `RotatePolygon()` and register it via `nfpCache.RegisterPolygon(drawing.Id, rotation, rotatedPolygon)`
   - Call `nfpCache.PreComputeAll()` — since all entries share the same drawing ID, this computes NFPs between all rotation pairs of the single part shape
6. For each candidate rotation, run `BottomLeftFill.Fill()`:
   - Build a sequence of N copies of `(drawing.Id, rotation, drawing)`
   - N = `(int)(workArea.Area() / polygon.Area())`, capped to 500 max, and further capped to `item.Quantity` when Quantity > 0 (avoids wasting BLF cycles on parts that will be discarded)
   - Convert BLF result via `BottomLeftFill.ToNestParts()` to get `List<Part>`
   - Score via `FillScore.Compute(parts, workArea)`
7. Return the parts list from the highest-scoring rotation

### Integration points

**Both `FindBestFill` overloads** — insert after the Pairs phase, before remainder improvement:

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

**`Fill(List<Part> groupParts, ...)` overload** — this method runs its own RectBestFit and Pairs phases inline when `groupParts.Count == 1`, bypassing `FindBestFill`. Add the NFP phase here too, after Pairs and before remainder improvement, following the same pattern.

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
| `OpenNest.Engine/NestEngine.cs` | Add `FillNfpBestFit()` method; call from both `FindBestFill` overloads and the `Fill(List<Part>, ...)` single-drawing path, after Pairs phase |
| `OpenNest.Engine/NestProgress.cs` | Add `Nfp` to `NestPhase` enum |

## What Doesn't Change

- `FillBestFit`, `FillLinear`, `FillWithPairs` — untouched
- `AutoNest` — separate code path, untouched
- `BottomLeftFill`, `NfpCache`, `NoFitPolygon`, `InnerFitPolygon` — reused as-is, no modifications
- `ComputeCandidateRotations`, `ExtractPerimeterPolygon`, `RotatePolygon` — reused as-is
- UI callers (`ActionFillArea`, `ActionClone`, `PlateView.FillWithProgress`) — no changes
- MCP tools (`NestingTools`) — no changes

## Edge Cases

- **Part with no valid perimeter polygon:** `ExtractPerimeterPolygon` returns null → return empty list
- **Rectangularity ratio > 0.95:** skip NFP entirely, grid strategies are optimal
- **All rotations filtered out by constraints:** no BLF runs → return empty list
- **BLF places zero parts at a rotation:** skip that rotation, try others
- **Very small work area where part doesn't fit:** IFP computation returns invalid polygon → BLF places nothing → return empty list
- **Large plate with small part:** N capped to 500 to keep BLF O(N^2) cost manageable
- **item.Quantity is set:** N further capped to Quantity to avoid placing parts that will be discarded
