# Polylabel Part Label Positioning

**Date:** 2026-03-16
**Status:** Approved

## Problem

Part ID labels in `PlateView` are drawn at `PathPoints[0]` — the first point of the graphics path, which sits on the part contour edge. This causes labels to overlap adjacent parts and be unreadable, especially in dense nests.

## Solution

Implement the polylabel algorithm (pole of inaccessibility) to find the point inside each part's polygon with maximum distance from all edges, including hole edges. Draw the part ID label centered on that point.

## Design

### Part 1: Polylabel Algorithm

Add `PolyLabel` static class in `OpenNest.Geometry` namespace (file: `OpenNest.Core/Geometry/PolyLabel.cs`).

**Public API:**

```csharp
public static class PolyLabel
{
    public static Vector Find(Polygon outer, IList<Polygon> holes = null, double precision = 0.5);
}
```

**Algorithm:**

1. Compute bounding box of the outer polygon.
2. Divide into a grid of cells (cell size = shorter bbox dimension).
3. For each cell, compute signed distance from cell center to nearest edge on any ring (outer boundary + all holes). Use `Polygon.ContainsPoint` for sign (negative if outside outer polygon or inside a hole).
4. Track the best interior point found so far.
5. Use a priority queue (sorted list) ordered by maximum possible distance for each cell.
6. Subdivide promising cells that could beat the current best; discard the rest.
7. Stop when the best cell's potential improvement over the current best is less than the precision tolerance.

**Dependencies within codebase:**

- `Polygon.ContainsPoint(Vector)` — ray-casting point-in-polygon test (already exists).
- Point-to-segment distance — compute from `Line` or inline (distance from point to each polygon edge).

**Fallback:** If the polygon is degenerate (< 3 vertices) or the program has no geometry, fall back to the bounding box center.

**No external dependencies.**

### Part 2: Label Rendering in LayoutPart

Modify `LayoutPart` in `OpenNest/LayoutPart.cs`.

**Changes:**

1. Add a cached `Vector? _labelPoint` field in **program-local coordinates** (pre-transform). Invalidated when `IsDirty` is set.
2. When computing the label point (on first draw after invalidation):
   - Convert the part's `Program` to geometry via `ConvertProgram.ToGeometry`.
   - Build shapes via `ShapeBuilder.GetShapes`.
   - Identify the outer contour using `ShapeProfile` (the `Perimeter` shape) and convert cutouts to hole polygons.
   - Run `PolyLabel.Find(outer, holes)` on the result.
   - Cache the `Vector` in program-local coordinates.
3. In `Draw(Graphics g, string id)`:
   - Offset the cached label point by `BasePart.Location`.
   - Transform through the current view matrix (handles zoom/pan without cache invalidation).
   - Draw the ID string centered using `StringFormat` with `Alignment = Center` and `LineAlignment = Center`.

**Coordinate pipeline:** polylabel runs once in program-local coordinates (expensive, cached). Location offset + matrix transform happen every frame (cheap, no caching needed). This matches how the existing `GraphicsPath` pipeline works and avoids stale cache on zoom/pan.

## Scope

- **In scope:** polylabel algorithm, label positioning change in `LayoutPart.Draw`.
- **Out of scope:** changing part origins, modifying the nesting engine, any changes to `Part`, `Drawing`, or `Program` classes.

## Testing

- Unit tests for `PolyLabel.Find()` with known polygons:
  - Square — label at center.
  - L-shape — label in the larger lobe.
  - C-shape — label inside the concavity, not at bounding box center.
  - Triangle — label at incenter.
  - Thin rectangle (10:1 aspect ratio) — label centered along the short axis.
  - Square with large centered hole — label avoids the hole.
- Verify the returned point is inside the polygon and has the expected distance from edges.
