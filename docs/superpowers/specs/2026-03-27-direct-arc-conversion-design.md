# Direct Arc Conversion for Spline and Ellipse Import

## Problem

During DXF import, splines and ellipses are converted to many small line segments (200 for ellipses, control-point polygons for splines), then optionally reconstructed back to arcs via GeometrySimplifier in the CAD converter. This is wasteful and lossy:

- **Ellipses** are sampled into 200 line segments, discarding the known parametric form.
- **Splines** connect control points with lines, which is geometrically incorrect for B-splines (control points don't lie on the curve).
- Reconstructing arcs from approximate line segments is less accurate than fitting arcs to the exact curve.

## Solution

Convert splines and ellipses directly to circular arcs (and lines where necessary) during import, using the exact curve geometry. No user review step — the import produces the best representation automatically.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| When to convert | During import (automatic) | User didn't ask for 200 lines; produce best representation |
| Tolerance | 0.001" default | Tighter than simplifier's 0.004" because we have exact curves |
| Ellipse method | Analytical (curvature-based) | We have the exact parametric form |
| Spline method | Sample-then-fit | ACadSharp provides `PolygonalVertexes()` for accurate curve points |
| Fallback | Keep line segments where arcs can't fit | Handles rapid curvature changes in splines |
| Junction continuity | G1 (tangent) continuity | Normal-constrained arc centers prevent serrated edges |

## Architecture

Two new classes in `OpenNest.Core/Geometry/`:

### EllipseConverter

**Input:** Ellipse parameters — center, semi-major axis length, semi-minor axis length, rotation angle, start parameter, end parameter, tolerance.

**Output:** `List<Entity>` containing arcs that approximate the ellipse within tolerance.

**Algorithm — normal-constrained arc fitting:**

1. Compute an initial set of split parameters along the ellipse. Start with quadrant boundaries (points of maximum/minimum curvature) as natural split candidates.
2. For each pair of consecutive split points (t_start, t_end):
   a. Compute the ellipse normal at both endpoints analytically.
   b. Find the arc center at the intersection of the two normals. This guarantees the arc is tangent to the ellipse at both endpoints (G1 continuity).
   c. Compute the arc radius from the center to either endpoint.
   d. Sample several points on the ellipse between t_start and t_end, and measure the maximum radial deviation from the fitted arc.
   e. If deviation exceeds tolerance, subdivide: insert a split point at the midpoint and retry both halves.
   f. If deviation is within tolerance, emit the arc.
3. Continue until all segments are within tolerance.

**Ellipse analytical formulas (in local coordinates before rotation):**

- Point: `P(t) = (a cos t, b sin t)`
- Tangent: `T(t) = (-a sin t, b cos t)`
- Normal (inward): perpendicular to tangent, pointing toward center of curvature
- Curvature: `k(t) = ab / (a^2 sin^2 t + b^2 cos^2 t)^(3/2)`

After computing in local coordinates, rotate by the ellipse's major axis angle and translate to center.

**Arc count:** Depends on eccentricity and tolerance. A nearly-circular ellipse needs 1-2 arcs. A highly eccentric one (ratio < 0.3) may need 8-16. Tolerance drives this automatically via subdivision.

**Closed ellipse handling:** When the ellipse sweep is approximately 2pi, ensure the last arc's endpoint connects back to the first arc's start point. Tangent continuity wraps around.

### SplineConverter

**Input:** List of points evaluated on the spline curve (from ACadSharp's `PolygonalVertexes`), tolerance, and whether the spline is closed.

**Output:** `List<Entity>` containing arcs and lines that approximate the spline within tolerance.

**Algorithm — tangent-chained greedy arc fitting:**

1. Evaluate the spline at high density using `PolygonalVertexes(precision)` where precision comes from the existing `SplinePrecision` setting.
2. Walk the evaluated points from the start:
   a. At the current segment start, compute the tangent direction from the first two points (or from the chained tangent of the previous arc).
   b. Fit an arc constrained to be tangent at the start point:
      - The arc center lies on the normal to the tangent at the start point.
      - Use the perpendicular bisector of the chord from start to candidate end point, intersected with the start normal, to find the center.
   c. Extend the arc forward point-by-point. At each extension, recompute the center (intersection of start normal and chord bisector to the new endpoint) and check that all intermediate points are within tolerance of the arc.
   d. When adding the next point would exceed tolerance, finalize the arc with the last good endpoint.
   e. Compute the tangent at the arc's end point (perpendicular to the radius at that point) and chain it to the next segment.
3. If fewer than 3 points remain in a run where no arc fits (curvature changes too rapidly), emit line segments instead.
4. For closed splines, chain the final arc's tangent back to constrain the first arc.

**This is essentially the same approach GeometrySimplifier uses** (tangent chaining via `chainedTangent`), but operating on densely-sampled curve points rather than pre-existing line segments.

### Changes to Existing Code

#### Extensions.cs

```
// Before: returns List<Line>
public static List<Geometry.Line> ToOpenNest(this Spline spline)

// After: returns List<Entity>
public static List<Entity> ToOpenNest(this Spline spline, int precision)
```

- Extracts ACadSharp spline data, calls `SplineConverter.Convert()`
- Now accepts `precision` parameter (was ignored before)

```
// Before: returns List<Line>
public static List<Geometry.Line> ToOpenNest(this Ellipse ellipse, int precision = 200)

// After: returns List<Entity>
public static List<Entity> ToOpenNest(this Ellipse ellipse, double tolerance = 0.001)
```

- Extracts ACadSharp ellipse parameters, calls `EllipseConverter.Convert()`
- Precision parameter replaced by tolerance (precision is no longer relevant)

Both methods preserve Layer, Color, and LineTypeName on the output entities.

#### DxfImporter.cs

Currently collects `List<Line>` and `List<Arc>` separately. The new converters return `List<Entity>` (mixed arcs and lines). Options:

- Sort returned entities into the existing `lines` and `arcs` lists by type, OR
- Switch to a single `List<Entity>` collection

The simpler change is to sort into existing lists so downstream code (GeometryOptimizer, ShapeBuilder) is unaffected.

### What Stays the Same

- **GeometrySimplifier** — still exists for user-triggered simplification of genuinely line-based geometry (e.g., DXF files that actually contain line segments, or polylines)
- **GeometryOptimizer** — still merges collinear lines and coradial arcs post-import. May merge adjacent arcs produced by the new converters if they happen to be coradial.
- **ShapeBuilder, ConvertGeometry, CNC pipeline** — unchanged, they already handle mixed Line/Arc entities
- **SplinePrecision setting** — still used for spline point evaluation density

## Testing

- **EllipseConverter unit tests:**
  - Circle (ratio = 1.0) produces 1-2 arcs
  - Moderate ellipse produces arcs within tolerance
  - Highly eccentric ellipse produces more arcs, all within tolerance
  - Partial ellipse (elliptical arc) works correctly
  - Endpoint continuity: each arc's end matches the next arc's start
  - Tangent continuity: no discontinuities at junctions
  - Closed ellipse: last arc connects back to first

- **SplineConverter unit tests:**
  - Circular arc spline produces a single arc
  - S-curve spline produces arcs + lines where needed
  - Straight-line spline produces a line (not degenerate arcs)
  - Closed spline: endpoints connect
  - Tangent chaining: smooth transitions between consecutive arcs

- **Integration test:**
  - Import a DXF with splines and ellipses, verify the result contains arcs (not 200 lines)
  - Compare bounding boxes to ensure geometry is preserved
