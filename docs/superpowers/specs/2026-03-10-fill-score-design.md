# Fill Score Design

## Problem

The nesting engine compares fill results by raw part count, which causes it to reject denser pair patterns that would yield more parts after remainder filling.

**Concrete case:** Part 4980 A24 PT02 on a 60×120" plate:
- Wider pair (90°/270°): 5 rows × 9 = **45 parts** at grid stage, no room for remainder
- Tighter pair #1 (89.7°/269.7°): 4 rows × 9 = **36 parts** at grid stage, 15.7" remainder strip → **47 total possible**

The algorithm compares 45 vs 36 at the grid stage. Pattern #1 loses before its remainder strip is fully evaluated. Two contributing issues:

1. **Scoring:** `FillWithPairs` uses raw count (`count > best.Count`) with no tiebreaker for density or compactness
2. **Remainder rotation coverage:** `FillLinear.FillRemainingStrip` only tries rotations present in the seed pattern (89.7°/269.7°), missing the 0° rotation that fits 11 parts in the strip vs ~8 at the seed angles

## Design

### 1. FillScore Struct

A value type encapsulating fill quality with lexicographic comparison:

```
Priority 1: Part count (higher wins)
Priority 2: Usable remnant area (higher wins) — largest remnant with short side ≥ MinRemnantDimension
Priority 3: Density (higher wins) — sum of part areas / bounding box area of placed parts
```

**Location:** `OpenNest.Engine/FillScore.cs`

**Fields:**
- `int Count` — number of parts placed
- `double UsableRemnantArea` — area of the largest remnant whose short side ≥ threshold (0 if none)
- `double Density` — total part area / bounding box area of all placed parts

**Constants:**
- `MinRemnantDimension = 12.0` (inches) — minimum short side for a remnant to be considered usable

**Implements** `IComparable<FillScore>` with lexicographic ordering.

**Static factory:** `FillScore.Compute(List<Part> parts, Box workArea)` — computes all three metrics from a fill result. Remnant calculation uses the same edge-strip approach as `Plate.GetRemnants()` but against the work area box and placed part bounding boxes.

### 2. Replace Comparison Points

All six comparison locations switch from raw count to `FillScore`:

| Location | File | Current Logic |
|----------|------|---------------|
| `IsBetterFill` | NestEngine.cs:299 | Count, then bbox area tiebreaker |
| `FillWithPairs` inner loop | NestEngine.cs:226 | Count only |
| `TryStripRefill` | NestEngine.cs:424 | `stripParts.Count > lastCluster.Count` (keep as-is — threshold check, not quality comparison) |
| `FillRemainingStrip` | FillLinear.cs:436 | `h.Count > best.Count` (keep as-is — internal sub-fill, count is correct) |
| `FillPattern` | NestEngine.cs:492 | `IsBetterValidFill` (overlap + count) |
| `FindBestFill` | NestEngine.cs:95-118 | `IsBetterFill` (already covered) |

`IsBetterFill(candidate, current)` becomes a `FillScore` comparison. `IsBetterValidFill` keeps its overlap check, then delegates to score comparison.

`FillLinear.FillRemainingStrip` needs access to the work area (already available via `WorkArea` property) to compute scores.

### 3. Expanded Remainder Rotations

`FillLinear.FillRemainingStrip` currently only tries rotations from the seed pattern. Add 0° and 90° (cardinal orientations) to the rotation set:

```csharp
// Current: only seed rotations
foreach (var seedPart in seedPattern.Parts) { ... }

// New: also try 0° and 90°
var rotations = new List<double> { 0, Angle.HalfPI };
foreach (var seedPart in seedPattern.Parts)
    if (!rotations.Any(r => r.IsEqualTo(seedPart.Rotation)))
        rotations.Add(seedPart.Rotation);
```

This is the change that actually fixes the 45→47 case by allowing `FillRemainingStrip` to discover the 0° rotation for the remainder strip.

## What This Does NOT Change

- Part count remains the primary criterion — no trading parts for remnants
- `FillWithPairs` still evaluates top 50 candidates (no extra `TryRemainderImprovement` per candidate)
- `BestFitResult` ranking/filtering is unchanged
- No new UI or configuration (MinRemnantDimension is a constant for now)

## Expected Outcome

For the 4980 A24 PT02 case:
1. Pattern #1 grid produces 36 parts
2. Expanded remainder rotations try 0° in the 15.7" strip → 11 parts → **47 total**
3. Wider pair grid produces 45 parts with ~5" remainder → ~0 extra parts → **45 total**
4. FillScore comparison: 47 > 45 → pattern #1 wins
