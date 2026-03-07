# Rotating Calipers for Optimal Rotation

## Problem

The current `FindBestRotation` is brute-force: it samples rotations at fixed `stepAngle` intervals and returns the angle with the smallest axis-aligned bounding box area. This is O(n * s) where s = number of step samples, and it can miss the true optimum between samples.

## Solution

Replace it with the **rotating calipers algorithm**, which finds the exact minimum bounding rectangle of a convex polygon in O(n log n) time (dominated by hull computation). The algorithm exploits the fact that the minimum-area bounding rectangle of a convex polygon must have one side flush with a hull edge.

## New Files

### 1. `OpenNest.Core/Geometry/ConvexHull.cs`

- Static class in `namespace OpenNest.Geometry`
- `Compute(IList<Vector> points)` returns a `Polygon` (convex hull)
- Algorithm: Andrew's monotone chain -- O(n log n), numerically stable
- Returns vertices in counter-clockwise order (consistent with existing `Polygon` conventions)

### 2. `OpenNest.Core/Geometry/RotatingCalipers.cs`

- Static class in `namespace OpenNest.Geometry`
- Primary method: `MinimumBoundingRectangle(Polygon convexHull)` returns `BoundingRectangleResult`
- Overloads:
  - `MinimumBoundingRectangle(Polygon hull, double startAngle, double endAngle)` -- filters candidates to constraint range, evaluates range boundaries as fallback
  - `MinimumBoundingRectangle(IList<Vector> points)` -- convenience overload that computes hull internally
- Optimization target: minimum area by default; result contains both dimensions so callers can choose minimum width

### 3. `BoundingRectangleResult` (in `RotatingCalipers.cs`)

```csharp
public class BoundingRectangleResult
{
    public double Angle { get; }   // Optimal rotation in radians
    public double Width { get; }   // Rectangle width at optimal angle
    public double Height { get; }  // Rectangle height at optimal angle
    public double Area { get; }    // Width * Height
}
```

## Modified Files

### 4. `OpenNest.Core/Geometry/Entity.cs`

- Change `FindBestRotation` extension method signature:
  - Old: `double FindBestRotation(this List<Entity> entities, double stepAngle, ...)`
  - New: `BoundingRectangleResult FindBestRotation(this List<Entity> entities, double startAngle = 0, double endAngle = Angle.TwoPI)`
- `stepAngle` parameter removed (no longer needed -- exact solution)
- Implementation: extract vertices from entities, call `RotatingCalipers.MinimumBoundingRectangle()`

### 5. `OpenNest.Core/Geometry/Polygon.cs`

- Update `FindBestRotation` wrapper methods to match new signature and return `BoundingRectangleResult`

### 6. `OpenNest.Core/Geometry/Shape.cs`

- Update `FindBestRotation` wrapper methods to match new signature and return `BoundingRectangleResult`

### 7. All callers of `FindBestRotation`

- Update to use `BoundingRectangleResult` instead of `double`
- Access `.Angle` for the rotation value where only the angle was used before

## Algorithm Detail

### Rotating Calipers for Minimum Bounding Rectangle

1. Compute convex hull (Andrew's monotone chain)
2. For each edge of the hull, compute the angle it makes with the x-axis
3. Project all hull vertices onto the edge direction and its perpendicular
4. Compute the bounding rectangle from the min/max projections
5. Track the rectangle with minimum area (and separately, minimum width)
6. When constrained by `startAngle`/`endAngle`: filter candidate angles to range, also evaluate the range boundaries, return best valid result

### Rotation Constraint Handling

The algorithm naturally produces a set of candidate angles (one per hull edge). For constrained rotation:

1. Compute all candidate angles and their bounding rectangles
2. Filter to candidates within `[startAngle, endAngle]`
3. Also evaluate the bounding rectangle at `startAngle` and `endAngle` (range boundaries)
4. Return the best result from the filtered set

## What This Does NOT Change

- The nesting engine's packing algorithms (still uses axis-aligned rectangles for bin packing)
- The `NestItem`, `Part`, or `Program` rotation mechanics
- The `Box`/`BoundingBox` classes (remain axis-aligned)
