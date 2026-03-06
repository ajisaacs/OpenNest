# Geometry-Based Push

## Problem

`PlateView.PushSelected` uses bounding boxes to determine how far to slide parts. For irregular shapes, this leaves large gaps between actual cut geometry. Parts should nestle together based on their true shape.

## Behavior

When pushing a selected part in a cardinal direction:

1. The moving part's geometry is offset outward by `Plate.PartSpacing` (the kerf buffer)
2. That offset geometry slides until it touches the **actual cut geometry** of stationary parts (zero gap between offset and cut)
3. The actual cut paths end up separated by exactly `PartSpacing`
4. For plate edges: the **actual geometry** bounding box stops at the work area boundary (no offset applied to edges — same as current behavior)

## Algorithm: Analytical Polygon Directional-Distance

### Conversion Pipeline

```
Part.Program → ConvertProgram.ToGeometry() → Helper.GetShapes() → Shape.ToPolygon() → Polygon.ToLines()
```

For the moving part, insert an offset step after getting shapes:

```
shapes → Shape.OffsetEntity(PartSpacing, OffsetSide.Left) → ToPolygon() → ToLines()
```

### Directional Distance

For a given push direction, compute the minimum translation distance before any edge of the offset moving polygon contacts any edge of a stationary polygon.

Three cases per polygon pair:

1. **Moving vertex → stationary edge**: For each vertex of the offset polygon, cast a ray in the push direction. Find where it crosses a stationary edge. Record the distance.
2. **Stationary vertex → moving edge**: For each vertex of the stationary polygon, cast a ray in the opposite direction. Find where it crosses an offset-moving edge. Record the distance.
3. **Edge-edge sliding contact**: For non-parallel edge pairs, compute the translation distance along the push axis where they first intersect.

The minimum positive distance across all cases and all polygon pairs is the push distance.

### Early-Out

Before polygon math, check bounding box overlap on the perpendicular axis. If a stationary part's bounding box doesn't overlap the moving part's bounding box on the axis perpendicular to the push direction, skip that pair.

## Changes

| File | Change |
|------|--------|
| `OpenNest.Core\Helper.cs` | Add `DirectionalDistance(List<Line>, List<Line>, PushDirection)` |
| `OpenNest.Core\Helper.cs` | Add `RayEdgeDistance(Vector, Line, PushDirection)` helper |
| `OpenNest\Controls\PlateView.cs` | Rewrite `PushSelected` to use polygon directional-distance |

### What stays the same

- Plate edge distance check (actual bounding box vs work area)
- Key bindings (X/Y/Shift combinations)
- Existing `ClosestDistance*` methods remain (may be used elsewhere)

### What changes

- `PushSelected` converts parts to polygons instead of using bounding boxes
- Distance calculation uses actual geometry edges instead of box edges
