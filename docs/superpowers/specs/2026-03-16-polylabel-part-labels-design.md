# Polylabel Part Label Positioning

**Date:** 2026-03-16
**Status:** Approved

## Problem

Part ID labels in `PlateView` are drawn at `PathPoints[0]` — the first point of the graphics path, which sits on the part contour edge. This causes labels to overlap adjacent parts and be unreadable, especially in dense nests.

## Solution

Implement the polylabel algorithm (pole of inaccessibility) to find the point inside each part's polygon with maximum distance from all edges. Draw the part ID label centered on that point.

## Design

### Part 1: Polylabel Algorithm

Add `PolyLabel` static class in `OpenNest.Geometry` namespace (file: `OpenNest.Core/Geometry/PolyLabel.cs`).

**Public API:**

```csharp
public static class PolyLabel
{
    public static Vector Find(Polygon polygon, double precision = 1.0);
}
```

**Algorithm:**

1. Compute bounding box of the polygon.
2. Divide into a grid of cells (cell size = shorter bbox dimension).
3. For each cell, compute signed distance from cell center to nearest polygon edge (negative if outside polygon).
4. Track the best interior point found so far.
5. Use a priority queue (sorted list) ordered by maximum possible distance for each cell.
6. Subdivide promising cells that could beat the current best; discard the rest.
7. Stop when the best cell's potential improvement over the current best is less than the precision tolerance.

**Dependencies within codebase:**

- `Polygon.Contains(Vector)` or ray-casting point-in-polygon test (already exists via `Intersect`).
- Point-to-segment distance calculation (already exists via `Line`/`Intersect`).

**No external dependencies.**

### Part 2: Label Rendering in LayoutPart

Modify `LayoutPart` in `OpenNest/LayoutPart.cs`.

**Changes:**

1. Add a cached `PointF? _labelPoint` field, invalidated when `IsDirty` is set.
2. In `Draw(Graphics g, string id)`:
   - If `_labelPoint` is null, compute it:
     - Convert the part's `Program` to geometry via `ConvertProgram.ToGeometry`.
     - Build shapes via `ShapeBuilder.GetShapes`.
     - Convert the outer shape to a `Polygon`.
     - Run `PolyLabel.Find()` on the polygon.
     - Offset by `BasePart.Location`.
     - Transform through the view matrix.
     - Cache the resulting `PointF`.
   - Draw the ID string centered on `_labelPoint` using `StringFormat` with `Alignment = Center` and `LineAlignment = Center`.
3. Invalidate `_labelPoint` when `IsDirty` is set (already triggers recompute on next draw).

**Coordinate pipeline:** polylabel runs in program-local coordinates (pre-transform), result is offset by `Location`, then transformed by the view matrix — same pipeline the existing `Path` uses.

## Scope

- **In scope:** polylabel algorithm, label positioning change in `LayoutPart.Draw`.
- **Out of scope:** changing part origins, modifying the nesting engine, any changes to `Part`, `Drawing`, or `Program` classes.

## Testing

- Unit tests for `PolyLabel.Find()` with known polygons: square, L-shape, C-shape, triangle.
- Verify the returned point is inside the polygon and has the expected distance from edges.
