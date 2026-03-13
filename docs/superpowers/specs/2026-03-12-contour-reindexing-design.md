# Contour Re-Indexing Design

## Overview

Add entity-splitting primitives and a `Shape.ReindexAt` method so that a closed contour can be reordered to start (and end) at an arbitrary point. Then wire this into `ContourCuttingStrategy.Apply()` to replace the `NotImplementedException` stubs.

All geometry additions live on existing classes in `OpenNest.Geometry`. The strategy wiring is a change to the existing `ContourCuttingStrategy` in `OpenNest.CNC.CuttingStrategy`.

## Entity Splitting Primitives

### Line.SplitAt(Vector point)

```csharp
public (Line first, Line second)? SplitAt(Vector point)
```

- Returns two lines: `StartPoint → point` and `point → EndPoint`.
- If the point is at `StartPoint` (within `Tolerance.Epsilon`), `first` is null.
- If the point is at `EndPoint` (within `Tolerance.Epsilon`), `second` is null.
- If both halves would be degenerate, returns null.
- The point is assumed to lie on the line (caller is responsible — it comes from `ClosestPointTo`).

### Arc.SplitAt(Vector point)

```csharp
public (Arc first, Arc second)? SplitAt(Vector point)
```

- Computes `splitAngle = Center.AngleTo(point)`, normalized via `Angle.NormalizeRad`.
- First arc: same center, radius, direction — `StartAngle → splitAngle`.
- Second arc: same center, radius, direction — `splitAngle → EndAngle`.
- If `splitAngle` equals `StartAngle` (within tolerance), `first` is null.
- If `splitAngle` equals `EndAngle` (within tolerance), `second` is null.
- If both halves would be degenerate, returns null.

### Circle.ToArcFrom(Vector point)

```csharp
public Arc ToArcFrom(Vector point)
```

- Computes angle: `Center.AngleTo(point)`.
- Returns an `Arc` with same center, radius, `StartAngle = EndAngle = angle`, and `IsReversed` matching the circle's `Rotation` (CW → reversed, CCW → not reversed).
- This produces a full-sweep arc that starts and ends at `point`.

## Shape.ReindexAt

```csharp
public Shape ReindexAt(Vector point, Entity entity)
```

- `point`: the start/end point for the reindexed contour (from `ClosestPointTo`).
- `entity`: the entity containing `point` (from `ClosestPointTo`'s `out` parameter).
- Returns a **new** Shape (does not modify the original).

### Algorithm

1. If `entity` is a `Circle`:
   - Return a new Shape with a single entity: `circle.ToArcFrom(point)`.

2. Find the index `i` of `entity` in `Entities`.

3. Split the entity at `point`:
   - `Line` → `line.SplitAt(point)` → `(firstHalf, secondHalf)`
   - `Arc` → `arc.SplitAt(point)` → `(firstHalf, secondHalf)`

4. Build the new entity list (all non-null):
   - `secondHalf` (if not null)
   - `Entities[i+1]`, `Entities[i+2]`, ..., `Entities[count-1]` (after the split)
   - `Entities[0]`, `Entities[1]`, ..., `Entities[i-1]` (before the split, wrapping around)
   - `firstHalf` (if not null)

5. Return a new Shape with this entity list.

### Edge Cases

- **Point lands on entity boundary** (start/end of an entity): one half of the split is null. The reordering still works — it just starts from the next full entity.
- **Single-entity shape that is an Arc**: split produces two arcs, reorder is just `[secondHalf, firstHalf]`.
- **Single-entity Circle**: handled by step 1 (converts to full-sweep arc).

## Wiring into ContourCuttingStrategy

### Entity-to-ICode Conversion

Add a private method to `ContourCuttingStrategy`:

```csharp
private List<ICode> ConvertShapeToMoves(Shape shape)
```

Iterates `shape.Entities` and converts each to cutting moves:
- `Line` → `LinearMove(line.EndPoint)`
- `Arc` → `ArcMove(arc.EndPoint(), arc.Center, arc.IsReversed ? RotationType.CW : RotationType.CCW)`
- `Circle` → should not appear (circles are converted to arcs by `ReindexAt`)

No `RapidMove` between entities — they are contiguous in a reindexed shape. The lead-in already positions the head at the shape's start point.

### Replace NotImplementedException

In `ContourCuttingStrategy.Apply()`, replace both `throw new NotImplementedException(...)` blocks with:

```
var reindexed = shape.ReindexAt(closestPt, entity);
result.Codes.AddRange(ConvertShapeToMoves(reindexed));
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
| `OpenNest.Core/Geometry/Circle.cs` | Add `ToArcFrom(Vector)` method |
| `OpenNest.Core/Geometry/Shape.cs` | Add `ReindexAt(Vector, Entity)` method |
| `OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs` | Add `ConvertShapeToMoves`, replace `NotImplementedException` blocks |

## Out of Scope

- **MicrotabLeadOut trimming** (trim last move by gap size — stubbed with TODO)
- **Tab insertion** (inserting tab codes mid-contour — already stubbed)
- **Lead-in editor UI** (interactive start point selection — separate feature)
- **Contour re-indexing for open shapes** (only closed contours supported)
