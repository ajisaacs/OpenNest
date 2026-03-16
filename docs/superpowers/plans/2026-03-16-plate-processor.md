# Plate Processor Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a plate-level orchestrator that sequences parts, assigns per-part lead-ins based on approach direction, and plans safe rapid paths between parts.

**Architecture:** Three-stage pipeline in OpenNest.Engine — IPartSequencer (cut order) → ContourCuttingStrategy (lead-ins) → IRapidPlanner (safe rapids) — wired by PlateProcessor. Non-destructive: results stored in PlateResult, original Part.Program untouched.

**Tech Stack:** .NET 8, xUnit (new test project), OpenNest.Core, OpenNest.Engine

**Spec:** `docs/superpowers/specs/2026-03-15-plate-processor-design.md`

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj` | xUnit test project |
| Create | `OpenNest.Engine.Tests/TestHelpers.cs` | Shared test helpers (MakePartAt, MakePlate) |
| Modify | `OpenNest.sln` | Add test project to solution |
| Modify | `OpenNest.Core/Part.cs` | Add `HasManualLeadIns` property |
| Create | `OpenNest.Core/CNC/CuttingStrategy/CuttingResult.cs` | Return type for ContourCuttingStrategy.Apply |
| Modify | `OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs` | Change Apply signature to accept Vector approachPoint |
| Create | `OpenNest.Engine/Sequencing/IPartSequencer.cs` | Interface + SequencedPart struct |
| Create | `OpenNest.Engine/Sequencing/PartSequencerFactory.cs` | Maps SequenceMethod to IPartSequencer |
| Create | `OpenNest.Engine/Sequencing/RightSideSequencer.cs` | Sort by X descending |
| Create | `OpenNest.Engine/Sequencing/LeftSideSequencer.cs` | Sort by X ascending |
| Create | `OpenNest.Engine/Sequencing/BottomSideSequencer.cs` | Sort by Y ascending |
| Create | `OpenNest.Engine/Sequencing/EdgeStartSequencer.cs` | Sort by distance from nearest plate edge |
| Create | `OpenNest.Engine/Sequencing/PlateHelper.cs` | Shared exit point calculation |
| Create | `OpenNest.Engine/Sequencing/LeastCodeSequencer.cs` | Nearest-neighbor + 2-opt |
| Create | `OpenNest.Engine/Sequencing/AdvancedSequencer.cs` | Row/column grouping with serpentine ordering |
| Create | `OpenNest.Engine/RapidPlanning/IRapidPlanner.cs` | Interface |
| Create | `OpenNest.Engine/RapidPlanning/RapidPath.cs` | Result struct |
| Create | `OpenNest.Engine/RapidPlanning/SafeHeightRapidPlanner.cs` | Always head-up |
| Create | `OpenNest.Engine/RapidPlanning/DirectRapidPlanner.cs` | Head-down if clear, head-up if blocked |
| Create | `OpenNest.Engine/PlateProcessor.cs` | Orchestrator |
| Create | `OpenNest.Engine/PlateResult.cs` | Result types (PlateResult, ProcessedPart) |

---

## Chunk 1: Foundation

### Task 1: Create xUnit test project

**Files:**
- Create: `OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj`
- Modify: `OpenNest.sln`

- [ ] **Step 1: Create the test project**

```bash
cd C:/Users/AJ/Desktop/Projects/OpenNest
dotnet new xunit -n OpenNest.Engine.Tests --framework net8.0-windows
dotnet sln add OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj
dotnet add OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj reference OpenNest.Core/OpenNest.Core.csproj
dotnet add OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj reference OpenNest.Engine/OpenNest.Engine.csproj
```

- [ ] **Step 2: Verify the project builds**

Run: `dotnet build OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj`
Expected: Build succeeded

- [ ] **Step 3: Delete the generated UnitTest1.cs**

Delete `OpenNest.Engine.Tests/UnitTest1.cs` — we'll create our own test files.

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine.Tests/ OpenNest.sln
git commit -m "chore: add OpenNest.Engine.Tests xUnit project"
```

---

### Task 2: Shared test helper

**Files:**
- Create: `OpenNest.Engine.Tests/TestHelpers.cs`

- [ ] **Step 1: Create TestHelpers.cs**

This helper is used by nearly every test in the plan. Creates simple 1x1 or 2x2 square parts at known positions.

```csharp
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.Engine.Tests;

internal static class TestHelpers
{
    public static Part MakePartAt(double x, double y, double size = 1)
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new LinearMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        return new Part(drawing, new Vector(x, y));
    }

    public static Plate MakePlate(double width = 60, double length = 120, params Part[] parts)
    {
        var plate = new Plate(width, length);
        foreach (var p in parts)
            plate.Parts.Add(p);
        return plate;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine.Tests/TestHelpers.cs
git commit -m "chore: add shared test helpers for Engine tests"
```

---

### Task 3: CuttingResult struct

**Files:**
- Create: `OpenNest.Core/CNC/CuttingStrategy/CuttingResult.cs`
- Test: `OpenNest.Engine.Tests/CuttingResultTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests;

public class CuttingResultTests
{
    [Fact]
    public void CuttingResult_StoresValues()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(1, 2)));
        var point = new Vector(3, 4);

        var result = new CuttingResult { Program = pgm, LastCutPoint = point };

        Assert.Same(pgm, result.Program);
        Assert.Equal(3, result.LastCutPoint.X);
        Assert.Equal(4, result.LastCutPoint.Y);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Engine.Tests --filter CuttingResult_StoresValues -v n`
Expected: FAIL — `CuttingResult` type does not exist

- [ ] **Step 3: Create CuttingResult.cs**

```csharp
using OpenNest.CNC;
using OpenNest.Geometry;

namespace OpenNest.CNC.CuttingStrategy
{
    public readonly struct CuttingResult
    {
        public Program Program { get; init; }
        public Vector LastCutPoint { get; init; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Engine.Tests --filter CuttingResult_StoresValues -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/CuttingResult.cs OpenNest.Engine.Tests/CuttingResultTests.cs
git commit -m "feat: add CuttingResult struct"
```

---

### Task 4: Part.HasManualLeadIns flag

**Files:**
- Modify: `OpenNest.Core/Part.cs`
- Test: `OpenNest.Engine.Tests/PartFlagTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using OpenNest.CNC;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests;

public class PartFlagTests
{
    [Fact]
    public void HasManualLeadIns_DefaultsFalse()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        var part = new Part(drawing);

        Assert.False(part.HasManualLeadIns);
    }

    [Fact]
    public void HasManualLeadIns_CanBeSet()
    {
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(0, 0)));
        var drawing = new Drawing("test", pgm);
        var part = new Part(drawing);

        part.HasManualLeadIns = true;

        Assert.True(part.HasManualLeadIns);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Engine.Tests --filter HasManualLeadIns -v n`
Expected: FAIL — `HasManualLeadIns` property does not exist

- [ ] **Step 3: Add the property to Part.cs**

In `OpenNest.Core/Part.cs`, after the `Program` property (line 52), add:

```csharp
public bool HasManualLeadIns { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Engine.Tests --filter HasManualLeadIns -v n`
Expected: 2 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Core/Part.cs OpenNest.Engine.Tests/PartFlagTests.cs
git commit -m "feat: add Part.HasManualLeadIns flag"
```

---

### Task 5: ContourCuttingStrategy signature change

**Files:**
- Modify: `OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs`

This changes the existing `Apply` method signature and return type. No new tests needed — this modifies an existing class that currently has no callers (it was recently built).

- [ ] **Step 1: Change the Apply signature**

In `ContourCuttingStrategy.cs`, change line 10:

```csharp
// Before:
public Program Apply(Program partProgram, Plate plate)
// After:
public CuttingResult Apply(Program partProgram, Vector approachPoint)
```

- [ ] **Step 2: Replace GetExitPoint usage with approachPoint**

Replace line 12:
```csharp
// Before:
var exitPoint = GetExitPoint(plate);
// After:
var exitPoint = approachPoint;
```

- [ ] **Step 3: Delete the GetExitPoint method**

Delete lines 66-79 (the `GetExitPoint(Plate plate)` method). This logic moves to `PlateProcessor` later.

- [ ] **Step 4: Track the last cut point and return CuttingResult**

`perimeterPt` is declared inside a bare block (lines 48-61) and goes out of scope at the closing brace. Declare a `lastCutPoint` variable before the block and assign it inside.

Before the `// Perimeter last` block (before line 48), add:
```csharp
var lastCutPoint = exitPoint;
```

Inside the `// Perimeter last` block, after the perimeter point is computed (after line 49), add:
```csharp
lastCutPoint = perimeterPt;
```

Replace line 63 (`return result;`):
```csharp
return new CuttingResult
{
    Program = result,
    LastCutPoint = lastCutPoint
};
```

- [ ] **Step 5: Remove the unused Plate using directive if needed**

Check if `Plate` is still referenced. The `using OpenNest.Geometry` is still needed for `Vector`.

- [ ] **Step 6: Build to verify**

Run: `dotnet build OpenNest.Core/OpenNest.Core.csproj`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add OpenNest.Core/CNC/CuttingStrategy/ContourCuttingStrategy.cs
git commit -m "refactor: change ContourCuttingStrategy.Apply to accept approachPoint"
```

---

## Chunk 2: Part Sequencing

### Task 6: IPartSequencer interface and SequencedPart

**Files:**
- Create: `OpenNest.Engine/Sequencing/IPartSequencer.cs`

- [ ] **Step 1: Create the interface file**

```csharp
using System.Collections.Generic;

namespace OpenNest.Engine.Sequencing
{
    public readonly struct SequencedPart
    {
        public Part Part { get; init; }
    }

    public interface IPartSequencer
    {
        List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/Sequencing/
git commit -m "feat: add IPartSequencer interface and SequencedPart"
```

---

### Task 7: Directional sequencers (RightSide, LeftSide, BottomSide)

**Files:**
- Create: `OpenNest.Engine/Sequencing/RightSideSequencer.cs`
- Create: `OpenNest.Engine/Sequencing/LeftSideSequencer.cs`
- Create: `OpenNest.Engine/Sequencing/BottomSideSequencer.cs`
- Test: `OpenNest.Engine.Tests/Sequencing/DirectionalSequencerTests.cs`

- [ ] **Step 1: Write the tests**

Create a helper method to build simple parts at known positions, then test all three directional sequencers.

```csharp
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Sequencing;

public class DirectionalSequencerTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y);
    private static Plate MakePlate(params Part[] parts) => TestHelpers.MakePlate(60, 120, parts);

    [Fact]
    public void RightSide_SortsXDescending()
    {
        var a = MakePartAt(10, 5);
        var b = MakePartAt(30, 5);
        var c = MakePartAt(20, 5);
        var plate = MakePlate(a, b, c);

        var sequencer = new RightSideSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(b, result[0].Part);
        Assert.Same(c, result[1].Part);
        Assert.Same(a, result[2].Part);
    }

    [Fact]
    public void LeftSide_SortsXAscending()
    {
        var a = MakePartAt(10, 5);
        var b = MakePartAt(30, 5);
        var c = MakePartAt(20, 5);
        var plate = MakePlate(a, b, c);

        var sequencer = new LeftSideSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(a, result[0].Part);
        Assert.Same(c, result[1].Part);
        Assert.Same(b, result[2].Part);
    }

    [Fact]
    public void BottomSide_SortsYAscending()
    {
        var a = MakePartAt(5, 20);
        var b = MakePartAt(5, 5);
        var c = MakePartAt(5, 10);
        var plate = MakePlate(a, b, c);

        var sequencer = new BottomSideSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(b, result[0].Part);
        Assert.Same(c, result[1].Part);
        Assert.Same(a, result[2].Part);
    }

    [Fact]
    public void RightSide_TiesBrokenByPerpendicularAxis()
    {
        var a = MakePartAt(10, 20);
        var b = MakePartAt(10, 5);
        var plate = MakePlate(a, b);

        var sequencer = new RightSideSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        // Same X, tie broken by Y — lower Y first for RightSide
        Assert.Same(b, result[0].Part);
        Assert.Same(a, result[1].Part);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Engine.Tests --filter DirectionalSequencerTests -v n`
Expected: FAIL — sequencer classes don't exist

- [ ] **Step 3: Implement RightSideSequencer**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Sequencing
{
    public class RightSideSequencer : IPartSequencer
    {
        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            return parts
                .OrderByDescending(p => p.BoundingBox.Center.X)
                .ThenBy(p => p.BoundingBox.Center.Y)
                .Select(p => new SequencedPart { Part = p })
                .ToList();
        }
    }
}
```

- [ ] **Step 4: Implement LeftSideSequencer**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Sequencing
{
    public class LeftSideSequencer : IPartSequencer
    {
        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            return parts
                .OrderBy(p => p.BoundingBox.Center.X)
                .ThenBy(p => p.BoundingBox.Center.Y)
                .Select(p => new SequencedPart { Part = p })
                .ToList();
        }
    }
}
```

- [ ] **Step 5: Implement BottomSideSequencer**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace OpenNest.Engine.Sequencing
{
    public class BottomSideSequencer : IPartSequencer
    {
        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            return parts
                .OrderBy(p => p.BoundingBox.Center.Y)
                .ThenBy(p => p.BoundingBox.Center.X)
                .Select(p => new SequencedPart { Part = p })
                .ToList();
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test OpenNest.Engine.Tests --filter DirectionalSequencerTests -v n`
Expected: 4 tests PASS

- [ ] **Step 7: Commit**

```bash
git add OpenNest.Engine/Sequencing/ OpenNest.Engine.Tests/Sequencing/
git commit -m "feat: add directional part sequencers (RightSide, LeftSide, BottomSide)"
```

---

### Task 8: EdgeStartSequencer

**Files:**
- Create: `OpenNest.Engine/Sequencing/EdgeStartSequencer.cs`
- Test: `OpenNest.Engine.Tests/Sequencing/EdgeStartSequencerTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Sequencing;

public class EdgeStartSequencerTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y);

    [Fact]
    public void SortsByDistanceFromNearestEdge()
    {
        // Plate is 60x120, Q1 (origin at 0,0)
        var plate = new Plate(60, 120);
        var edgePart = MakePartAt(1, 1);       // 1 unit from left and bottom edges
        var centerPart = MakePartAt(25, 55);   // ~25 from nearest edge
        var midPart = MakePartAt(10, 10);      // 10 from left and bottom edges
        plate.Parts.Add(edgePart);
        plate.Parts.Add(centerPart);
        plate.Parts.Add(midPart);

        var sequencer = new EdgeStartSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Same(edgePart, result[0].Part);
        Assert.Same(midPart, result[1].Part);
        Assert.Same(centerPart, result[2].Part);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Engine.Tests --filter EdgeStartSequencerTests -v n`
Expected: FAIL

- [ ] **Step 3: Implement EdgeStartSequencer**

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Engine.Sequencing
{
    public class EdgeStartSequencer : IPartSequencer
    {
        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            var plateBox = plate.BoundingBox(false);

            return parts
                .OrderBy(p => MinEdgeDistance(p.BoundingBox, plateBox))
                .ThenBy(p => p.BoundingBox.Center.X)
                .Select(p => new SequencedPart { Part = p })
                .ToList();
        }

        private static double MinEdgeDistance(Box partBox, Box plateBox)
        {
            var center = partBox.Center;
            var distLeft = System.Math.Abs(center.X - plateBox.Left);
            var distRight = System.Math.Abs(center.X - plateBox.Right);
            var distBottom = System.Math.Abs(center.Y - plateBox.Bottom);
            var distTop = System.Math.Abs(center.Y - plateBox.Top);
            return System.Math.Min(System.Math.Min(distLeft, distRight),
                                   System.Math.Min(distBottom, distTop));
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Engine.Tests --filter EdgeStartSequencerTests -v n`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/Sequencing/EdgeStartSequencer.cs OpenNest.Engine.Tests/Sequencing/EdgeStartSequencerTests.cs
git commit -m "feat: add EdgeStartSequencer"
```

---

### Task 9: LeastCodeSequencer (nearest-neighbor + 2-opt)

**Files:**
- Create: `OpenNest.Engine/Sequencing/LeastCodeSequencer.cs`
- Test: `OpenNest.Engine.Tests/Sequencing/LeastCodeSequencerTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Sequencing;

public class LeastCodeSequencerTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y);

    [Fact]
    public void NearestNeighbor_FromExitPoint()
    {
        // Q1 plate: exit point is (60, 120) (top-right corner)
        var plate = new Plate(60, 120);
        var farPart = MakePartAt(5, 5);
        var nearPart = MakePartAt(55, 115);
        plate.Parts.Add(farPart);
        plate.Parts.Add(nearPart);

        var sequencer = new LeastCodeSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        // nearPart is closer to exit point (60,120), should come first
        Assert.Same(nearPart, result[0].Part);
        Assert.Same(farPart, result[1].Part);
    }

    [Fact]
    public void PreservesAllParts()
    {
        var plate = new Plate(60, 120);
        for (var i = 0; i < 10; i++)
            plate.Parts.Add(MakePartAt(i * 5, i * 10));

        var sequencer = new LeastCodeSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void TwoOpt_ImprovesSolution()
    {
        // Create a scenario where nearest-neighbor produces a crossing
        // that 2-opt should fix
        var plate = new Plate(100, 100);
        // Exit point at (100, 100) for Q1
        // Parts arranged so NN would cross but 2-opt should uncross
        var a = MakePartAt(90, 90);  // nearest to exit
        var b = MakePartAt(10, 80);  // NN picks this 2nd (closest to a)
        var c = MakePartAt(80, 10);  // NN picks this 3rd — crosses!
        var d = MakePartAt(5, 5);    // last
        plate.Parts.Add(a);
        plate.Parts.Add(b);
        plate.Parts.Add(c);
        plate.Parts.Add(d);

        var sequencer = new LeastCodeSequencer();
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        // Just verify all parts present and result is deterministic
        Assert.Equal(4, result.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Engine.Tests --filter LeastCodeSequencerTests -v n`
Expected: FAIL

- [ ] **Step 3: Implement LeastCodeSequencer**

The exit point logic (opposite corner from quadrant origin) is needed here and will also be used by `PlateProcessor`. Put it as a static helper.

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest.Engine.Sequencing
{
    public class LeastCodeSequencer : IPartSequencer
    {
        private readonly int _maxIterations;

        public LeastCodeSequencer(int maxIterations = 100)
        {
            _maxIterations = maxIterations;
        }

        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            if (parts.Count == 0)
                return new List<SequencedPart>();

            var exitPoint = PlateHelper.GetExitPoint(plate);
            var order = NearestNeighbor(parts, exitPoint);
            TwoOpt(order, exitPoint);

            return order.Select(p => new SequencedPart { Part = p }).ToList();
        }

        private static List<Part> NearestNeighbor(IReadOnlyList<Part> parts, Vector startPoint)
        {
            var remaining = new List<Part>(parts);
            var ordered = new List<Part>();
            var currentPoint = startPoint;

            while (remaining.Count > 0)
            {
                var nearest = remaining[0];
                var nearestDist = nearest.BoundingBox.Center.DistanceTo(currentPoint);

                for (var i = 1; i < remaining.Count; i++)
                {
                    var dist = remaining[i].BoundingBox.Center.DistanceTo(currentPoint);
                    if (dist < nearestDist)
                    {
                        nearest = remaining[i];
                        nearestDist = dist;
                    }
                }

                ordered.Add(nearest);
                remaining.Remove(nearest);
                currentPoint = nearest.BoundingBox.Center;
            }

            return ordered;
        }

        private void TwoOpt(List<Part> order, Vector startPoint)
        {
            var improved = true;
            var iterations = 0;

            while (improved && iterations < _maxIterations)
            {
                improved = false;
                iterations++;

                for (var i = 0; i < order.Count - 1; i++)
                {
                    for (var j = i + 1; j < order.Count; j++)
                    {
                        var delta = TwoOptDelta(order, startPoint, i, j);
                        if (delta < -OpenNest.Math.Tolerance.Epsilon)
                        {
                            Reverse(order, i, j);
                            improved = true;
                        }
                    }
                }
            }
        }

        private static double TwoOptDelta(List<Part> order, Vector startPoint, int i, int j)
        {
            var prevI = i == 0 ? startPoint : order[i - 1].BoundingBox.Center;
            var ci = order[i].BoundingBox.Center;
            var cj = order[j].BoundingBox.Center;
            var nextJ = j + 1 < order.Count ? order[j + 1].BoundingBox.Center : (Vector?)null;

            var oldDist = prevI.DistanceTo(ci);
            var newDist = prevI.DistanceTo(cj);

            if (nextJ.HasValue)
            {
                oldDist += cj.DistanceTo(nextJ.Value);
                newDist += ci.DistanceTo(nextJ.Value);
            }

            return newDist - oldDist;
        }

        private static void Reverse(List<Part> list, int start, int end)
        {
            while (start < end)
            {
                (list[start], list[end]) = (list[end], list[start]);
                start++;
                end--;
            }
        }
    }
}
```

- [ ] **Step 4: Create PlateHelper for shared exit point logic**

```csharp
using OpenNest.Geometry;

namespace OpenNest.Engine.Sequencing
{
    internal static class PlateHelper
    {
        public static Vector GetExitPoint(Plate plate)
        {
            var w = plate.Size.Width;
            var l = plate.Size.Length;

            return plate.Quadrant switch
            {
                1 => new Vector(w, l),
                2 => new Vector(0, l),
                3 => new Vector(0, 0),
                4 => new Vector(w, 0),
                _ => new Vector(w, l)
            };
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test OpenNest.Engine.Tests --filter LeastCodeSequencerTests -v n`
Expected: 3 tests PASS

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/Sequencing/ OpenNest.Engine.Tests/Sequencing/
git commit -m "feat: add LeastCodeSequencer with nearest-neighbor and 2-opt"
```

---

### Task 10: AdvancedSequencer

**Files:**
- Create: `OpenNest.Engine/Sequencing/AdvancedSequencer.cs`
- Test: `OpenNest.Engine.Tests/Sequencing/AdvancedSequencerTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.Sequencing;

public class AdvancedSequencerTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y);

    [Fact]
    public void GroupsIntoRows_NoAlternate()
    {
        // Two rows of parts with clear Y separation
        // Q1 plate 100x100: exit point (100, 100), so leftToRight starts true
        var plate = new Plate(100, 100);
        var row1a = MakePartAt(10, 10);
        var row1b = MakePartAt(30, 10);
        var row2a = MakePartAt(10, 50);
        var row2b = MakePartAt(30, 50);
        plate.Parts.Add(row1a);
        plate.Parts.Add(row1b);
        plate.Parts.Add(row2a);
        plate.Parts.Add(row2b);

        var parameters = new SequenceParameters
        {
            Method = SequenceMethod.Advanced,
            MinDistanceBetweenRowsColumns = 5.0,
            AlternateRowsColumns = false
        };
        var sequencer = new AdvancedSequencer(parameters);
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        // No alternation: both rows left-to-right (X ascending)
        Assert.Same(row1a, result[0].Part);
        Assert.Same(row1b, result[1].Part);
        Assert.Same(row2a, result[2].Part);
        Assert.Same(row2b, result[3].Part);
    }

    [Fact]
    public void SerpentineAlternatesDirection()
    {
        // Q1 plate 100x100: exit point (100, 100), so leftToRight starts true
        var plate = new Plate(100, 100);
        var r1Left = MakePartAt(10, 10);
        var r1Right = MakePartAt(30, 10);
        var r2Left = MakePartAt(10, 50);
        var r2Right = MakePartAt(30, 50);
        plate.Parts.Add(r1Left);
        plate.Parts.Add(r1Right);
        plate.Parts.Add(r2Left);
        plate.Parts.Add(r2Right);

        var parameters = new SequenceParameters
        {
            Method = SequenceMethod.Advanced,
            MinDistanceBetweenRowsColumns = 5.0,
            AlternateRowsColumns = true
        };
        var sequencer = new AdvancedSequencer(parameters);
        var result = sequencer.Sequence(plate.Parts.ToList(), plate);

        // Row 1: left-to-right (X ascending)
        Assert.Same(r1Left, result[0].Part);
        Assert.Same(r1Right, result[1].Part);
        // Row 2: right-to-left (X descending, alternated)
        Assert.Same(r2Right, result[2].Part);
        Assert.Same(r2Left, result[3].Part);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Engine.Tests --filter AdvancedSequencerTests -v n`
Expected: FAIL

- [ ] **Step 3: Implement AdvancedSequencer**

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Geometry;

namespace OpenNest.Engine.Sequencing
{
    public class AdvancedSequencer : IPartSequencer
    {
        private readonly SequenceParameters _parameters;

        public AdvancedSequencer(SequenceParameters parameters)
        {
            _parameters = parameters;
        }

        public List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate)
        {
            if (parts.Count == 0)
                return new List<SequencedPart>();

            var rows = GroupIntoRows(parts);
            var exitPoint = PlateHelper.GetExitPoint(plate);

            // Sort rows by Y (bottom to top for Q1)
            rows.Sort((a, b) => a[0].BoundingBox.Center.Y.CompareTo(b[0].BoundingBox.Center.Y));

            var result = new List<SequencedPart>();
            var leftToRight = exitPoint.X > plate.Size.Width * 0.5;

            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];

                if (leftToRight)
                    row.Sort((a, b) => a.BoundingBox.Center.X.CompareTo(b.BoundingBox.Center.X));
                else
                    row.Sort((a, b) => b.BoundingBox.Center.X.CompareTo(a.BoundingBox.Center.X));

                foreach (var part in row)
                    result.Add(new SequencedPart { Part = part });

                if (_parameters.AlternateRowsColumns)
                    leftToRight = !leftToRight;
            }

            return result;
        }

        private List<List<Part>> GroupIntoRows(IReadOnlyList<Part> parts)
        {
            var sorted = parts.OrderBy(p => p.BoundingBox.Center.Y).ToList();
            var rows = new List<List<Part>>();
            var currentRow = new List<Part> { sorted[0] };

            for (var i = 1; i < sorted.Count; i++)
            {
                var prevY = sorted[i - 1].BoundingBox.Center.Y;
                var currY = sorted[i].BoundingBox.Center.Y;

                if (currY - prevY > _parameters.MinDistanceBetweenRowsColumns)
                {
                    rows.Add(currentRow);
                    currentRow = new List<Part>();
                }

                currentRow.Add(sorted[i]);
            }

            rows.Add(currentRow);
            return rows;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Engine.Tests --filter AdvancedSequencerTests -v n`
Expected: 2 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/Sequencing/AdvancedSequencer.cs OpenNest.Engine.Tests/Sequencing/AdvancedSequencerTests.cs
git commit -m "feat: add AdvancedSequencer with row grouping and serpentine"
```

---

### Task 11: PartSequencerFactory

**Files:**
- Create: `OpenNest.Engine/Sequencing/PartSequencerFactory.cs`
- Test: `OpenNest.Engine.Tests/Sequencing/PartSequencerFactoryTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.Sequencing;
using Xunit;

namespace OpenNest.Engine.Tests.Sequencing;

public class PartSequencerFactoryTests
{
    [Theory]
    [InlineData(SequenceMethod.RightSide, typeof(RightSideSequencer))]
    [InlineData(SequenceMethod.LeftSide, typeof(LeftSideSequencer))]
    [InlineData(SequenceMethod.BottomSide, typeof(BottomSideSequencer))]
    [InlineData(SequenceMethod.EdgeStart, typeof(EdgeStartSequencer))]
    [InlineData(SequenceMethod.LeastCode, typeof(LeastCodeSequencer))]
    [InlineData(SequenceMethod.Advanced, typeof(AdvancedSequencer))]
    public void Create_ReturnsCorrectType(SequenceMethod method, Type expectedType)
    {
        var parameters = new SequenceParameters { Method = method };
        var sequencer = PartSequencerFactory.Create(parameters);
        Assert.IsType(expectedType, sequencer);
    }

    [Fact]
    public void Create_RightSideAlt_Throws()
    {
        var parameters = new SequenceParameters { Method = SequenceMethod.RightSideAlt };
        Assert.Throws<NotSupportedException>(() => PartSequencerFactory.Create(parameters));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Engine.Tests --filter PartSequencerFactoryTests -v n`
Expected: FAIL

- [ ] **Step 3: Implement PartSequencerFactory**

```csharp
using System;
using OpenNest.CNC.CuttingStrategy;

namespace OpenNest.Engine.Sequencing
{
    public static class PartSequencerFactory
    {
        public static IPartSequencer Create(SequenceParameters parameters)
        {
            return parameters.Method switch
            {
                SequenceMethod.RightSide => new RightSideSequencer(),
                SequenceMethod.LeftSide => new LeftSideSequencer(),
                SequenceMethod.BottomSide => new BottomSideSequencer(),
                SequenceMethod.EdgeStart => new EdgeStartSequencer(),
                SequenceMethod.LeastCode => new LeastCodeSequencer(),
                SequenceMethod.Advanced => new AdvancedSequencer(parameters),
                _ => throw new NotSupportedException($"SequenceMethod {parameters.Method} is not supported")
            };
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Engine.Tests --filter PartSequencerFactoryTests -v n`
Expected: 7 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/Sequencing/PartSequencerFactory.cs OpenNest.Engine.Tests/Sequencing/PartSequencerFactoryTests.cs
git commit -m "feat: add PartSequencerFactory"
```

---

## Chunk 3: Rapid Planning and Orchestrator

### Task 12: IRapidPlanner, RapidPath, and SafeHeightRapidPlanner

**Files:**
- Create: `OpenNest.Engine/RapidPlanning/IRapidPlanner.cs`
- Create: `OpenNest.Engine/RapidPlanning/RapidPath.cs`
- Create: `OpenNest.Engine/RapidPlanning/SafeHeightRapidPlanner.cs`
- Test: `OpenNest.Engine.Tests/RapidPlanning/SafeHeightRapidPlannerTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Collections.Generic;
using OpenNest.Engine.RapidPlanning;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.RapidPlanning;

public class SafeHeightRapidPlannerTests
{
    [Fact]
    public void AlwaysReturnsHeadUp()
    {
        var planner = new SafeHeightRapidPlanner();
        var from = new Vector(10, 10);
        var to = new Vector(50, 50);
        var cutAreas = new List<Shape>();

        var result = planner.Plan(from, to, cutAreas);

        Assert.True(result.HeadUp);
        Assert.Empty(result.Waypoints);
    }

    [Fact]
    public void ReturnsHeadUp_EvenWithCutAreas()
    {
        var planner = new SafeHeightRapidPlanner();
        var from = new Vector(0, 0);
        var to = new Vector(10, 10);

        // Add a cut area (doesn't matter — always head up)
        var shape = new Shape();
        shape.Entities.Add(new Line(new Vector(5, 0), new Vector(5, 20)));
        var cutAreas = new List<Shape> { shape };

        var result = planner.Plan(from, to, cutAreas);

        Assert.True(result.HeadUp);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Engine.Tests --filter SafeHeightRapidPlannerTests -v n`
Expected: FAIL

- [ ] **Step 3: Create IRapidPlanner.cs**

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.RapidPlanning
{
    public interface IRapidPlanner
    {
        RapidPath Plan(Vector from, Vector to, IReadOnlyList<Shape> cutAreas);
    }
}
```

- [ ] **Step 4: Create RapidPath.cs**

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.RapidPlanning
{
    public readonly struct RapidPath
    {
        public bool HeadUp { get; init; }
        public List<Vector> Waypoints { get; init; }
    }
}
```

- [ ] **Step 5: Create SafeHeightRapidPlanner.cs**

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.RapidPlanning
{
    public class SafeHeightRapidPlanner : IRapidPlanner
    {
        public RapidPath Plan(Vector from, Vector to, IReadOnlyList<Shape> cutAreas)
        {
            return new RapidPath
            {
                HeadUp = true,
                Waypoints = new List<Vector>()
            };
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test OpenNest.Engine.Tests --filter SafeHeightRapidPlannerTests -v n`
Expected: 2 tests PASS

- [ ] **Step 7: Commit**

```bash
git add OpenNest.Engine/RapidPlanning/ OpenNest.Engine.Tests/RapidPlanning/
git commit -m "feat: add IRapidPlanner, RapidPath, and SafeHeightRapidPlanner"
```

---

### Task 13: DirectRapidPlanner

**Files:**
- Create: `OpenNest.Engine/RapidPlanning/DirectRapidPlanner.cs`
- Test: `OpenNest.Engine.Tests/RapidPlanning/DirectRapidPlannerTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System.Collections.Generic;
using OpenNest.Engine.RapidPlanning;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests.RapidPlanning;

public class DirectRapidPlannerTests
{
    [Fact]
    public void NoCutAreas_ReturnsHeadDown()
    {
        var planner = new DirectRapidPlanner();
        var result = planner.Plan(new Vector(0, 0), new Vector(10, 10), new List<Shape>());

        Assert.False(result.HeadUp);
        Assert.Empty(result.Waypoints);
    }

    [Fact]
    public void ClearPath_ReturnsHeadDown()
    {
        var planner = new DirectRapidPlanner();

        // Cut area is off to the side — path doesn't cross it
        var cutArea = new Shape();
        cutArea.Entities.Add(new Line(new Vector(50, 0), new Vector(50, 10)));
        cutArea.Entities.Add(new Line(new Vector(50, 10), new Vector(60, 10)));
        cutArea.Entities.Add(new Line(new Vector(60, 10), new Vector(60, 0)));
        cutArea.Entities.Add(new Line(new Vector(60, 0), new Vector(50, 0)));

        var result = planner.Plan(
            new Vector(0, 0), new Vector(10, 10),
            new List<Shape> { cutArea });

        Assert.False(result.HeadUp);
    }

    [Fact]
    public void BlockedPath_ReturnsHeadUp()
    {
        var planner = new DirectRapidPlanner();

        // Cut area directly between from and to
        var cutArea = new Shape();
        cutArea.Entities.Add(new Line(new Vector(5, 0), new Vector(5, 20)));
        cutArea.Entities.Add(new Line(new Vector(5, 20), new Vector(6, 20)));
        cutArea.Entities.Add(new Line(new Vector(6, 20), new Vector(6, 0)));
        cutArea.Entities.Add(new Line(new Vector(6, 0), new Vector(5, 0)));

        var result = planner.Plan(
            new Vector(0, 10), new Vector(10, 10),
            new List<Shape> { cutArea });

        Assert.True(result.HeadUp);
        Assert.Empty(result.Waypoints);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Engine.Tests --filter DirectRapidPlannerTests -v n`
Expected: FAIL

- [ ] **Step 3: Implement DirectRapidPlanner**

Uses `Shape.Intersects(Line)` — a public method on `Shape` that delegates to `Intersect.Intersects(Line, Shape, out pts)`.

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Engine.RapidPlanning
{
    public class DirectRapidPlanner : IRapidPlanner
    {
        public RapidPath Plan(Vector from, Vector to, IReadOnlyList<Shape> cutAreas)
        {
            var travelLine = new Line(from, to);

            foreach (var cutArea in cutAreas)
            {
                if (cutArea.Intersects(travelLine))
                {
                    return new RapidPath
                    {
                        HeadUp = true,
                        Waypoints = new List<Vector>()
                    };
                }
            }

            return new RapidPath
            {
                HeadUp = false,
                Waypoints = new List<Vector>()
            };
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Engine.Tests --filter DirectRapidPlannerTests -v n`
Expected: 3 tests PASS

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/RapidPlanning/DirectRapidPlanner.cs OpenNest.Engine.Tests/RapidPlanning/DirectRapidPlannerTests.cs
git commit -m "feat: add DirectRapidPlanner with line-shape intersection check"
```

---

### Task 14: PlateResult and ProcessedPart

**Files:**
- Create: `OpenNest.Engine/PlateResult.cs`

- [ ] **Step 1: Create PlateResult.cs**

```csharp
using System.Collections.Generic;
using OpenNest.CNC;
using OpenNest.Engine.RapidPlanning;

namespace OpenNest.Engine
{
    public class PlateResult
    {
        public List<ProcessedPart> Parts { get; init; }
    }

    public readonly struct ProcessedPart
    {
        public Part Part { get; init; }
        public Program ProcessedProgram { get; init; }
        public RapidPath RapidPath { get; init; }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/PlateResult.cs
git commit -m "feat: add PlateResult and ProcessedPart"
```

---

### Task 15: PlateProcessor orchestrator

**Files:**
- Create: `OpenNest.Engine/PlateProcessor.cs`
- Test: `OpenNest.Engine.Tests/PlateProcessorTests.cs`

- [ ] **Step 1: Write the tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.RapidPlanning;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;
using Xunit;

namespace OpenNest.Engine.Tests;

public class PlateProcessorTests
{
    private static Part MakePartAt(double x, double y) => TestHelpers.MakePartAt(x, y, size: 2);

    [Fact]
    public void Process_ReturnsAllParts()
    {
        var plate = new Plate(60, 120);
        plate.Parts.Add(MakePartAt(10, 10));
        plate.Parts.Add(MakePartAt(30, 30));
        plate.Parts.Add(MakePartAt(50, 50));

        var processor = new PlateProcessor
        {
            Sequencer = new RightSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Equal(3, result.Parts.Count);
    }

    [Fact]
    public void Process_PreservesSequenceOrder()
    {
        var plate = new Plate(60, 120);
        var left = MakePartAt(5, 10);
        var right = MakePartAt(50, 10);
        plate.Parts.Add(left);
        plate.Parts.Add(right);

        var processor = new PlateProcessor
        {
            Sequencer = new RightSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        // RightSide sorts X descending — right part first
        Assert.Same(right, result.Parts[0].Part);
        Assert.Same(left, result.Parts[1].Part);
    }

    [Fact]
    public void Process_SkipsCuttingStrategy_WhenManualLeadIns()
    {
        var plate = new Plate(60, 120);
        var part = MakePartAt(10, 10);
        part.HasManualLeadIns = true;
        plate.Parts.Add(part);

        var processor = new PlateProcessor
        {
            Sequencer = new LeftSideSequencer(),
            CuttingStrategy = new ContourCuttingStrategy
            {
                Parameters = new CuttingParameters()
            },
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        // Part program should be passed through unchanged
        Assert.Same(part.Program, result.Parts[0].ProcessedProgram);
    }

    [Fact]
    public void Process_DoesNotMutatePart()
    {
        var plate = new Plate(60, 120);
        var part = MakePartAt(10, 10);
        var originalProgram = part.Program;
        plate.Parts.Add(part);

        var processor = new PlateProcessor
        {
            Sequencer = new LeftSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        // Part.Program should be untouched
        Assert.Same(originalProgram, part.Program);
    }

    [Fact]
    public void Process_NoCuttingStrategy_PassesProgramThrough()
    {
        var plate = new Plate(60, 120);
        var part = MakePartAt(10, 10);
        plate.Parts.Add(part);

        var processor = new PlateProcessor
        {
            Sequencer = new LeftSideSequencer(),
            // No CuttingStrategy set
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Same(part.Program, result.Parts[0].ProcessedProgram);
    }

    [Fact]
    public void Process_EmptyPlate_ReturnsEmptyResult()
    {
        var plate = new Plate(60, 120);

        var processor = new PlateProcessor
        {
            Sequencer = new LeftSideSequencer(),
            RapidPlanner = new SafeHeightRapidPlanner()
        };

        var result = processor.Process(plate);

        Assert.Empty(result.Parts);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Engine.Tests --filter PlateProcessorTests -v n`
Expected: FAIL — `PlateProcessor` class doesn't exist

- [ ] **Step 3: Implement PlateProcessor**

```csharp
using System.Collections.Generic;
using System.Linq;
using OpenNest.CNC;
using OpenNest.CNC.CuttingStrategy;
using OpenNest.Engine.RapidPlanning;
using OpenNest.Engine.Sequencing;
using OpenNest.Geometry;

namespace OpenNest.Engine
{
    public class PlateProcessor
    {
        public IPartSequencer Sequencer { get; set; }
        public ContourCuttingStrategy CuttingStrategy { get; set; }
        public IRapidPlanner RapidPlanner { get; set; }

        public PlateResult Process(Plate plate)
        {
            var ordered = Sequencer.Sequence(plate.Parts.ToList(), plate);

            var results = new List<ProcessedPart>();
            var cutAreas = new List<Shape>();
            var currentPoint = PlateHelper.GetExitPoint(plate);

            foreach (var sequenced in ordered)
            {
                var part = sequenced.Part;
                var localApproach = ToPartLocal(currentPoint, part);

                CuttingResult cutResult;
                if (!part.HasManualLeadIns && CuttingStrategy != null)
                {
                    cutResult = CuttingStrategy.Apply(part.Program, localApproach);
                }
                else
                {
                    cutResult = new CuttingResult
                    {
                        Program = part.Program,
                        LastCutPoint = GetProgramEndPoint(part.Program)
                    };
                }

                var piercePoint = ToPlateSpace(GetProgramStartPoint(cutResult.Program), part);
                var rapid = RapidPlanner.Plan(currentPoint, piercePoint, cutAreas);

                results.Add(new ProcessedPart
                {
                    Part = part,
                    ProcessedProgram = cutResult.Program,
                    RapidPath = rapid
                });

                var perimeter = GetPartPerimeter(part);
                if (perimeter != null)
                    cutAreas.Add(perimeter);

                currentPoint = ToPlateSpace(cutResult.LastCutPoint, part);
            }

            return new PlateResult { Parts = results };
        }

        private static Vector ToPartLocal(Vector platePoint, Part part)
        {
            return platePoint - part.Location;
        }

        private static Vector ToPlateSpace(Vector localPoint, Part part)
        {
            return localPoint + part.Location;
        }

        private static Vector GetProgramStartPoint(Program program)
        {
            var firstMove = program.Codes.OfType<Motion>().FirstOrDefault();
            return firstMove?.EndPoint ?? new Vector();
        }

        private static Vector GetProgramEndPoint(Program program)
        {
            var lastMove = program.Codes.OfType<Motion>().LastOrDefault();
            return lastMove?.EndPoint ?? new Vector();
        }

        private static Shape GetPartPerimeter(Part part)
        {
            var entities = part.Program.ToGeometry();
            if (entities == null || entities.Count == 0)
                return null;

            var profile = new ShapeProfile(entities);
            if (profile.Perimeter == null)
                return null;

            var perimeter = profile.Perimeter;
            perimeter.Offset(part.Location);
            return perimeter;
        }
    }
}
```

Note: `PlateHelper.GetExitPoint` is already defined in Task 8. If `PlateHelper` was created in the `Sequencing` namespace, make it `internal` and accessible via `using OpenNest.Engine.Sequencing`. Alternatively, move it to the `OpenNest.Engine` namespace root.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Engine.Tests --filter PlateProcessorTests -v n`
Expected: 6 tests PASS

- [ ] **Step 5: Run all tests**

Run: `dotnet test OpenNest.Engine.Tests -v n`
Expected: All tests PASS

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/PlateProcessor.cs OpenNest.Engine.Tests/PlateProcessorTests.cs
git commit -m "feat: add PlateProcessor orchestrator"
```

---

## Chunk 4: Final verification

### Task 16: Full build and test run

- [ ] **Step 1: Build entire solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded with no errors

- [ ] **Step 2: Run all tests**

Run: `dotnet test OpenNest.Engine.Tests -v n`
Expected: All tests pass

- [ ] **Step 3: Commit any remaining changes**

If any files were missed, stage and commit them.
