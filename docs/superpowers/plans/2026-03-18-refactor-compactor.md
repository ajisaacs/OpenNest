# Refactor Compactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prune dead code from Compactor and deduplicate the Push overloads into a single scanning core.

**Architecture:** Delete 6 unused methods (Compact, CompactLoop, SavePositions, RestorePositions, CompactIndividual, CompactIndividualLoop). Unify the `Push(... PushDirection)` core overload to convert its PushDirection to a unit Vector and delegate to the `Push(... Vector)` overload, eliminating ~60 lines of duplicated obstacle scanning logic. PushBoundingBox stays separate since it's a fundamentally different algorithm (no geometry lines).

**Tech Stack:** C# / .NET 8

---

### Task 1: Write Compactor Push tests as a safety net

No Compactor tests exist. Before changing anything, add tests for the public Push methods that have live callers: `Push(parts, obstacles, workArea, spacing, PushDirection)` and `Push(parts, obstacles, workArea, spacing, angle)`.

**Files:**
- Create: `OpenNest.Tests/CompactorTests.cs`

- [ ] **Step 1: Write tests for Push with PushDirection**

```csharp
using OpenNest;
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using Xunit;
using System.Collections.Generic;

namespace OpenNest.Tests
{
    public class CompactorTests
    {
        private static Drawing MakeRectDrawing(double w, double h)
        {
            var pgm = new OpenNest.CNC.Program();
            pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
            pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, 0)));
            pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, h)));
            pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, h)));
            pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
            return new Drawing("rect", pgm);
        }

        private static Part MakeRectPart(double x, double y, double w, double h)
        {
            var drawing = MakeRectDrawing(w, h);
            var part = new Part(drawing) { Location = new Vector(x, y) };
            part.UpdateBounds();
            return part;
        }

        [Fact]
        public void Push_Left_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            var distance = Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Left < 1);
        }

        [Fact]
        public void Push_Left_StopsAtObstacle()
        {
            var workArea = new Box(0, 0, 100, 100);
            var obstacle = MakeRectPart(0, 0, 10, 10);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part> { obstacle };

            Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.True(part.BoundingBox.Left >= obstacle.BoundingBox.Right - 0.1);
        }

        [Fact]
        public void Push_Down_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(0, 50, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            var distance = Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Down);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Bottom < 1);
        }

        [Fact]
        public void Push_ReturnsZero_WhenAlreadyAtEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(0, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            var distance = Compactor.Push(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.Equal(0, distance);
        }

        [Fact]
        public void Push_WithSpacing_MaintainsGap()
        {
            var workArea = new Box(0, 0, 100, 100);
            var obstacle = MakeRectPart(0, 0, 10, 10);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part> { obstacle };

            Compactor.Push(moving, obstacles, workArea, 2, PushDirection.Left);

            Assert.True(part.BoundingBox.Left >= obstacle.BoundingBox.Right + 2 - 0.5);
        }
    }
}
```

- [ ] **Step 2: Write tests for Push with angle (Vector-based)**

Add to the same file:

```csharp
        [Fact]
        public void Push_AngleLeft_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            // angle = π = push left
            var distance = Compactor.Push(moving, obstacles, workArea, 0, System.Math.PI);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Left < 1);
        }

        [Fact]
        public void Push_AngleDown_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(0, 50, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            // angle = 3π/2 = push down
            var distance = Compactor.Push(moving, obstacles, workArea, 0, 3 * System.Math.PI / 2);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Bottom < 1);
        }
```

- [ ] **Step 3: Write tests for PushBoundingBox**

Add to the same file:

```csharp
        [Fact]
        public void PushBoundingBox_Left_MovesPartTowardEdge()
        {
            var workArea = new Box(0, 0, 100, 100);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part>();

            var distance = Compactor.PushBoundingBox(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.True(distance > 0);
            Assert.True(part.BoundingBox.Left < 1);
        }

        [Fact]
        public void PushBoundingBox_StopsAtObstacle()
        {
            var workArea = new Box(0, 0, 100, 100);
            var obstacle = MakeRectPart(0, 0, 10, 10);
            var part = MakeRectPart(50, 0, 10, 10);
            var moving = new List<Part> { part };
            var obstacles = new List<Part> { obstacle };

            Compactor.PushBoundingBox(moving, obstacles, workArea, 0, PushDirection.Left);

            Assert.True(part.BoundingBox.Left >= obstacle.BoundingBox.Right - 0.1);
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~CompactorTests" -v n`
Expected: All tests PASS (these test existing behavior before refactoring)

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Tests/CompactorTests.cs
git commit -m "test: add Compactor safety-net tests before refactor"
```

---

### Task 2: Delete dead code

Remove the 6 methods that have zero live callers: `Compact`, `CompactLoop`, `SavePositions`, `RestorePositions`, `CompactIndividual`, `CompactIndividualLoop`. Also remove the unused `contactGap` variable (line 181) and the misplaced XML doc comment above `RepeatThreshold` (lines 16-20, describes the deleted `Compact` method).

**Files:**
- Modify: `OpenNest.Engine/Fill/Compactor.cs`

- [ ] **Step 1: Delete SavePositions and RestorePositions**

Delete `SavePositions` (lines 64-70) and `RestorePositions` (lines 72-76). These are only used by `Compact` and `CompactIndividual`.

- [ ] **Step 2: Delete Compact and CompactLoop**

Delete the `Compact` method (lines 24-44) and `CompactLoop` method (lines 46-62). Zero callers.

- [ ] **Step 3: Delete CompactIndividual and CompactIndividualLoop**

Delete `CompactIndividual` (lines 312-332) and `CompactIndividualLoop` (lines 334-360). Only caller is a commented-out line in `StripNestEngine.cs:189`.

- [ ] **Step 4: Remove the commented-out caller in StripNestEngine**

In `OpenNest.Engine/StripNestEngine.cs`, delete the entire commented-out block (lines 186-194):
```csharp
            // TODO: Compact strip parts individually to close geometry-based gaps.
            // Disabled pending investigation — remnant finder picks up gaps created
            // by compaction and scatters parts into them.
            // Compactor.CompactIndividual(bestParts, workArea, Plate.PartSpacing);
            //
            // var compactedBox = bestParts.Cast<IBoundable>().GetBoundingBox();
            // bestDim = direction == StripDirection.Bottom
            //     ? compactedBox.Top - workArea.Y
            //     : compactedBox.Right - workArea.X;
```

- [ ] **Step 5: Clean up stale doc comment and dead variable**

Remove the orphaned XML doc comment above `RepeatThreshold` (lines 16-20 — it describes the deleted `Compact` method). Remove the `RepeatThreshold` and `MaxIterations` constants (only used by the deleted loop methods). Remove the unused `contactGap` variable from the `Push(... PushDirection)` method (line 181).

- [ ] **Step 6: Run tests**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~CompactorTests" -v n`
Expected: All tests PASS (deleted code was unused)

- [ ] **Step 7: Build full solution to verify no compilation errors**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded, 0 errors

- [ ] **Step 8: Commit**

```bash
git add OpenNest.Engine/Fill/Compactor.cs OpenNest.Engine/StripNestEngine.cs
git commit -m "refactor(compactor): remove dead code — Compact, CompactIndividual, and helpers"
```

---

### Task 3: Deduplicate Push overloads

The `Push(... PushDirection)` core overload (lines 166-238) duplicates the obstacle scanning loop from `Push(... Vector)` (lines 102-164). Convert `PushDirection` to a unit `Vector` and delegate.

**Files:**
- Modify: `OpenNest.Engine/Fill/Compactor.cs`

- [ ] **Step 1: Replace the Push(... PushDirection) core overload**

Replace the full body of `Push(List<Part> movingParts, List<Part> obstacleParts, Box workArea, double partSpacing, PushDirection direction)` with a delegation to the Vector overload:

```csharp
public static double Push(List<Part> movingParts, List<Part> obstacleParts,
    Box workArea, double partSpacing, PushDirection direction)
{
    var vector = SpatialQuery.DirectionToOffset(direction, 1.0);
    return Push(movingParts, obstacleParts, workArea, partSpacing, vector);
}
```

This works because `DirectionToOffset(Left, 1.0)` returns `(-1, 0)`, which is the unit vector for "push left" — exactly what `new Vector(Math.Cos(π), Math.Sin(π))` produces. The Vector overload already handles edge distance, obstacle scanning, geometry lines, and offset application identically.

- [ ] **Step 2: Update the angle-based Push to accept Vector directly**

Rename the existing `Push(... double angle)` core overload to accept a `Vector` direction instead of computing it internally. This avoids a redundant cos/sin when the PushDirection overload already provides a unit vector.

Change the signature from:
```csharp
public static double Push(List<Part> movingParts, List<Part> obstacleParts,
    Box workArea, double partSpacing, double angle)
```
to:
```csharp
public static double Push(List<Part> movingParts, List<Part> obstacleParts,
    Box workArea, double partSpacing, Vector direction)
```

Remove the `var direction = new Vector(...)` line from the body since `direction` is now a parameter.

- [ ] **Step 3: Update the angle convenience overload to convert**

The convenience overload `Push(List<Part> movingParts, Plate plate, double angle)` must now convert the angle to a Vector before calling the core:

```csharp
public static double Push(List<Part> movingParts, Plate plate, double angle)
{
    var obstacleParts = plate.Parts
        .Where(p => !movingParts.Contains(p))
        .ToList();

    var direction = new Vector(System.Math.Cos(angle), System.Math.Sin(angle));
    return Push(movingParts, obstacleParts, plate.WorkArea(), plate.PartSpacing, direction);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~CompactorTests" -v n`
Expected: All tests PASS

- [ ] **Step 5: Build full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded, 0 errors. All callers in FillExtents, ActionClone, PlateView, PatternTileForm compile without changes — their call signatures are unchanged.

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/Fill/Compactor.cs
git commit -m "refactor(compactor): deduplicate Push — PushDirection delegates to Vector overload"
```

---

### Task 4: Final cleanup and verify

**Files:**
- Modify: `OpenNest.Engine/Fill/Compactor.cs` (if needed)

- [ ] **Step 1: Run full test suite**

Run: `dotnet test OpenNest.Tests -v n`
Expected: All tests PASS

- [ ] **Step 2: Verify Compactor is clean**

The final Compactor should have 6 public methods:
1. `Push(parts, plate, PushDirection)` — convenience, extracts plate fields
2. `Push(parts, plate, angle)` — convenience, converts angle to Vector
3. `Push(parts, obstacles, workArea, spacing, PushDirection)` — converts to Vector, delegates
4. `Push(parts, obstacles, workArea, spacing, Vector)` — the single scanning core
5. `PushBoundingBox(parts, plate, direction)` — convenience
6. `PushBoundingBox(parts, obstacles, workArea, spacing, direction)` — BB-only core

Plus one constant: `ChordTolerance`.

File should be ~110-120 lines, down from 362.
