# Iterative Shrink-Fill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `StripNestEngine`'s single-strip approach with iterative shrink-fill — every multi-quantity drawing gets shrink-fitted into its tightest sub-region using dual-direction selection, with leftovers packed at the end.

**Architecture:** New `IterativeShrinkFiller` static class composes existing `RemnantFiller` + `ShrinkFiller` with a dual-direction wrapper closure. `StripNestEngine.Nest` becomes a thin orchestrator calling the new filler then packing leftovers. No changes to `NestEngineBase`, `DefaultNestEngine`, or UI.

**Tech Stack:** .NET 8, xUnit, OpenNest.Engine

**Spec:** `docs/superpowers/specs/2026-03-19-iterative-shrink-fill-design.md`

---

### File Structure

| File | Responsibility |
|------|---------------|
| `OpenNest.Engine/Fill/IterativeShrinkFiller.cs` | **New.** Static class + result type. Wraps a raw fill function with dual-direction `ShrinkFiller.Shrink`, passes the wrapper to `RemnantFiller.FillItems`. Returns placed parts + leftover items. |
| `OpenNest.Engine/StripNestEngine.cs` | **Modify.** Rewrite `Nest` to separate items, call `IterativeShrinkFiller.Fill`, pack leftovers. Delete `SelectStripItemIndex`, `EstimateStripDimension`, `TryOrientation`, `ShrinkFill`. |
| `OpenNest.Engine/StripNestResult.cs` | **Delete.** No longer needed. |
| `OpenNest.Engine/StripDirection.cs` | **Delete.** No longer needed. |
| `OpenNest.Tests/IterativeShrinkFillerTests.cs` | **New.** Unit tests for the new filler. |
| `OpenNest.Tests/EngineRefactorSmokeTests.cs` | **Verify.** Existing `StripEngine_Nest_ProducesResults` must still pass. |

---

### Task 1: IterativeShrinkFiller — empty/null input

**Files:**
- Create: `OpenNest.Tests/IterativeShrinkFillerTests.cs`
- Create: `OpenNest.Engine/Fill/IterativeShrinkFiller.cs`

- [ ] **Step 1: Write failing tests for empty/null input**

```csharp
using OpenNest.Engine.Fill;
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class IterativeShrinkFillerTests
{
    [Fact]
    public void Fill_NullItems_ReturnsEmpty()
    {
        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) => new List<Part>();
        var result = IterativeShrinkFiller.Fill(null, new Box(0, 0, 100, 100), fillFunc, 1.0);

        Assert.Empty(result.Parts);
        Assert.Empty(result.Leftovers);
    }

    [Fact]
    public void Fill_EmptyItems_ReturnsEmpty()
    {
        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) => new List<Part>();
        var result = IterativeShrinkFiller.Fill(new List<NestItem>(), new Box(0, 0, 100, 100), fillFunc, 1.0);

        Assert.Empty(result.Parts);
        Assert.Empty(result.Leftovers);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~IterativeShrinkFillerTests" --no-build 2>&1 || dotnet test OpenNest.Tests --filter "FullyQualifiedName~IterativeShrinkFillerTests"`
Expected: Build error — `IterativeShrinkFiller` does not exist yet.

- [ ] **Step 3: Write minimal implementation**

Create `OpenNest.Engine/Fill/IterativeShrinkFiller.cs`:

```csharp
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenNest.Engine.Fill
{
    public class IterativeShrinkResult
    {
        public List<Part> Parts { get; set; } = new();
        public List<NestItem> Leftovers { get; set; } = new();
    }

    public static class IterativeShrinkFiller
    {
        public static IterativeShrinkResult Fill(
            List<NestItem> items,
            Box workArea,
            Func<NestItem, Box, List<Part>> fillFunc,
            double spacing,
            CancellationToken token = default)
        {
            if (items == null || items.Count == 0)
                return new IterativeShrinkResult();

            // TODO: dual-direction shrink logic
            return new IterativeShrinkResult();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~IterativeShrinkFillerTests"`
Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Tests/IterativeShrinkFillerTests.cs OpenNest.Engine/Fill/IterativeShrinkFiller.cs
git commit -m "feat(engine): add IterativeShrinkFiller skeleton with empty/null tests"
```

---

### Task 2: IterativeShrinkFiller — dual-direction shrink core logic

**Files:**
- Modify: `OpenNest.Engine/Fill/IterativeShrinkFiller.cs`
- Modify: `OpenNest.Tests/IterativeShrinkFillerTests.cs`

**Context:** The core algorithm wraps the caller's `fillFunc` in a closure that calls `ShrinkFiller.Shrink` in both axis directions and picks the better `FillScore`, then passes this wrapper to `RemnantFiller.FillItems`.

- [ ] **Step 1: Write failing test — single item gets shrink-filled**

Add to `IterativeShrinkFillerTests.cs`:

```csharp
private static Drawing MakeRectDrawing(double w, double h, string name = "rect")
{
    var pgm = new OpenNest.CNC.Program();
    pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
    pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, 0)));
    pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(w, h)));
    pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, h)));
    pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
    return new Drawing(name, pgm);
}

[Fact]
public void Fill_SingleItem_PlacesParts()
{
    var drawing = MakeRectDrawing(20, 10);
    var items = new List<NestItem>
    {
        new NestItem { Drawing = drawing, Quantity = 5 }
    };

    Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
    {
        var plate = new Plate(b.Width, b.Length);
        var engine = new DefaultNestEngine(plate);
        return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
    };

    var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 120, 60), fillFunc, 1.0);

    Assert.True(result.Parts.Count > 0, "Should place parts");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~IterativeShrinkFillerTests.Fill_SingleItem_PlacesParts"`
Expected: FAIL — returns 0 parts (skeleton returns empty).

- [ ] **Step 3: Implement dual-direction shrink logic**

Replace the TODO in `IterativeShrinkFiller.Fill`:

```csharp
public static IterativeShrinkResult Fill(
    List<NestItem> items,
    Box workArea,
    Func<NestItem, Box, List<Part>> fillFunc,
    double spacing,
    CancellationToken token = default)
{
    if (items == null || items.Count == 0)
        return new IterativeShrinkResult();

    // RemnantFiller.FillItems skips items with Quantity <= 0 (its localQty
    // check treats them as "done"). Convert unlimited items to an estimated
    // max capacity so they are actually processed.
    var workItems = new List<NestItem>(items.Count);
    var unlimitedDrawings = new HashSet<string>();

    foreach (var item in items)
    {
        if (item.Quantity <= 0)
        {
            var bbox = item.Drawing.Program.BoundingBox();
            var estimatedMax = bbox.Area() > 0
                ? (int)(workArea.Area() / bbox.Area()) * 2
                : 1000;

            unlimitedDrawings.Add(item.Drawing.Name);
            workItems.Add(new NestItem
            {
                Drawing = item.Drawing,
                Quantity = System.Math.Max(1, estimatedMax),
                Priority = item.Priority,
                StepAngle = item.StepAngle,
                RotationStart = item.RotationStart,
                RotationEnd = item.RotationEnd
            });
        }
        else
        {
            workItems.Add(item);
        }
    }

    var filler = new RemnantFiller(workArea, spacing);

    Func<NestItem, Box, List<Part>> shrinkWrapper = (ni, box) =>
    {
        var heightResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Height, token);
        var widthResult = ShrinkFiller.Shrink(fillFunc, ni, box, spacing, ShrinkAxis.Width, token);

        var heightScore = FillScore.Compute(heightResult.Parts, box);
        var widthScore = FillScore.Compute(widthResult.Parts, box);

        return widthScore > heightScore ? widthResult.Parts : heightResult.Parts;
    };

    var placed = filler.FillItems(workItems, shrinkWrapper, token);

    // Build leftovers: compare placed count to original quantities.
    // RemnantFiller.FillItems does NOT mutate NestItem.Quantity.
    var leftovers = new List<NestItem>();
    foreach (var item in items)
    {
        var placedCount = placed.Count(p => p.BaseDrawing.Name == item.Drawing.Name);

        if (item.Quantity <= 0)
            continue; // unlimited items are always "satisfied" — no leftover

        var remaining = item.Quantity - placedCount;
        if (remaining > 0)
        {
            leftovers.Add(new NestItem
            {
                Drawing = item.Drawing,
                Quantity = remaining,
                Priority = item.Priority,
                StepAngle = item.StepAngle,
                RotationStart = item.RotationStart,
                RotationEnd = item.RotationEnd
            });
        }
    }

    return new IterativeShrinkResult { Parts = placed, Leftovers = leftovers };
}
```

**Key points:**
- `RemnantFiller.FillItems` skips items with `Quantity <= 0` (its `localQty` check treats them as done). To work around this without modifying `RemnantFiller`, unlimited items are converted to an estimated max capacity (`workArea / bboxArea * 2`) before being passed in.
- `RemnantFiller.FillItems` does NOT mutate `NestItem.Quantity` — it tracks quantities internally via `localQty` dictionary (verified in `RemnantFillerTests2.FillItems_DoesNotMutateItemQuantities`).
- The leftover calculation iterates the *original* items list (not `workItems`), so unlimited items are correctly skipped.
- `FillScore` comparison: `widthScore > heightScore` uses the operator overload which is lexicographic (count first, then density).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~IterativeShrinkFillerTests"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/Fill/IterativeShrinkFiller.cs OpenNest.Tests/IterativeShrinkFillerTests.cs
git commit -m "feat(engine): implement dual-direction shrink logic in IterativeShrinkFiller"
```

---

### Task 3: IterativeShrinkFiller — multiple items and leftovers

**Files:**
- Modify: `OpenNest.Tests/IterativeShrinkFillerTests.cs`

- [ ] **Step 1: Write failing tests for multi-item and leftover scenarios**

Add to `IterativeShrinkFillerTests.cs`:

```csharp
[Fact]
public void Fill_MultipleItems_PlacesFromBoth()
{
    var items = new List<NestItem>
    {
        new NestItem { Drawing = MakeRectDrawing(20, 10, "large"), Quantity = 5 },
        new NestItem { Drawing = MakeRectDrawing(8, 5, "small"), Quantity = 5 },
    };

    Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
    {
        var plate = new Plate(b.Width, b.Length);
        var engine = new DefaultNestEngine(plate);
        return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
    };

    var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 120, 60), fillFunc, 1.0);

    var largeCount = result.Parts.Count(p => p.BaseDrawing.Name == "large");
    var smallCount = result.Parts.Count(p => p.BaseDrawing.Name == "small");

    Assert.True(largeCount > 0, "Should place large parts");
    Assert.True(smallCount > 0, "Should place small parts in remaining space");
}

[Fact]
public void Fill_UnfilledQuantity_ReturnsLeftovers()
{
    // Huge quantity that can't all fit on a small plate
    var items = new List<NestItem>
    {
        new NestItem { Drawing = MakeRectDrawing(20, 10), Quantity = 1000 },
    };

    Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
    {
        var plate = new Plate(b.Width, b.Length);
        var engine = new DefaultNestEngine(plate);
        return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
    };

    var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 60, 30), fillFunc, 1.0);

    Assert.True(result.Parts.Count > 0, "Should place some parts");
    Assert.True(result.Leftovers.Count > 0, "Should have leftovers");
    Assert.True(result.Leftovers[0].Quantity > 0, "Leftover quantity should be positive");
}

[Fact]
public void Fill_UnlimitedQuantity_PlacesParts()
{
    var items = new List<NestItem>
    {
        new NestItem { Drawing = MakeRectDrawing(20, 10), Quantity = 0 }
    };

    Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
    {
        var plate = new Plate(b.Width, b.Length);
        var engine = new DefaultNestEngine(plate);
        return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
    };

    var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 120, 60), fillFunc, 1.0);

    Assert.True(result.Parts.Count > 0, "Unlimited qty items should still be placed");
    Assert.Empty(result.Leftovers); // unlimited items never produce leftovers
}

[Fact]
public void Fill_RespectsCancellation()
{
    var cts = new System.Threading.CancellationTokenSource();
    cts.Cancel();

    var items = new List<NestItem>
    {
        new NestItem { Drawing = MakeRectDrawing(20, 10), Quantity = 10 }
    };

    Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };

    var result = IterativeShrinkFiller.Fill(items, new Box(0, 0, 100, 100), fillFunc, 1.0, cts.Token);

    Assert.NotNull(result);
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~IterativeShrinkFillerTests"`
Expected: All 7 tests pass (these test existing behavior, no new code needed — they verify the implementation from Task 2 handles these cases).

If any fail, fix the implementation in `IterativeShrinkFiller.Fill` and re-run.

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Tests/IterativeShrinkFillerTests.cs
git commit -m "test(engine): add multi-item, leftover, unlimited qty, and cancellation tests for IterativeShrinkFiller"
```

---

### Task 4: Rewrite StripNestEngine.Nest

**Files:**
- Modify: `OpenNest.Engine/StripNestEngine.cs`

**Context:** Replace the current `Nest` implementation that does single-strip + remnant fill with the new iterative approach. Keep `Fill`, `Fill(groupParts)`, and `PackArea` overrides unchanged — they still delegate to `DefaultNestEngine`.

- [ ] **Step 1: Run existing smoke test to establish baseline**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~StripEngine_Nest_ProducesResults"`
Expected: PASS (current implementation works).

- [ ] **Step 2: Rewrite StripNestEngine.Nest**

Replace the `Nest` override and delete `SelectStripItemIndex`, `EstimateStripDimension`, `TryOrientation`, and `ShrinkFill` methods. The full file should become:

```csharp
using OpenNest.Engine.Fill;
using OpenNest.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace OpenNest
{
    public class StripNestEngine : NestEngineBase
    {
        public StripNestEngine(Plate plate) : base(plate)
        {
        }

        public override string Name => "Strip";

        public override string Description => "Iterative shrink-fill nesting for mixed-drawing layouts";

        /// <summary>
        /// Single-item fill delegates to DefaultNestEngine.
        /// </summary>
        public override List<Part> Fill(NestItem item, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.Fill(item, workArea, progress, token);
        }

        /// <summary>
        /// Group-parts fill delegates to DefaultNestEngine.
        /// </summary>
        public override List<Part> Fill(List<Part> groupParts, Box workArea,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.Fill(groupParts, workArea, progress, token);
        }

        /// <summary>
        /// Pack delegates to DefaultNestEngine.
        /// </summary>
        public override List<Part> PackArea(Box box, List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            var inner = new DefaultNestEngine(Plate);
            return inner.PackArea(box, items, progress, token);
        }

        /// <summary>
        /// Multi-drawing iterative shrink-fill strategy.
        /// Each multi-quantity drawing gets shrink-filled into the tightest
        /// sub-region using dual-direction selection. Singles and leftovers
        /// are packed at the end.
        /// </summary>
        public override List<Part> Nest(List<NestItem> items,
            IProgress<NestProgress> progress, CancellationToken token)
        {
            if (items == null || items.Count == 0)
                return new List<Part>();

            var workArea = Plate.WorkArea();

            // Separate multi-quantity from singles.
            var fillItems = items
                .Where(i => i.Quantity != 1)
                .OrderBy(i => i.Priority)
                .ThenByDescending(i => i.Drawing.Area)
                .ToList();

            var packItems = items
                .Where(i => i.Quantity == 1)
                .ToList();

            var allParts = new List<Part>();

            // Phase 1: Iterative shrink-fill for multi-quantity items.
            if (fillItems.Count > 0)
            {
                Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
                {
                    var inner = new DefaultNestEngine(Plate);
                    return inner.Fill(ni, b, progress, token);
                };

                var shrinkResult = IterativeShrinkFiller.Fill(
                    fillItems, workArea, fillFunc, Plate.PartSpacing, token);

                allParts.AddRange(shrinkResult.Parts);

                // Add unfilled items to pack list.
                packItems.AddRange(shrinkResult.Leftovers);
            }

            // Phase 2: Pack singles + leftovers into remaining space.
            packItems = packItems.Where(i => i.Quantity > 0).ToList();

            if (packItems.Count > 0 && !token.IsCancellationRequested)
            {
                // Reconstruct remaining area from placed parts.
                var packArea = workArea;
                if (allParts.Count > 0)
                {
                    var obstacles = allParts
                        .Select(p => p.BoundingBox.Offset(Plate.PartSpacing))
                        .ToList();
                    var finder = new RemnantFinder(workArea, obstacles);
                    var remnants = finder.FindRemnants();
                    packArea = remnants.Count > 0 ? remnants[0] : new Box(0, 0, 0, 0);
                }

                if (packArea.Width > 0 && packArea.Length > 0)
                {
                    var packParts = PackArea(packArea, packItems, progress, token);
                    allParts.AddRange(packParts);
                }
            }

            // Deduct placed quantities from original items.
            foreach (var item in items)
            {
                if (item.Quantity <= 0)
                    continue;

                var placed = allParts.Count(p => p.BaseDrawing.Name == item.Drawing.Name);
                item.Quantity = System.Math.Max(0, item.Quantity - placed);
            }

            return allParts;
        }
    }
}
```

- [ ] **Step 3: Run smoke test**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~StripEngine_Nest_ProducesResults"`
Expected: PASS.

- [ ] **Step 4: Run all engine tests to check for regressions**

Run: `dotnet test OpenNest.Tests --filter "FullyQualifiedName~EngineRefactorSmokeTests|FullyQualifiedName~IterativeShrinkFillerTests|FullyQualifiedName~ShrinkFillerTests|FullyQualifiedName~RemnantFillerTests"`
Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/StripNestEngine.cs
git commit -m "feat(engine): rewrite StripNestEngine.Nest with iterative shrink-fill"
```

---

### Task 5: Delete obsolete files

**Files:**
- Delete: `OpenNest.Engine/StripNestResult.cs`
- Delete: `OpenNest.Engine/StripDirection.cs`

- [ ] **Step 1: Verify no remaining references**

Run: `grep -r "StripNestResult\|StripDirection" --include="*.cs" . | grep -v "\.md"`

Expected: No matches (all references were in the old `StripNestEngine.TryOrientation` which was deleted in Task 4).

- [ ] **Step 2: Delete the files**

```bash
rm OpenNest.Engine/StripNestResult.cs OpenNest.Engine/StripDirection.cs
```

- [ ] **Step 3: Build to verify no breakage**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test OpenNest.Tests`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add -u OpenNest.Engine/StripNestResult.cs OpenNest.Engine/StripDirection.cs
git commit -m "refactor(engine): delete obsolete StripNestResult and StripDirection"
```

---

### Task 6: Update spec and docs

**Files:**
- Modify: `docs/superpowers/specs/2026-03-19-iterative-shrink-fill-design.md`

- [ ] **Step 1: Update spec with "rotating calipers already included" note**

The spec was already updated during planning. Verify it reflects the final state — no `AngleCandidateBuilder` or `NestItem.CaliperAngle` changes listed.

- [ ] **Step 2: Commit if any changes**

```bash
git add docs/
git commit -m "docs: finalize iterative shrink-fill spec"
```
