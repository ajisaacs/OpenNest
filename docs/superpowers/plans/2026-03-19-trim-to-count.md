# Trim-to-Count Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the expensive iterative ShrinkFiller loop with a single fill + axis-aware edge-sorted trim, and replace the blind `Take(N)` in DefaultNestEngine.Fill with the same trim.

**Architecture:** Add `ShrinkFiller.TrimToCount` static method that sorts parts by trailing edge and keeps the N nearest to origin. Gut the shrink loop in `Shrink`, remove `maxIterations` parameter. Update `DefaultNestEngine.Fill` to call `TrimToCount` instead of `Take(N)`.

**Tech Stack:** .NET 8, xUnit

**Spec:** `docs/superpowers/specs/2026-03-19-trim-to-count-design.md`

---

## File Map

- **Modify:** `OpenNest.Engine/Fill/ShrinkFiller.cs` — add `TrimToCount`, replace shrink loop, remove `maxIterations`
- **Modify:** `OpenNest.Engine/DefaultNestEngine.cs:55-56` — replace `Take(N)` with `TrimToCount`
- **Modify:** `OpenNest.Tests/ShrinkFillerTests.cs` — delete `Shrink_RespectsMaxIterations`, update existing tests, add `TrimToCount` tests

---

### Task 1: Add TrimToCount with tests

**Files:**
- Modify: `OpenNest.Tests/ShrinkFillerTests.cs`
- Modify: `OpenNest.Engine/Fill/ShrinkFiller.cs`

- [ ] **Step 1: Write failing tests for TrimToCount**

Add these tests to the bottom of `OpenNest.Tests/ShrinkFillerTests.cs`:

```csharp
[Fact]
public void TrimToCount_Width_KeepsPartsNearestToOrigin()
{
    var parts = new List<Part>
    {
        TestHelpers.MakePartAt(0, 0, 5),   // Right = 5
        TestHelpers.MakePartAt(10, 0, 5),  // Right = 15
        TestHelpers.MakePartAt(20, 0, 5),  // Right = 25
        TestHelpers.MakePartAt(30, 0, 5),  // Right = 35
    };

    var trimmed = ShrinkFiller.TrimToCount(parts, 2, ShrinkAxis.Width);

    Assert.Equal(2, trimmed.Count);
    Assert.True(trimmed.All(p => p.BoundingBox.Right <= 15));
}

[Fact]
public void TrimToCount_Height_KeepsPartsNearestToOrigin()
{
    var parts = new List<Part>
    {
        TestHelpers.MakePartAt(0, 0, 5),   // Top = 5
        TestHelpers.MakePartAt(0, 10, 5),  // Top = 15
        TestHelpers.MakePartAt(0, 20, 5),  // Top = 25
        TestHelpers.MakePartAt(0, 30, 5),  // Top = 35
    };

    var trimmed = ShrinkFiller.TrimToCount(parts, 2, ShrinkAxis.Height);

    Assert.Equal(2, trimmed.Count);
    Assert.True(trimmed.All(p => p.BoundingBox.Top <= 15));
}

[Fact]
public void TrimToCount_ReturnsInput_WhenCountAtOrBelowTarget()
{
    var parts = new List<Part>
    {
        TestHelpers.MakePartAt(0, 0, 5),
        TestHelpers.MakePartAt(10, 0, 5),
    };

    var same = ShrinkFiller.TrimToCount(parts, 2, ShrinkAxis.Width);
    Assert.Same(parts, same);

    var fewer = ShrinkFiller.TrimToCount(parts, 5, ShrinkAxis.Width);
    Assert.Same(parts, fewer);
}

[Fact]
public void TrimToCount_DoesNotMutateInput()
{
    var parts = new List<Part>
    {
        TestHelpers.MakePartAt(0, 0, 5),
        TestHelpers.MakePartAt(10, 0, 5),
        TestHelpers.MakePartAt(20, 0, 5),
    };

    var originalCount = parts.Count;
    var trimmed = ShrinkFiller.TrimToCount(parts, 1, ShrinkAxis.Width);

    Assert.Equal(originalCount, parts.Count);
    Assert.Equal(1, trimmed.Count);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests --filter "TrimToCount"`
Expected: Build error — `ShrinkFiller` does not contain `TrimToCount`

- [ ] **Step 3: Implement TrimToCount**

Add this method to `OpenNest.Engine/Fill/ShrinkFiller.cs` inside the `ShrinkFiller` class, after the `Shrink` method. Note: `internal` visibility works because `OpenNest.Engine.csproj` has `<InternalsVisibleTo Include="OpenNest.Tests" />`.

```csharp
/// <summary>
/// Keeps the <paramref name="targetCount"/> parts nearest to the origin
/// along the given axis, discarding parts farthest from the origin.
/// Returns the input list unchanged if count is already at or below target.
/// </summary>
internal static List<Part> TrimToCount(List<Part> parts, int targetCount, ShrinkAxis axis)
{
    if (parts == null || parts.Count <= targetCount)
        return parts;

    return axis == ShrinkAxis.Width
        ? parts.OrderBy(p => p.BoundingBox.Right).Take(targetCount).ToList()
        : parts.OrderBy(p => p.BoundingBox.Top).Take(targetCount).ToList();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests --filter "TrimToCount"`
Expected: All 4 TrimToCount tests PASS

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/Fill/ShrinkFiller.cs OpenNest.Tests/ShrinkFillerTests.cs
git commit -m "feat: add ShrinkFiller.TrimToCount for axis-aware edge trimming"
```

---

### Task 2: Replace ShrinkFiller shrink loop with TrimToCount

**Files:**
- Modify: `OpenNest.Engine/Fill/ShrinkFiller.cs`
- Modify: `OpenNest.Tests/ShrinkFillerTests.cs`

- [ ] **Step 1: Delete `Shrink_RespectsMaxIterations` test and rename misleading test**

Remove the entire `Shrink_RespectsMaxIterations` test method (lines 63-79) from `OpenNest.Tests/ShrinkFillerTests.cs`.

Rename `Shrink_ReducesDimension_UntilCountDrops` to `Shrink_FillsAndReturnsDimension` since the iterative shrink behavior no longer exists.

- [ ] **Step 2: Replace the shrink loop in `Shrink`**

In `OpenNest.Engine/Fill/ShrinkFiller.cs`, replace the `Shrink` method signature and body. Remove the `maxIterations` parameter. Replace the shrink loop (lines 63-96) with trim + measure:

Before (lines 25-97):
```csharp
public static ShrinkResult Shrink(
    Func<NestItem, Box, List<Part>> fillFunc,
    NestItem item, Box box,
    double spacing,
    ShrinkAxis axis,
    CancellationToken token = default,
    int maxIterations = 20,
    int targetCount = 0,
    IProgress<NestProgress> progress = null,
    int plateNumber = 0,
    List<Part> placedParts = null)
{
    // If a target count is specified, estimate a smaller starting box
    // to avoid an expensive full-area fill.
    var startBox = box;
    if (targetCount > 0)
        startBox = EstimateStartBox(item, box, spacing, axis, targetCount);

    var parts = fillFunc(item, startBox);

    // If estimate was too aggressive and we got fewer than target,
    // fall back to the full box.
    if (targetCount > 0 && startBox != box
        && (parts == null || parts.Count < targetCount))
    {
        parts = fillFunc(item, box);
    }

    if (parts == null || parts.Count == 0)
        return new ShrinkResult { Parts = parts ?? new List<Part>(), Dimension = 0 };

    // Shrink target: if a target count was given and we got at least that many,
    // shrink to fit targetCount (not the full count). This produces a tighter box.
    // If we got fewer than target, shrink to maintain what we have.
    var shrinkTarget = targetCount > 0
        ? System.Math.Min(targetCount, parts.Count)
        : parts.Count;

    var bestParts = parts;
    var bestDim = MeasureDimension(parts, box, axis);

    ReportShrinkProgress(progress, plateNumber, placedParts, bestParts, box, axis, bestDim);

    for (var i = 0; i < maxIterations; i++)
    {
        if (token.IsCancellationRequested)
            break;

        var trialDim = bestDim - spacing;
        if (trialDim <= 0)
            break;

        var trialBox = axis == ShrinkAxis.Width
            ? new Box(box.X, box.Y, trialDim, box.Length)
            : new Box(box.X, box.Y, box.Width, trialDim);

        // Report the trial box before the fill so the UI updates the
        // work area border immediately rather than after the fill completes.
        ReportShrinkProgress(progress, plateNumber, placedParts, bestParts, trialBox, axis, trialDim);

        var trialParts = fillFunc(item, trialBox);

        if (trialParts == null || trialParts.Count < shrinkTarget)
            break;

        bestParts = trialParts;
        bestDim = MeasureDimension(trialParts, box, axis);

        ReportShrinkProgress(progress, plateNumber, placedParts, bestParts, trialBox, axis, bestDim);
    }

    return new ShrinkResult { Parts = bestParts, Dimension = bestDim };
}
```

After:
```csharp
public static ShrinkResult Shrink(
    Func<NestItem, Box, List<Part>> fillFunc,
    NestItem item, Box box,
    double spacing,
    ShrinkAxis axis,
    CancellationToken token = default,
    int targetCount = 0,
    IProgress<NestProgress> progress = null,
    int plateNumber = 0,
    List<Part> placedParts = null)
{
    var startBox = box;
    if (targetCount > 0)
        startBox = EstimateStartBox(item, box, spacing, axis, targetCount);

    var parts = fillFunc(item, startBox);

    if (targetCount > 0 && startBox != box
        && (parts == null || parts.Count < targetCount))
    {
        parts = fillFunc(item, box);
    }

    if (parts == null || parts.Count == 0)
        return new ShrinkResult { Parts = parts ?? new List<Part>(), Dimension = 0 };

    var shrinkTarget = targetCount > 0
        ? System.Math.Min(targetCount, parts.Count)
        : parts.Count;

    if (parts.Count > shrinkTarget)
        parts = TrimToCount(parts, shrinkTarget, axis);

    var dim = MeasureDimension(parts, box, axis);

    ReportShrinkProgress(progress, plateNumber, placedParts, parts, box, axis, dim);

    return new ShrinkResult { Parts = parts, Dimension = dim };
}
```

- [ ] **Step 3: Update the class xmldoc**

Replace the `ShrinkFiller` class summary (line 18-22):

Before:
```csharp
/// <summary>
/// Fills a box then iteratively shrinks one axis by the spacing amount
/// until the part count drops. Returns the tightest box that still fits
/// the target number of parts.
/// </summary>
```

After:
```csharp
/// <summary>
/// Fills a box and trims excess parts by removing those farthest from
/// the origin along the shrink axis.
/// </summary>
```

- [ ] **Step 4: Run all ShrinkFiller tests**

Run: `dotnet test OpenNest.Tests --filter "ShrinkFiller"`
Expected: All tests PASS (the `Shrink_RespectsMaxIterations` test was deleted, remaining tests still pass because the fill + trim produces valid results with positive counts and dimensions)

- [ ] **Step 5: Build the full solution to check for compilation errors**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds. No callers pass `maxIterations` by name except the deleted test. `IterativeShrinkFiller.cs` uses positional args and does not pass `maxIterations`.

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/Fill/ShrinkFiller.cs OpenNest.Tests/ShrinkFillerTests.cs
git commit -m "refactor: replace ShrinkFiller shrink loop with TrimToCount"
```

---

### Task 3: Replace Take(N) in DefaultNestEngine.Fill

**Files:**
- Modify: `OpenNest.Engine/DefaultNestEngine.cs:55-56`

- [ ] **Step 1: Replace the Take(N) call**

In `OpenNest.Engine/DefaultNestEngine.cs`, replace lines 55-56:

Before:
```csharp
if (item.Quantity > 0 && best.Count > item.Quantity)
    best = best.Take(item.Quantity).ToList();
```

After:
```csharp
if (item.Quantity > 0 && best.Count > item.Quantity)
    best = ShrinkFiller.TrimToCount(best, item.Quantity, ShrinkAxis.Width);
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds. `ShrinkFiller` and `ShrinkAxis` are already available via the `using OpenNest.Engine.Fill;` import on line 1.

- [ ] **Step 3: Run all engine tests**

Run: `dotnet test OpenNest.Tests`
Expected: All tests PASS

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine/DefaultNestEngine.cs
git commit -m "refactor: use TrimToCount instead of blind Take(N) in DefaultNestEngine.Fill"
```
