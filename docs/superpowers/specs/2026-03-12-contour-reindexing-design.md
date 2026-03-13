# Contour Re-Indexing Design

## Overview

Add entity-splitting primitives and a `Shape.ReindexAt` method so that a closed contour can be reordered to start (and end) at an arbitrary point. Then wire this into `ContourCuttingStrategy.Apply()` to replace the `NotImplementedException` stubs.

All geometry additions live on existing classes in `OpenNest.Geometry`. The strategy wiring is a change to the existing `ContourCuttingStrategy` in `OpenNest.CNC.CuttingStrategy`.

## Entity Splitting Primitives

### Line.SplitAt(Vector point)

```csharp
public (Line first, Line second) SplitAt(Vector point)
```

- Returns two lines: `StartPoint → point` and `point → EndPoint`.
- If the point is at `StartPoint` (within `Tolerance.Epsilon` distance), `first` is null.
- If the point is at `EndPoint` (within `Tolerance.Epsilon` distance), `second` is null.
- The point is assumed to lie on the line (caller is responsible — it comes from `ClosestPointTo`).

### Arc.SplitAt(Vector point)

```csharp
public (Arc first, Arc second) SplitAt(Vector point)
```

- Computes `splitAngle = Center.AngleTo(point)`, normalized via `Angle.NormalizeRad`.
- First arc: same center, radius, direction — `StartAngle → splitAngle`.
- Second arc: same center, radius, direction — `splitAngle → EndAngle`.
- **Endpoint tolerance**: compare `point.DistanceTo(arc.StartPoint())` and `point.DistanceTo(arc.EndPoint())` rather than comparing angles directly. This avoids wrap-around issues at the 0/2π boundary.
- If the point is at `StartPoint()` (within `Tolerance.Epsilon` distance), `first` is null.
- If the point is at `EndPoint()` (within `Tolerance.Epsilon` distance), `second` is null.

### Circle — no conversion needed

Circles are kept as-is in `ReindexAt`. The `ConvertShapeToMoves` method handles circles directly by emitting an `ArcMove` from the start point back to itself (a full circle), matching the existing `ConvertGeometry.AddCircle` pattern. This avoids the problem of constructing a "full-sweep arc" where `StartAngle == EndAngle` would produce zero sweep.

## Shape.ReindexAt

```csharp
public Shape ReindexAt(Vector point, Entity entity)
```

- `point`: the start/end point for the reindexed contour (from `ClosestPointTo`).
- `entity`: the entity containing `point` (from `ClosestPointTo`'s `out` parameter).
- Returns a **new** Shape (does not modify the original). The new shape shares entity references with the original for unsplit entities — callers must not mutate either.
- Throws `ArgumentException` if `entity` is not found in `Entities`.

### Algorithm

1. If `entity` is a `Circle`:
   - Return a new Shape with that single `Circle` entity and `point` stored for `ConvertShapeToMoves` to use as the start point.

2. Find the index `i` of `entity` in `Entities`. Throw `ArgumentException` if not found.

3. Split the entity at `point`:
   - `Line` → `line.SplitAt(point)` → `(firstHalf, secondHalf)`
   - `Arc` → `arc.SplitAt(point)` → `(firstHalf, secondHalf)`

4. Build the new entity list (skip null entries):
   - `secondHalf` (if not null)
   - `Entities[i+1]`, `Entities[i+2]`, ..., `Entities[count-1]` (after the split)
   - `Entities[0]`, `Entities[1]`, ..., `Entities[i-1]` (before the split, wrapping around)
   - `firstHalf` (if not null)

5. Return a new Shape with this entity list.

### Edge Cases

- **Point lands on entity boundary** (start/end of an entity): one half of the split is null. The reordering still works — it just starts from the next full entity.
- **Single-entity shape that is an Arc**: split produces two arcs, reorder is just `[secondHalf, firstHalf]`.
- **Single-entity Circle**: handled by step 1 — kept as Circle, converted to a full-circle ArcMove in `ConvertShapeToMoves`.

## Wiring into ContourCuttingStrategy

### Entity-to-ICode Conversion

Add a private method to `ContourCuttingStrategy`:

```csharp
private List<ICode> ConvertShapeToMoves(Shape shape, Vector startPoint)
```

The `startPoint` parameter is needed for the Circle case (to know where the full-circle ArcMove starts).

Iterates `shape.Entities` and converts each to cutting moves using **absolute coordinates** (consistent with `ConvertGeometry`):
- `Line` → `LinearMove(line.EndPoint)`
- `Arc` → `ArcMove(arc.EndPoint(), arc.Center, arc.IsReversed ? RotationType.CW : RotationType.CCW)`
- `Circle` → `ArcMove(startPoint, circle.Center, circle.Rotation)` — full circle from start point back to itself, matching `ConvertGeometry.AddCircle`
- Any other entity type → throw `InvalidOperationException`

No `RapidMove` between entities — they are contiguous in a reindexed shape. The lead-in already positions the head at the shape's start point.

### Replace NotImplementedException

In `ContourCuttingStrategy.Apply()`, replace the two `throw new NotImplementedException(...)` blocks:

**Cutout loop** (uses `cutout` shape variable):
```csharp
var reindexed = cutout.ReindexAt(closestPt, entity);
result.Codes.AddRange(ConvertShapeToMoves(reindexed, closestPt));
```

**Perimeter block** (uses `profile.Perimeter`):
```csharp
var reindexed = profile.Perimeter.ReindexAt(perimeterPt, perimeterEntity);
result.Codes.AddRange(ConvertShapeToMoves(reindexed, perimeterPt));
```

The full sequence for each contour becomes:
1. Lead-in codes (rapid to pierce point, cutting moves to contour start)
2. Contour body (reindexed entity moves from `ConvertShapeToMoves`)
3. Lead-out codes (overcut moves away from contour)

### MicrotabLeadOut Handling

When the lead-out is `MicrotabLeadOut`, the last cutting move must be trimmed by `GapSize`. This is a separate concern from re-indexing — stub it with a TODO comment for now. The trimming logic will shorten the last `LinearMove` or `ArcMove` in the contour body.

## Files Modified

| File | Change |
|------|--------|
| `OpenNest.Core/Geometry/Line.cs` | Add `SplitAt(Vector)` method |
| `OpenNest.Core/Geometry/Arc.cs` | Add `SplitAt(Vector)` method |
| `OpenNest.Core/Geometry/Shape.cs` | Add `ReindexAt(Vector, Entity)` method |
| `OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs` | Add `ConvertShapeToMoves`, replace `NotImplementedException` blocks |

## Out of Scope

- **MicrotabLeadOut trimming** (trim last move by gap size — stubbed with TODO)
- **Tab insertion** (inserting tab codes mid-contour — already stubbed)
- **Lead-in editor UI** (interactive start point selection — separate feature)
- **Contour re-indexing for open shapes** (only closed contours supported)
