# Geometry-Based Push — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Replace bounding-box push with polygon-based directional distance so parts nestle together based on actual cut geometry.

**Architecture:** Convert CNC programs to polygon line segments, offset the moving part's polygon by `PartSpacing`, then compute the exact minimum translation distance along the push axis before any edge contact. Plate edge checks remain bounding-box based.

**Tech Stack:** .NET Framework 4.8, OpenNest.Core geometry primitives

---

## Context

- `PlateView.PushSelected` (in `OpenNest\Controls\PlateView.cs:753-839`) currently uses `Helper.ClosestDistance*` methods that operate on `Box` objects
- `PushDirection` enum lives in `OpenNest\PushDirection.cs` (UI project) — must move to Core so `Helper` can reference it
- Parts convert to geometry via: `ConvertProgram.ToGeometry()` → `Helper.GetShapes()` → `Shape.ToPolygon()` → `Polygon.ToLines()`
- `Shape.OffsetEntity(distance, OffsetSide.Left)` offsets a shape outward (already implemented in `OpenNest.Core\Geometry\Shape.cs:355-423`)

---

### Task 1: Move PushDirection enum to OpenNest.Core

**Files:**
- Move: `OpenNest\PushDirection.cs` → `OpenNest.Core\PushDirection.cs`

`PushDirection` is currently in the UI project. `Helper.cs` (Core) needs to reference it. Since the enum has no UI dependencies, move it to Core. The UI project already references Core, so all existing usages continue to compile.

**Step 1: Create PushDirection.cs in OpenNest.Core**

```csharp
namespace OpenNest
{
    public enum PushDirection
    {
        Up,
        Down,
        Left,
        Right
    }
}
```

**Step 2: Delete `OpenNest\PushDirection.cs`**

**Step 3: Add the new file to `OpenNest.Core.csproj`**

Add `<Compile Include="PushDirection.cs" />` and remove from `OpenNest.csproj`.

**Step 4: Build to verify**

Run: `msbuild OpenNest.sln /p:Configuration=Release`
Expected: Build succeeds — namespace is already `OpenNest`, no code changes needed in consumers.

**Step 5: Commit**

```
feat: move PushDirection enum to OpenNest.Core
```

---

### Task 2: Add Helper.GetPartLines — convert a Part to positioned line segments

**Files:**
- Modify: `OpenNest.Core\Helper.cs`

Add a helper that encapsulates the conversion pipeline: Program → geometry → shapes → polygons → lines, positioned at the part's world location. This is called once per part during push.

**Step 1: Add GetPartLines to Helper.cs** (after the existing `GetShapes` methods, around line 337)

```csharp
public static List<Line> GetPartLines(Part part)
{
    var entities = Converters.ConvertProgram.ToGeometry(part.Program);
    var shapes = GetShapes(entities.Where(e => e.Layer != Geometry.SpecialLayers.Rapid));
    var lines = new List<Line>();

    foreach (var shape in shapes)
    {
        var polygon = shape.ToPolygon();
        polygon.Offset(part.Location);
        lines.AddRange(polygon.ToLines());
    }

    return lines;
}
```

**Step 2: Add GetOffsetPartLines to Helper.cs** (immediately after)

Same pipeline but offsets shapes before converting to polygon:

```csharp
public static List<Line> GetOffsetPartLines(Part part, double spacing)
{
    var entities = Converters.ConvertProgram.ToGeometry(part.Program);
    var shapes = GetShapes(entities.Where(e => e.Layer != Geometry.SpecialLayers.Rapid));
    var lines = new List<Line>();

    foreach (var shape in shapes)
    {
        var offsetEntity = shape.OffsetEntity(spacing, OffsetSide.Left) as Shape;

        if (offsetEntity == null)
            continue;

        var polygon = offsetEntity.ToPolygon();
        polygon.Offset(part.Location);
        lines.AddRange(polygon.ToLines());
    }

    return lines;
}
```

**Step 3: Build to verify**

Run: `msbuild OpenNest.sln /p:Configuration=Release`
Expected: Build succeeds.

**Step 4: Commit**

```
feat: add Helper.GetPartLines and GetOffsetPartLines
```

---

### Task 3: Add Helper.DirectionalDistance — core algorithm

**Files:**
- Modify: `OpenNest.Core\Helper.cs`

This is the main algorithm. For two sets of line segments and a push direction, compute the minimum translation distance before any contact.

**Step 1: Add RayEdgeDistance helper**

For a vertex moving along an axis, find where it hits a line segment:

```csharp
/// <summary>
/// Finds the distance from a vertex to a line segment along a push axis.
/// Returns double.MaxValue if the ray does not hit the segment.
/// </summary>
private static double RayEdgeDistance(Vector vertex, Line edge, PushDirection direction)
{
    var p1 = edge.StartPoint;
    var p2 = edge.EndPoint;

    switch (direction)
    {
        case PushDirection.Left:
        {
            // Ray goes in -X direction. Edge must have a horizontal span.
            if (p1.Y.IsEqualTo(p2.Y))
                return double.MaxValue; // horizontal edge, parallel to ray

            // Find t where ray Y == edge Y at parametric t
            var t = (vertex.Y - p1.Y) / (p2.Y - p1.Y);
            if (t < -Tolerance.Epsilon || t > 1.0 + Tolerance.Epsilon)
                return double.MaxValue;

            var ix = p1.X + t * (p2.X - p1.X);
            var dist = vertex.X - ix; // positive if edge is to the left
            return dist > Tolerance.Epsilon ? dist : double.MaxValue;
        }

        case PushDirection.Right:
        {
            if (p1.Y.IsEqualTo(p2.Y))
                return double.MaxValue;

            var t = (vertex.Y - p1.Y) / (p2.Y - p1.Y);
            if (t < -Tolerance.Epsilon || t > 1.0 + Tolerance.Epsilon)
                return double.MaxValue;

            var ix = p1.X + t * (p2.X - p1.X);
            var dist = ix - vertex.X;
            return dist > Tolerance.Epsilon ? dist : double.MaxValue;
        }

        case PushDirection.Down:
        {
            if (p1.X.IsEqualTo(p2.X))
                return double.MaxValue;

            var t = (vertex.X - p1.X) / (p2.X - p1.X);
            if (t < -Tolerance.Epsilon || t > 1.0 + Tolerance.Epsilon)
                return double.MaxValue;

            var iy = p1.Y + t * (p2.Y - p1.Y);
            var dist = vertex.Y - iy;
            return dist > Tolerance.Epsilon ? dist : double.MaxValue;
        }

        case PushDirection.Up:
        {
            if (p1.X.IsEqualTo(p2.X))
                return double.MaxValue;

            var t = (vertex.X - p1.X) / (p2.X - p1.X);
            if (t < -Tolerance.Epsilon || t > 1.0 + Tolerance.Epsilon)
                return double.MaxValue;

            var iy = p1.Y + t * (p2.Y - p1.Y);
            var dist = iy - vertex.Y;
            return dist > Tolerance.Epsilon ? dist : double.MaxValue;
        }

        default:
            return double.MaxValue;
    }
}
```

**Step 2: Add DirectionalDistance method**

```csharp
/// <summary>
/// Computes the minimum translation distance along a push direction before
/// any edge of movingLines contacts any edge of stationaryLines.
/// Returns double.MaxValue if no collision path exists.
/// </summary>
public static double DirectionalDistance(List<Line> movingLines, List<Line> stationaryLines, PushDirection direction)
{
    var minDist = double.MaxValue;

    // Case 1: Each moving vertex → each stationary edge
    for (int i = 0; i < movingLines.Count; i++)
    {
        var movingLine = movingLines[i];

        for (int j = 0; j < stationaryLines.Count; j++)
        {
            var d = RayEdgeDistance(movingLine.StartPoint, stationaryLines[j], direction);
            if (d < minDist) minDist = d;
        }
    }

    // Case 2: Each stationary vertex → each moving edge (opposite direction)
    var opposite = OppositeDirection(direction);

    for (int i = 0; i < stationaryLines.Count; i++)
    {
        var stationaryLine = stationaryLines[i];

        for (int j = 0; j < movingLines.Count; j++)
        {
            var d = RayEdgeDistance(stationaryLine.StartPoint, movingLines[j], opposite);
            if (d < minDist) minDist = d;
        }
    }

    return minDist;
}

private static PushDirection OppositeDirection(PushDirection direction)
{
    switch (direction)
    {
        case PushDirection.Left: return PushDirection.Right;
        case PushDirection.Right: return PushDirection.Left;
        case PushDirection.Up: return PushDirection.Down;
        case PushDirection.Down: return PushDirection.Up;
        default: return direction;
    }
}
```

**Step 3: Build to verify**

Run: `msbuild OpenNest.sln /p:Configuration=Release`
Expected: Build succeeds.

**Step 4: Commit**

```
feat: add Helper.DirectionalDistance for polygon-based push
```

---

### Task 4: Rewrite PlateView.PushSelected to use geometry-based distance

**Files:**
- Modify: `OpenNest\Controls\PlateView.cs:753-839`

**Step 1: Add `using OpenNest.Converters;`** at the top if not already present.

**Step 2: Replace the PushSelected method body**

Replace the entire `PushSelected` method (lines 753-839) with:

```csharp
public void PushSelected(PushDirection direction)
{
    // Build line segments for all stationary parts.
    var stationaryParts = parts.Where(p => !p.IsSelected && !SelectedParts.Contains(p)).ToList();
    var stationaryLines = new List<List<Line>>(stationaryParts.Count);
    var stationaryBoxes = new List<Box>(stationaryParts.Count);

    foreach (var part in stationaryParts)
    {
        stationaryLines.Add(Helper.GetPartLines(part.BasePart));
        stationaryBoxes.Add(part.BoundingBox);
    }

    var workArea = Plate.WorkArea();
    var distance = double.MaxValue;

    foreach (var selected in SelectedParts)
    {
        // Get offset lines for the moving part.
        var movingLines = Plate.PartSpacing > 0
            ? Helper.GetOffsetPartLines(selected.BasePart, Plate.PartSpacing)
            : Helper.GetPartLines(selected.BasePart);

        var movingBox = selected.BoundingBox;

        // Check geometry distance against each stationary part.
        for (int i = 0; i < stationaryLines.Count; i++)
        {
            // Early-out: skip if bounding boxes don't overlap on the perpendicular axis.
            var stBox = stationaryBoxes[i];
            bool perpOverlap;

            switch (direction)
            {
                case PushDirection.Left:
                case PushDirection.Right:
                    perpOverlap = !(movingBox.Bottom >= stBox.Top || movingBox.Top <= stBox.Bottom);
                    break;
                default: // Up, Down
                    perpOverlap = !(movingBox.Left >= stBox.Right || movingBox.Right <= stBox.Left);
                    break;
            }

            if (!perpOverlap)
                continue;

            var d = Helper.DirectionalDistance(movingLines, stationaryLines[i], direction);
            if (d < distance)
                distance = d;
        }

        // Check distance to plate edge (actual geometry bbox, not offset).
        double edgeDist;
        switch (direction)
        {
            case PushDirection.Left:
                edgeDist = selected.Left - workArea.Left;
                break;
            case PushDirection.Right:
                edgeDist = workArea.Right - selected.Right;
                break;
            case PushDirection.Up:
                edgeDist = workArea.Top - selected.Top;
                break;
            default: // Down
                edgeDist = selected.Bottom - workArea.Bottom;
                break;
        }

        if (edgeDist > 0 && edgeDist < distance)
            distance = edgeDist;
    }

    if (distance < double.MaxValue && distance > 0)
    {
        var offset = new Vector();

        switch (direction)
        {
            case PushDirection.Left:  offset.X = -distance; break;
            case PushDirection.Right: offset.X =  distance; break;
            case PushDirection.Up:    offset.Y =  distance; break;
            case PushDirection.Down:  offset.Y = -distance; break;
        }

        SelectedParts.ForEach(p => p.Offset(offset));
        Invalidate();
    }
}
```

**Step 3: Build to verify**

Run: `msbuild OpenNest.sln /p:Configuration=Release`
Expected: Build succeeds.

**Step 4: Manual test**

1. Open a nest with at least two irregular parts on a plate
2. Select one part, press X to push left — it should slide until its offset geometry touches the other part's cut geometry
3. Press Shift+X to push right
4. Press Y to push down, Shift+Y to push up
5. Verify parts nestle closer than before (no bounding box gap)
6. Verify parts stop at plate edges correctly

**Step 5: Commit**

```
feat: rewrite PushSelected to use polygon directional-distance

Parts now push based on actual cut geometry instead of bounding boxes,
allowing irregular shapes to nestle together with PartSpacing gap.
```

---

### Task 5: Clean up — remove PushDirection from OpenNest.csproj if not already done

**Files:**
- Modify: `OpenNest\OpenNest.csproj` — remove `<Compile Include="PushDirection.cs" />`
- Delete: `OpenNest\PushDirection.cs` (if not already deleted in Task 1)

**Step 1: Verify build is clean**

Run: `msbuild OpenNest.sln /p:Configuration=Release`
Expected: Build succeeds with zero warnings related to PushDirection.

**Step 2: Commit**

```
chore: clean up PushDirection move
```
