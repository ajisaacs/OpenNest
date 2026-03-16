# Remnant Finder Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract remnant detection from the nesting engine into a standalone `RemnantFinder` class that finds all rectangular empty regions via edge projection, and visualize the active work area on the plate view.

**Architecture:** `RemnantFinder` is a mutable class in `OpenNest.Engine` that takes a work area + obstacle boxes and uses edge projection to find empty rectangles. The remainder phase is removed from `DefaultNestEngine`, making `Fill()` single-pass. `FillScore` drops remnant tracking. `PlateView` gains a dashed orange rectangle overlay for the active work area. `NestProgress` carries `ActiveWorkArea` so callers can show which region is currently being filled.

**Tech Stack:** .NET 8, C#, xUnit, WinForms (GDI+)

**Spec:** `docs/superpowers/specs/2026-03-16-remnant-finder-design.md`

---

## Chunk 1: RemnantFinder Core

### Task 1: RemnantFinder — failing tests

**Files:**
- Create: `OpenNest.Tests/RemnantFinderTests.cs`

- [ ] **Step 1: Write failing tests for RemnantFinder**

```csharp
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class RemnantFinderTests
{
    [Fact]
    public void EmptyPlate_ReturnsWholeWorkArea()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        var remnants = finder.FindRemnants();

        Assert.Single(remnants);
        Assert.Equal(100 * 100, remnants[0].Area(), 0.1);
    }

    [Fact]
    public void SingleObstacle_InCorner_FindsLShapedRemnants()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 40, 40));
        var remnants = finder.FindRemnants();

        // Should find at least the right strip (60x100) and top strip (40x60)
        Assert.True(remnants.Count >= 2);

        // Largest remnant should be the right strip
        var largest = remnants[0];
        Assert.Equal(60 * 100, largest.Area(), 0.1);
    }

    [Fact]
    public void SingleObstacle_InCenter_FindsFourRemnants()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(30, 30, 40, 40));
        var remnants = finder.FindRemnants();

        // Should find remnants on all four sides
        Assert.True(remnants.Count >= 4);
    }

    [Fact]
    public void MinDimension_FiltersSmallRemnants()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        // Obstacle leaves a 5-wide strip on the right
        finder.AddObstacle(new Box(0, 0, 95, 100));
        var all = finder.FindRemnants(0);
        var filtered = finder.FindRemnants(10);

        Assert.True(all.Count > filtered.Count);
        foreach (var r in filtered)
        {
            Assert.True(r.Width >= 10);
            Assert.True(r.Length >= 10);
        }
    }

    [Fact]
    public void ResultsSortedByAreaDescending()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 50, 50));
        var remnants = finder.FindRemnants();

        for (var i = 1; i < remnants.Count; i++)
            Assert.True(remnants[i - 1].Area() >= remnants[i].Area());
    }

    [Fact]
    public void AddObstacle_UpdatesResults()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        var before = finder.FindRemnants();
        Assert.Single(before);

        finder.AddObstacle(new Box(0, 0, 50, 50));
        var after = finder.FindRemnants();
        Assert.True(after.Count > 1);
    }

    [Fact]
    public void ClearObstacles_ResetsToFullWorkArea()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 50, 50));
        finder.ClearObstacles();
        var remnants = finder.FindRemnants();

        Assert.Single(remnants);
        Assert.Equal(100 * 100, remnants[0].Area(), 0.1);
    }

    [Fact]
    public void FullyCovered_ReturnsEmpty()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        finder.AddObstacle(new Box(0, 0, 100, 100));
        var remnants = finder.FindRemnants();

        Assert.Empty(remnants);
    }

    [Fact]
    public void MultipleObstacles_FindsGapBetween()
    {
        var finder = new RemnantFinder(new Box(0, 0, 100, 100));
        // Two obstacles with a 20-wide gap in the middle
        finder.AddObstacle(new Box(0, 0, 40, 100));
        finder.AddObstacle(new Box(60, 0, 40, 100));
        var remnants = finder.FindRemnants();

        // Should find the 20x100 gap between the two obstacles
        var gap = remnants.FirstOrDefault(r =>
            r.Width >= 19.9 && r.Width <= 20.1 &&
            r.Length >= 99.9);
        Assert.NotNull(gap);
    }

    [Fact]
    public void FromPlate_CreatesFinderWithPartsAsObstacles()
    {
        var plate = TestHelpers.MakePlate(60, 120,
            TestHelpers.MakePartAt(0, 0, 20));
        var finder = RemnantFinder.FromPlate(plate);
        var remnants = finder.FindRemnants();

        // Should have remnants around the 20x20 part
        Assert.True(remnants.Count >= 1);
        // Largest remnant area should be less than full plate work area
        Assert.True(remnants[0].Area() < plate.WorkArea().Area());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~RemnantFinderTests" -v minimal`
Expected: FAIL — `RemnantFinder` class does not exist

- [ ] **Step 3: Commit failing tests**

```bash
git add OpenNest.Tests/RemnantFinderTests.cs
git commit -m "test: add RemnantFinder tests (red)"
```

### Task 2: RemnantFinder — implementation

**Files:**
- Create: `OpenNest.Engine/RemnantFinder.cs`

- [ ] **Step 1: Implement RemnantFinder**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using OpenNest.Geometry;

namespace OpenNest
{
    public class RemnantFinder
    {
        private readonly Box workArea;

        public List<Box> Obstacles { get; } = new();

        public RemnantFinder(Box workArea, List<Box> obstacles = null)
        {
            this.workArea = workArea;

            if (obstacles != null)
                Obstacles.AddRange(obstacles);
        }

        public void AddObstacle(Box obstacle) => Obstacles.Add(obstacle);

        public void AddObstacles(IEnumerable<Box> obstacles) => Obstacles.AddRange(obstacles);

        public void ClearObstacles() => Obstacles.Clear();

        public List<Box> FindRemnants(double minDimension = 0)
        {
            // Step 1-2: Collect unique X and Y coordinates
            var xs = new SortedSet<double> { workArea.Left, workArea.Right };
            var ys = new SortedSet<double> { workArea.Bottom, workArea.Top };

            foreach (var obs in Obstacles)
            {
                var clipped = ClipToWorkArea(obs);
                if (clipped.Width <= 0 || clipped.Length <= 0)
                    continue;

                xs.Add(clipped.Left);
                xs.Add(clipped.Right);
                ys.Add(clipped.Bottom);
                ys.Add(clipped.Top);
            }

            var xList = xs.ToList();
            var yList = ys.ToList();

            // Step 3-4: Build grid cells and mark empty ones
            var cols = xList.Count - 1;
            var rows = yList.Count - 1;

            if (cols <= 0 || rows <= 0)
                return new List<Box>();

            var empty = new bool[rows, cols];

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    var cell = new Box(xList[c], yList[r],
                        xList[c + 1] - xList[c], yList[r + 1] - yList[r]);

                    empty[r, c] = !OverlapsAnyObstacle(cell);
                }
            }

            // Step 5: Merge adjacent empty cells into larger rectangles
            var merged = MergeCells(empty, xList, yList, rows, cols);

            // Step 6: Filter by minDimension
            var results = new List<Box>();

            foreach (var box in merged)
            {
                if (box.Width >= minDimension && box.Length >= minDimension)
                    results.Add(box);
            }

            // Step 7: Sort by area descending
            results.Sort((a, b) => b.Area().CompareTo(a.Area()));
            return results;
        }

        public static RemnantFinder FromPlate(Plate plate)
        {
            var obstacles = new List<Box>(plate.Parts.Count);

            foreach (var part in plate.Parts)
                obstacles.Add(part.BoundingBox.Offset(plate.PartSpacing));

            return new RemnantFinder(plate.WorkArea(), obstacles);
        }

        private Box ClipToWorkArea(Box obs)
        {
            var left = System.Math.Max(obs.Left, workArea.Left);
            var bottom = System.Math.Max(obs.Bottom, workArea.Bottom);
            var right = System.Math.Min(obs.Right, workArea.Right);
            var top = System.Math.Min(obs.Top, workArea.Top);

            if (right <= left || top <= bottom)
                return Box.Empty;

            return new Box(left, bottom, right - left, top - bottom);
        }

        private bool OverlapsAnyObstacle(Box cell)
        {
            foreach (var obs in Obstacles)
            {
                var clipped = ClipToWorkArea(obs);

                if (clipped.Width <= 0 || clipped.Length <= 0)
                    continue;

                if (cell.Left < clipped.Right &&
                    cell.Right > clipped.Left &&
                    cell.Bottom < clipped.Top &&
                    cell.Top > clipped.Bottom)
                    return true;
            }

            return false;
        }

        private static List<Box> MergeCells(bool[,] empty, List<double> xList, List<double> yList, int rows, int cols)
        {
            var used = new bool[rows, cols];
            var results = new List<Box>();

            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    if (!empty[r, c] || used[r, c])
                        continue;

                    // Expand right as far as possible
                    var maxC = c;
                    while (maxC + 1 < cols && empty[r, maxC + 1] && !used[r, maxC + 1])
                        maxC++;

                    // Expand down as far as possible
                    var maxR = r;
                    while (maxR + 1 < rows)
                    {
                        var rowOk = true;
                        for (var cc = c; cc <= maxC; cc++)
                        {
                            if (!empty[maxR + 1, cc] || used[maxR + 1, cc])
                            {
                                rowOk = false;
                                break;
                            }
                        }

                        if (!rowOk) break;
                        maxR++;
                    }

                    // Mark cells as used
                    for (var rr = r; rr <= maxR; rr++)
                        for (var cc = c; cc <= maxC; cc++)
                            used[rr, cc] = true;

                    var box = new Box(
                        xList[c], yList[r],
                        xList[maxC + 1] - xList[c],
                        yList[maxR + 1] - yList[r]);

                    results.Add(box);
                }
            }

            return results;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~RemnantFinderTests" -v minimal`
Expected: All PASS

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/RemnantFinder.cs
git commit -m "feat: add RemnantFinder with edge projection algorithm"
```

---

## Chunk 2: FillScore Simplification and Remnant Cleanup

### Task 3: Simplify FillScore — remove remnant tracking

**Files:**
- Modify: `OpenNest.Engine/FillScore.cs`

- [ ] **Step 1: Remove remnant-related members from FillScore**

Remove `MinRemnantDimension`, `UsableRemnantArea`, `ComputeUsableRemnantArea()`. Simplify constructor and `Compute()`. Update `CompareTo` to compare count then density (no remnant area).

New `FillScore.cs`:

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest
{
    public readonly struct FillScore : System.IComparable<FillScore>
    {
        public int Count { get; }

        /// <summary>
        /// Total part area / bounding box area of all placed parts.
        /// </summary>
        public double Density { get; }

        public FillScore(int count, double density)
        {
            Count = count;
            Density = density;
        }

        /// <summary>
        /// Computes a fill score from placed parts and the work area they were placed in.
        /// </summary>
        public static FillScore Compute(List<Part> parts, Box workArea)
        {
            if (parts == null || parts.Count == 0)
                return default;

            var totalPartArea = 0.0;
            var minX = double.MaxValue;
            var minY = double.MaxValue;
            var maxX = double.MinValue;
            var maxY = double.MinValue;

            foreach (var part in parts)
            {
                totalPartArea += part.BaseDrawing.Area;
                var bb = part.BoundingBox;

                if (bb.Left < minX) minX = bb.Left;
                if (bb.Bottom < minY) minY = bb.Bottom;
                if (bb.Right > maxX) maxX = bb.Right;
                if (bb.Top > maxY) maxY = bb.Top;
            }

            var bboxArea = (maxX - minX) * (maxY - minY);
            var density = bboxArea > 0 ? totalPartArea / bboxArea : 0;

            return new FillScore(parts.Count, density);
        }

        /// <summary>
        /// Lexicographic comparison: count, then density.
        /// </summary>
        public int CompareTo(FillScore other)
        {
            var c = Count.CompareTo(other.Count);

            if (c != 0)
                return c;

            return Density.CompareTo(other.Density);
        }

        public static bool operator >(FillScore a, FillScore b) => a.CompareTo(b) > 0;
        public static bool operator <(FillScore a, FillScore b) => a.CompareTo(b) < 0;
        public static bool operator >=(FillScore a, FillScore b) => a.CompareTo(b) >= 0;
        public static bool operator <=(FillScore a, FillScore b) => a.CompareTo(b) <= 0;
    }
}
```

- [ ] **Step 2: Commit (build will not pass yet — remaining UsableRemnantArea references fixed in Tasks 4-5)**

```bash
git add OpenNest.Engine/FillScore.cs
git commit -m "refactor: simplify FillScore to count + density, remove remnant tracking"
```

### Task 4: Update DefaultNestEngine debug logging

**Files:**
- Modify: `OpenNest.Engine/DefaultNestEngine.cs:456-459`

- [ ] **Step 1: Update FillWithPairs debug log**

At line 456, change:
```csharp
Debug.WriteLine($"[FillWithPairs] Best pair result: {bestScore.Count} parts, remnant={bestScore.UsableRemnantArea:F1}, density={bestScore.Density:P1}");
```
to:
```csharp
Debug.WriteLine($"[FillWithPairs] Best pair result: {bestScore.Count} parts, density={bestScore.Density:P1}");
```

Also update the file-based debug log at lines 457-459 — change `bestScore.UsableRemnantArea` references similarly. If the file log references `UsableRemnantArea`, remove that interpolation.

- [ ] **Step 2: Commit**

```bash
git add OpenNest.Engine/DefaultNestEngine.cs
git commit -m "fix: update FillWithPairs debug logging after FillScore simplification"
```

### Task 5: Remove NestProgress.UsableRemnantArea and UI references

**Files:**
- Modify: `OpenNest.Engine/NestProgress.cs:44`
- Modify: `OpenNest.Engine/NestEngineBase.cs:232`
- Modify: `OpenNest\Forms\NestProgressForm.cs:40`

- [ ] **Step 1: Remove UsableRemnantArea from NestProgress**

In `NestProgress.cs`, remove line 44:
```csharp
public double UsableRemnantArea { get; set; }
```

- [ ] **Step 2: Remove UsableRemnantArea from ReportProgress**

In `NestEngineBase.cs` at line 232, remove:
```csharp
UsableRemnantArea = workArea.Area() - totalPartArea,
```

- [ ] **Step 3: Remove remnant display from NestProgressForm**

In `NestProgressForm.cs` at line 40, remove:
```csharp
remnantValue.Text = $"{progress.UsableRemnantArea:F1} sq in";
```

Also remove the `remnantValue` label and its corresponding "Remnant:" label from the form's Designer file (or set them to display something else if desired). If simpler, just remove the line that sets the text — the label will remain but show its default empty text.

- [ ] **Step 4: Build to verify all UsableRemnantArea references are resolved**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds — all `UsableRemnantArea` references are now removed

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/NestProgress.cs OpenNest.Engine/NestEngineBase.cs OpenNest/Forms/NestProgressForm.cs
git commit -m "refactor: remove UsableRemnantArea from NestProgress and UI"
```

---

## Chunk 3: Remove Remainder Phase from Engine

### Task 6: Remove remainder phase from DefaultNestEngine

**Files:**
- Modify: `OpenNest.Engine/DefaultNestEngine.cs`

- [ ] **Step 1: Remove TryRemainderImprovement calls from Fill() overrides**

In the first `Fill()` override (line 31), remove lines 40-53 (the remainder improvement block after `FindBestFill`):
```csharp
// Remove this entire block:
if (!token.IsCancellationRequested)
{
    var remainderSw = Stopwatch.StartNew();
    var improved = TryRemainderImprovement(item, workArea, best);
    // ... through to the closing brace
}
```

In the second `Fill()` override (line 118), remove lines 165-174 (the remainder improvement block inside the `if (groupParts.Count == 1)` block):
```csharp
// Remove this entire block:
var improved = TryRemainderImprovement(nestItem, workArea, best);
if (IsBetterFill(improved, best, workArea))
{
    // ...
}
```

- [ ] **Step 2: Remove TryRemainderImprovement, TryStripRefill, ClusterParts methods**

Remove the three private methods (lines 563-694):
- `TryRemainderImprovement`
- `TryStripRefill`
- `ClusterParts`

- [ ] **Step 3: Update Description property**

Change:
```csharp
public override string Description => "Multi-phase nesting (Linear, Pairs, RectBestFit, Remainder)";
```
to:
```csharp
public override string Description => "Multi-phase nesting (Linear, Pairs, RectBestFit)";
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet build OpenNest.sln && dotnet test OpenNest.Tests -v minimal`
Expected: Build succeeds, tests pass

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/DefaultNestEngine.cs
git commit -m "refactor: remove remainder phase from DefaultNestEngine"
```

### Task 7: Remove NestPhase.Remainder and cleanup

**Files:**
- Modify: `OpenNest.Engine/NestProgress.cs:11`
- Modify: `OpenNest.Engine/NestEngineBase.cs:314`
- Modify: `OpenNest\Forms\NestProgressForm.cs:100`

- [ ] **Step 1: Remove Remainder from NestPhase enum**

In `NestProgress.cs`, remove `Remainder` from the enum.

- [ ] **Step 2: Remove Remainder case from FormatPhaseName**

In `NestEngineBase.cs`, remove:
```csharp
case NestPhase.Remainder: return "Remainder";
```

- [ ] **Step 3: Remove Remainder case from FormatPhase**

In `NestProgressForm.cs`, remove:
```csharp
case NestPhase.Remainder: return "Filling remainder...";
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: No errors

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/NestProgress.cs OpenNest.Engine/NestEngineBase.cs OpenNest/Forms/NestProgressForm.cs
git commit -m "refactor: remove NestPhase.Remainder enum value and switch cases"
```

### Task 8: Remove ComputeRemainderWithin and update Nest()

**Files:**
- Modify: `OpenNest.Engine/NestEngineBase.cs:92,120-133`

- [ ] **Step 1: Replace ComputeRemainderWithin usage in Nest()**

At line 91-92, change:
```csharp
var placedBox = parts.Cast<IBoundable>().GetBoundingBox();
workArea = ComputeRemainderWithin(workArea, placedBox, Plate.PartSpacing);
```
to:
```csharp
var placedObstacles = parts.Select(p => p.BoundingBox.Offset(Plate.PartSpacing)).ToList();
var finder = new RemnantFinder(workArea, placedObstacles);
var remnants = finder.FindRemnants();
if (remnants.Count == 0)
    break;
workArea = remnants[0]; // Largest remnant
```

Note: This is a behavioral improvement — the old code used a single merged bounding box and picked one strip. The new code finds per-part obstacles and discovers all gaps, using the largest. This may produce different (better) results for non-rectangular layouts.

- [ ] **Step 2: Remove ComputeRemainderWithin method**

Delete lines 120-133 (the `ComputeRemainderWithin` static method).

- [ ] **Step 3: Build and run tests**

Run: `dotnet build OpenNest.sln && dotnet test OpenNest.Tests -v minimal`
Expected: Build succeeds, tests pass

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine/NestEngineBase.cs
git commit -m "refactor: replace ComputeRemainderWithin with RemnantFinder in Nest()"
```

---

## Chunk 4: Remove Old Remnant Code and Update Callers

### Task 9: Remove Plate.GetRemnants()

**Files:**
- Modify: `OpenNest.Core/Plate.cs:477-557`

- [ ] **Step 1: Remove GetRemnants method**

Delete the `GetRemnants()` method (lines 477-557, the XML doc comment through the closing brace).

- [ ] **Step 2: Build to check for remaining references**

Run: `dotnet build OpenNest.sln`
Expected: Errors in `NestingTools.cs` and `InspectionTools.cs` (fixed in next task)

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Core/Plate.cs
git commit -m "refactor: remove Plate.GetRemnants(), replaced by RemnantFinder"
```

### Task 10: Update MCP callers

**Files:**
- Modify: `OpenNest.Mcp/Tools/NestingTools.cs:105`
- Modify: `OpenNest.Mcp/Tools/InspectionTools.cs:31`

- [ ] **Step 1: Update NestingTools.FillRemnants**

At line 105, change:
```csharp
var remnants = plate.GetRemnants();
```
to:
```csharp
var finder = RemnantFinder.FromPlate(plate);
var remnants = finder.FindRemnants();
```

- [ ] **Step 2: Update InspectionTools.GetPlateInfo**

At line 31, change:
```csharp
var remnants = plate.GetRemnants();
```
to:
```csharp
var remnants = RemnantFinder.FromPlate(plate).FindRemnants();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Mcp/Tools/NestingTools.cs OpenNest.Mcp/Tools/InspectionTools.cs
git commit -m "refactor: update MCP tools to use RemnantFinder"
```

### Task 11: Remove StripNestResult.RemnantBox

**Files:**
- Modify: `OpenNest.Engine/StripNestResult.cs:10`
- Modify: `OpenNest.Engine/StripNestEngine.cs:301`

- [ ] **Step 1: Remove RemnantBox property from StripNestResult**

In `StripNestResult.cs`, remove line 10:
```csharp
public Box RemnantBox { get; set; }
```

- [ ] **Step 2: Remove RemnantBox assignment in StripNestEngine**

In `StripNestEngine.cs` at line 301, remove:
```csharp
result.RemnantBox = remnantBox;
```

Also check if the local `remnantBox` variable is now unused — if so, remove its declaration and computation too.

- [ ] **Step 3: Build and run tests**

Run: `dotnet build OpenNest.sln && dotnet test OpenNest.Tests -v minimal`
Expected: Build succeeds, all tests pass

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine/StripNestResult.cs OpenNest.Engine/StripNestEngine.cs
git commit -m "refactor: remove StripNestResult.RemnantBox"
```

---

## Chunk 5: PlateView Active Work Area Visualization

### Task 12: Add ActiveWorkArea to NestProgress

**Files:**
- Modify: `OpenNest.Engine/NestProgress.cs`

- [ ] **Step 1: Add ActiveWorkArea property to NestProgress**

`Box` is a reference type (class), so use `Box` directly (not `Box?`):

```csharp
public Box ActiveWorkArea { get; set; }
```

`NestProgress.cs` already has `using OpenNest.Geometry;` via the `Box` usage in existing properties. If not, add it.

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.Engine`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestProgress.cs
git commit -m "feat: add ActiveWorkArea property to NestProgress"
```

### Task 13: Draw active work area on PlateView

**Files:**
- Modify: `OpenNest\Controls\PlateView.cs`

- [ ] **Step 1: Add ActiveWorkArea property**

Add a field and property to `PlateView`:
```csharp
private Box activeWorkArea;

public Box ActiveWorkArea
{
    get => activeWorkArea;
    set
    {
        activeWorkArea = value;
        Invalidate();
    }
}
```

- [ ] **Step 2: Add DrawActiveWorkArea method**

Add a private method to draw the dashed orange rectangle, using the same coordinate transform pattern as `DrawBox` (line 591-601):

```csharp
private void DrawActiveWorkArea(Graphics g)
{
    if (activeWorkArea == null)
        return;

    var rect = new RectangleF
    {
        Location = PointWorldToGraph(activeWorkArea.Location),
        Width = LengthWorldToGui(activeWorkArea.Width),
        Height = LengthWorldToGui(activeWorkArea.Length)
    };
    rect.Y -= rect.Height;

    using var pen = new Pen(Color.Orange, 2f)
    {
        DashStyle = DashStyle.Dash
    };
    g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
}
```

- [ ] **Step 3: Call DrawActiveWorkArea in OnPaint**

In `OnPaint` (line 363-364), add the call after `DrawParts`:
```csharp
DrawPlate(e.Graphics);
DrawParts(e.Graphics);
DrawActiveWorkArea(e.Graphics);
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add OpenNest/Controls/PlateView.cs
git commit -m "feat: draw active work area as dashed orange rectangle on PlateView"
```

### Task 14: Wire ActiveWorkArea through progress callbacks

**Files:**
- Modify: `OpenNest\Controls\PlateView.cs:828-829`
- Modify: `OpenNest\Forms\MainForm.cs:760-761,895-896,955-956`

The `PlateView` and `MainForm` both have progress callbacks that already set `SetTemporaryParts`. Add `ActiveWorkArea` alongside those.

- [ ] **Step 1: Update PlateView.FillWithProgress callback**

At `PlateView.cs` line 828-829, the callback currently does:
```csharp
progressForm.UpdateProgress(p);
SetTemporaryParts(p.BestParts);
```

Add after `SetTemporaryParts`:
```csharp
ActiveWorkArea = p.ActiveWorkArea;
```

- [ ] **Step 2: Update MainForm progress callbacks**

There are three progress callback sites in `MainForm.cs`. At each one, after the `SetTemporaryParts` call, add:

At line 761 (after `activeForm.PlateView.SetTemporaryParts(p.BestParts);`):
```csharp
activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
```

At line 896 (after `activeForm.PlateView.SetTemporaryParts(p.BestParts);`):
```csharp
activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
```

At line 956 (after `activeForm.PlateView.SetTemporaryParts(p.BestParts);`):
```csharp
activeForm.PlateView.ActiveWorkArea = p.ActiveWorkArea;
```

- [ ] **Step 3: Clear ActiveWorkArea when nesting completes**

In each nesting method's completion/cleanup path, clear the work area overlay. In `PlateView.cs` after the fill task completes (near `progressForm.ShowCompleted()`), add:
```csharp
ActiveWorkArea = null;
```

Similarly in each `MainForm` nesting method's completion path:
```csharp
activeForm.PlateView.ActiveWorkArea = null;
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add OpenNest/Controls/PlateView.cs OpenNest/Forms/MainForm.cs
git commit -m "feat: wire ActiveWorkArea from NestProgress to PlateView"
```

### Task 15: Set ActiveWorkArea in Nest() method

**Files:**
- Modify: `OpenNest.Engine/NestEngineBase.cs` (the `Nest()` method updated in Task 8)

- [ ] **Step 1: Report ActiveWorkArea in Nest() progress**

In the `Nest()` method, after picking the largest remnant as the next work area (Task 8's change), set `ActiveWorkArea` on the progress report. Find the `ReportProgress` call inside or near the fill loop and ensure the progress object carries the current `workArea`.

The simplest approach: pass the work area through `ReportProgress`. In `NestEngineBase.ReportProgress` (the static helper), add `ActiveWorkArea = workArea` to the `NestProgress` initializer:

In `ReportProgress`, add to the `new NestProgress { ... }` block:
```csharp
ActiveWorkArea = workArea,
```

This ensures every progress report includes the current work area being filled.

- [ ] **Step 2: Build and run tests**

Run: `dotnet build OpenNest.sln && dotnet test OpenNest.Tests -v minimal`
Expected: Build succeeds, all tests pass

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/NestEngineBase.cs
git commit -m "feat: report ActiveWorkArea in NestProgress from ReportProgress"
```

---

## Chunk 6: Final Verification

### Task 16: Full build and test

**Files:** None (verification only)

- [ ] **Step 1: Run full build**

Run: `dotnet build OpenNest.sln`
Expected: 0 errors, 0 warnings related to remnant code

- [ ] **Step 2: Run all tests**

Run: `dotnet test OpenNest.Tests -v minimal`
Expected: All tests pass including new `RemnantFinderTests`

- [ ] **Step 3: Verify no stale references**

Run: `grep -rn "GetRemnants\|ComputeRemainderWithin\|TryRemainderImprovement\|MinRemnantDimension\|UsableRemnantArea" --include="*.cs" .`
Expected: No matches in source files (only in docs/specs/plans)

- [ ] **Step 4: Final commit if any fixups needed**

```bash
git add -A
git commit -m "chore: final cleanup after remnant finder extraction"
```
