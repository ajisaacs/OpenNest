# Contour Re-Indexing Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add entity-splitting primitives (`Line.SplitAt`, `Arc.SplitAt`), a `Shape.ReindexAt` method, and wire them into `ContourCuttingStrategy.Apply()` to replace the `NotImplementedException` stubs.

**Architecture:** Bottom-up — build splitting primitives first, then the reindexing algorithm on top, then wire into the strategy. Each layer depends only on the one below it.

**Tech Stack:** C# / .NET 8, OpenNest.Core (Geometry + CNC namespaces)

**Spec:** `docs/superpowers/specs/2026-03-12-contour-reindexing-design.md`

---

## File Structure

| File | Change | Responsibility |
|------|--------|----------------|
| `OpenNest.Core/Geometry/Line.cs` | Add method | `SplitAt(Vector)` — split a line at a point into two halves |
| `OpenNest.Core/Geometry/Arc.cs` | Add method | `SplitAt(Vector)` — split an arc at a point into two halves |
| `OpenNest.Core/Geometry/Shape.cs` | Add method | `ReindexAt(Vector, Entity)` — reorder a closed contour to start at a given point |
| `OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs` | Add method + modify | `ConvertShapeToMoves` + replace two `NotImplementedException` blocks |

---

## Chunk 1: Splitting Primitives

### Task 1: Add `Line.SplitAt(Vector)`

**Files:**
- Modify: `OpenNest.Core/Geometry/Line.cs`

- [ ] **Step 1: Add `SplitAt` method to `Line`**

Add the following method to the `Line` class (after the existing `ClosestPointTo` method):

```csharp
public (Line first, Line second) SplitAt(Vector point)
{
    var first = point.DistanceTo(StartPoint) < Tolerance.Epsilon
        ? null
        : new Line(StartPoint, point);

    var second = point.DistanceTo(EndPoint) < Tolerance.Epsilon
        ? null
        : new Line(point, EndPoint);

    return (first, second);
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Core/OpenNest.Core.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/Geometry/Line.cs
git commit -m "feat: add Line.SplitAt(Vector) splitting primitive"
```

### Task 2: Add `Arc.SplitAt(Vector)`

**Files:**
- Modify: `OpenNest.Core/Geometry/Arc.cs`

- [ ] **Step 1: Add `SplitAt` method to `Arc`**

Add the following method to the `Arc` class (after the existing `EndPoint` method):

```csharp
public (Arc first, Arc second) SplitAt(Vector point)
{
    if (point.DistanceTo(StartPoint()) < Tolerance.Epsilon)
        return (null, new Arc(Center, Radius, StartAngle, EndAngle, IsReversed));

    if (point.DistanceTo(EndPoint()) < Tolerance.Epsilon)
        return (new Arc(Center, Radius, StartAngle, EndAngle, IsReversed), null);

    var splitAngle = Angle.NormalizeRad(Center.AngleTo(point));

    var firstArc = new Arc(Center, Radius, StartAngle, splitAngle, IsReversed);
    var secondArc = new Arc(Center, Radius, splitAngle, EndAngle, IsReversed);

    return (firstArc, secondArc);
}
```

Key details from spec:
- Compare distances to `StartPoint()`/`EndPoint()` rather than comparing angles (avoids 0/2π wrap-around issues).
- `splitAngle` is computed from `Center.AngleTo(point)`, normalized.
- Both halves preserve center, radius, and `IsReversed` direction.

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Core/OpenNest.Core.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/Geometry/Arc.cs
git commit -m "feat: add Arc.SplitAt(Vector) splitting primitive"
```

---

## Chunk 2: Shape.ReindexAt

### Task 3: Add `Shape.ReindexAt(Vector, Entity)`

**Files:**
- Modify: `OpenNest.Core/Geometry/Shape.cs`

- [ ] **Step 1: Add `ReindexAt` method to `Shape`**

Add the following method to the `Shape` class (after the existing `ClosestPointTo(Vector, out Entity)` method around line 201):

```csharp
public Shape ReindexAt(Vector point, Entity entity)
{
    // Circle case: return a new shape with just the circle
    if (entity is Circle)
    {
        var result = new Shape();
        result.Entities.Add(entity);
        return result;
    }

    var i = Entities.IndexOf(entity);
    if (i < 0)
        throw new ArgumentException("Entity not found in shape", nameof(entity));

    // Split the entity at the point
    Entity firstHalf = null;
    Entity secondHalf = null;

    if (entity is Line line)
    {
        var (f, s) = line.SplitAt(point);
        firstHalf = f;
        secondHalf = s;
    }
    else if (entity is Arc arc)
    {
        var (f, s) = arc.SplitAt(point);
        firstHalf = f;
        secondHalf = s;
    }

    // Build reindexed entity list
    var entities = new List<Entity>();

    // secondHalf of split entity (if not null)
    if (secondHalf != null)
        entities.Add(secondHalf);

    // Entities after the split index (wrapping)
    for (var j = i + 1; j < Entities.Count; j++)
        entities.Add(Entities[j]);

    // Entities before the split index (wrapping)
    for (var j = 0; j < i; j++)
        entities.Add(Entities[j]);

    // firstHalf of split entity (if not null)
    if (firstHalf != null)
        entities.Add(firstHalf);

    var reindexed = new Shape();
    reindexed.Entities.AddRange(entities);
    return reindexed;
}
```

The `Shape` class already imports `System` and `System.Collections.Generic`, so no new usings needed.

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Core/OpenNest.Core.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/Geometry/Shape.cs
git commit -m "feat: add Shape.ReindexAt(Vector, Entity) for contour reordering"
```

---

## Chunk 3: Wire into ContourCuttingStrategy

### Task 4: Add `ConvertShapeToMoves` and replace stubs

**Files:**
- Modify: `OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs`

- [ ] **Step 1: Add `ConvertShapeToMoves` private method**

Add the following private method to `ContourCuttingStrategy` (after the existing `SelectLeadOut` method, before the closing brace of the class):

```csharp
private List<ICode> ConvertShapeToMoves(Shape shape, Vector startPoint)
{
    var moves = new List<ICode>();

    foreach (var entity in shape.Entities)
    {
        if (entity is Line line)
        {
            moves.Add(new LinearMove(line.EndPoint));
        }
        else if (entity is Arc arc)
        {
            moves.Add(new ArcMove(arc.EndPoint(), arc.Center, arc.IsReversed ? RotationType.CW : RotationType.CCW));
        }
        else if (entity is Circle circle)
        {
            moves.Add(new ArcMove(startPoint, circle.Center, circle.Rotation));
        }
        else
        {
            throw new System.InvalidOperationException($"Unsupported entity type: {entity.Type}");
        }
    }

    return moves;
}
```

This matches the `ConvertGeometry.AddArc`/`AddCircle`/`AddLine` patterns but without `RapidMove` between entities (they are contiguous in a reindexed shape).

- [ ] **Step 2: Replace cutout `NotImplementedException` (line 41)**

In the `Apply` method, replace:
```csharp
                // Contour re-indexing: split shape entities at closestPt so cutting
                // starts there, convert to ICode, and add to result.Codes
                throw new System.NotImplementedException("Contour re-indexing not yet implemented");
```

With:
```csharp
                var reindexed = cutout.ReindexAt(closestPt, entity);
                result.Codes.AddRange(ConvertShapeToMoves(reindexed, closestPt));
                // TODO: MicrotabLeadOut — trim last cutting move by GapSize
```

- [ ] **Step 3: Replace perimeter `NotImplementedException` (line 57)**

In the `Apply` method, replace:
```csharp
                throw new System.NotImplementedException("Contour re-indexing not yet implemented");
```

With:
```csharp
                var reindexed = profile.Perimeter.ReindexAt(perimeterPt, perimeterEntity);
                result.Codes.AddRange(ConvertShapeToMoves(reindexed, perimeterPt));
                // TODO: MicrotabLeadOut — trim last cutting move by GapSize
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest.Core/OpenNest.Core.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 5: Build full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded, 0 errors

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs
git commit -m "feat: wire contour re-indexing into ContourCuttingStrategy.Apply()"
```
