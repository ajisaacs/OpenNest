# Engine Refactor Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract shared algorithms from DefaultNestEngine and StripNestEngine into focused, reusable helper classes.

**Architecture:** Five helper classes (AccumulatingProgress, ShrinkFiller, AngleCandidateBuilder, PairFiller, RemnantFiller) are extracted from the two engines. Each engine then composes these helpers instead of inlining the logic. No new interfaces — just focused classes with delegate-based decoupling.

**Tech Stack:** .NET 8, C#, xUnit

**Spec:** `docs/superpowers/specs/2026-03-16-engine-refactor-design.md`

---

## File Structure

**New files (OpenNest.Engine/):**
- `AccumulatingProgress.cs` — IProgress wrapper that prepends prior parts
- `ShrinkFiller.cs` — Static shrink-to-fit loop + ShrinkAxis enum + ShrinkResult class
- `AngleCandidateBuilder.cs` — Angle sweep/ML/pruning with known-good state
- `PairFiller.cs` — Pair candidate selection and fill loop
- `RemnantFiller.cs` — Iterative remnant-fill loop over RemnantFinder

**New test files (OpenNest.Tests/):**
- `AccumulatingProgressTests.cs`
- `ShrinkFillerTests.cs`
- `AngleCandidateBuilderTests.cs`
- `PairFillerTests.cs`
- `RemnantFillerTests.cs`
- `EngineRefactorSmokeTests.cs` — Integration tests verifying engines produce same results

**Modified files:**
- `OpenNest.Engine/DefaultNestEngine.cs` — Remove extracted code, compose helpers
- `OpenNest.Engine/StripNestEngine.cs` — Remove extracted code, compose helpers
- `OpenNest.Engine/NestEngineBase.cs` — Replace remnant loop with RemnantFiller

---

## Chunk 1: Leaf Extractions (AccumulatingProgress, ShrinkFiller)

### Task 0: Add InternalsVisibleTo for test project

**Files:**
- Modify: `OpenNest.Engine/OpenNest.Engine.csproj`

Several extracted classes are `internal`. The test project needs access.

- [ ] **Step 1: Add InternalsVisibleTo to the Engine csproj**

Add inside the first `<PropertyGroup>` or add a new `<ItemGroup>`:

```xml
<ItemGroup>
    <InternalsVisibleTo Include="OpenNest.Tests" />
</ItemGroup>
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add OpenNest.Engine/OpenNest.Engine.csproj
git commit -m "build: add InternalsVisibleTo for OpenNest.Tests"
```

---

### Task 1: Extract AccumulatingProgress

**Files:**
- Create: `OpenNest.Engine/AccumulatingProgress.cs`
- Create: `OpenNest.Tests/AccumulatingProgressTests.cs`

- [ ] **Step 1: Write the failing test**

In `OpenNest.Tests/AccumulatingProgressTests.cs`:

```csharp
namespace OpenNest.Tests;

public class AccumulatingProgressTests
{
    private class CapturingProgress : IProgress<NestProgress>
    {
        public NestProgress Last { get; private set; }
        public void Report(NestProgress value) => Last = value;
    }

    [Fact]
    public void Report_PrependsPreviousParts()
    {
        var inner = new CapturingProgress();
        var previous = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        var accumulating = new AccumulatingProgress(inner, previous);

        var newParts = new List<Part> { TestHelpers.MakePartAt(20, 0, 10) };
        accumulating.Report(new NestProgress { BestParts = newParts, BestPartCount = 1 });

        Assert.NotNull(inner.Last);
        Assert.Equal(2, inner.Last.BestParts.Count);
        Assert.Equal(2, inner.Last.BestPartCount);
    }

    [Fact]
    public void Report_NoPreviousParts_PassesThrough()
    {
        var inner = new CapturingProgress();
        var accumulating = new AccumulatingProgress(inner, new List<Part>());

        var newParts = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        accumulating.Report(new NestProgress { BestParts = newParts, BestPartCount = 1 });

        Assert.NotNull(inner.Last);
        Assert.Single(inner.Last.BestParts);
    }

    [Fact]
    public void Report_NullBestParts_PassesThrough()
    {
        var inner = new CapturingProgress();
        var previous = new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };
        var accumulating = new AccumulatingProgress(inner, previous);

        accumulating.Report(new NestProgress { BestParts = null });

        Assert.NotNull(inner.Last);
        Assert.Null(inner.Last.BestParts);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Tests --filter "AccumulatingProgressTests" -v n`
Expected: Build error — `AccumulatingProgress` class does not exist yet.

- [ ] **Step 3: Create AccumulatingProgress class**

In `OpenNest.Engine/AccumulatingProgress.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace OpenNest
{
    /// <summary>
    /// Wraps an IProgress to prepend previously placed parts to each report,
    /// so the UI shows the full picture during incremental fills.
    /// </summary>
    internal class AccumulatingProgress : IProgress<NestProgress>
    {
        private readonly IProgress<NestProgress> inner;
        private readonly List<Part> previousParts;

        public AccumulatingProgress(IProgress<NestProgress> inner, List<Part> previousParts)
        {
            this.inner = inner;
            this.previousParts = previousParts;
        }

        public void Report(NestProgress value)
        {
            if (value.BestParts != null && previousParts.Count > 0)
            {
                var combined = new List<Part>(previousParts.Count + value.BestParts.Count);
                combined.AddRange(previousParts);
                combined.AddRange(value.BestParts);
                value.BestParts = combined;
                value.BestPartCount = combined.Count;
            }

            inner.Report(value);
        }
    }
}
```

**Note:** The class is `internal`. The test project needs `InternalsVisibleTo`. Check if `OpenNest.Engine.csproj` already has it; if not, add `[assembly: InternalsVisibleTo("OpenNest.Tests")]` in `OpenNest.Engine/Properties/AssemblyInfo.cs` or as an `<InternalsVisibleTo>` item in the csproj.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Tests --filter "AccumulatingProgressTests" -v n`
Expected: All 3 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/AccumulatingProgress.cs OpenNest.Tests/AccumulatingProgressTests.cs
git commit -m "refactor(engine): extract AccumulatingProgress from StripNestEngine"
```

---

### Task 2: Extract ShrinkFiller

**Files:**
- Create: `OpenNest.Engine/ShrinkFiller.cs`
- Create: `OpenNest.Tests/ShrinkFillerTests.cs`

- [ ] **Step 1: Write the failing tests**

In `OpenNest.Tests/ShrinkFillerTests.cs`:

```csharp
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class ShrinkFillerTests
{
    private static Drawing MakeSquareDrawing(double size)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing("square", pgm);
    }

    [Fact]
    public void Shrink_ReducesDimension_UntilCountDrops()
    {
        // 10x10 parts on a 100x50 box with 1.0 spacing.
        // Initial fill should place multiple parts. Shrinking height
        // should eventually reduce to the tightest strip.
        var drawing = MakeSquareDrawing(10);
        var item = new NestItem { Drawing = drawing };
        var box = new Box(0, 0, 100, 50);

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var result = ShrinkFiller.Shrink(fillFunc, item, box, 1.0, ShrinkAxis.Height);

        Assert.NotNull(result);
        Assert.True(result.Parts.Count > 0);
        Assert.True(result.Dimension <= 50, "Dimension should be <= original");
        Assert.True(result.Dimension > 0);
    }

    [Fact]
    public void Shrink_Width_ReducesHorizontally()
    {
        var drawing = MakeSquareDrawing(10);
        var item = new NestItem { Drawing = drawing };
        var box = new Box(0, 0, 100, 50);

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        var result = ShrinkFiller.Shrink(fillFunc, item, box, 1.0, ShrinkAxis.Width);

        Assert.NotNull(result);
        Assert.True(result.Parts.Count > 0);
        Assert.True(result.Dimension <= 100);
    }

    [Fact]
    public void Shrink_RespectsMaxIterations()
    {
        var callCount = 0;
        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            callCount++;
            return new List<Part> { TestHelpers.MakePartAt(0, 0, 5) };
        };

        var item = new NestItem { Drawing = MakeSquareDrawing(5) };
        var box = new Box(0, 0, 100, 100);

        ShrinkFiller.Shrink(fillFunc, item, box, 1.0, ShrinkAxis.Height, maxIterations: 3);

        // 1 initial + up to 3 shrink iterations = max 4 calls
        Assert.True(callCount <= 4);
    }

    [Fact]
    public void Shrink_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var drawing = MakeSquareDrawing(10);
        var item = new NestItem { Drawing = drawing };
        var box = new Box(0, 0, 100, 50);

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
            new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };

        var result = ShrinkFiller.Shrink(fillFunc, item, box, 1.0,
            ShrinkAxis.Height, token: cts.Token);

        // Should return initial fill without shrinking
        Assert.NotNull(result);
        Assert.True(result.Parts.Count > 0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Tests --filter "ShrinkFillerTests" -v n`
Expected: Build error — `ShrinkFiller`, `ShrinkAxis`, `ShrinkResult` do not exist.

- [ ] **Step 3: Create ShrinkFiller class**

In `OpenNest.Engine/ShrinkFiller.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest
{
    public enum ShrinkAxis { Width, Height }

    public class ShrinkResult
    {
        public List<Part> Parts { get; set; }
        public double Dimension { get; set; }
    }

    /// <summary>
    /// Fills a box then iteratively shrinks one axis by the spacing amount
    /// until the part count drops. Returns the tightest box that still fits
    /// the same number of parts.
    /// </summary>
    public static class ShrinkFiller
    {
        public static ShrinkResult Shrink(
            Func<NestItem, Box, List<Part>> fillFunc,
            NestItem item, Box box,
            double spacing,
            ShrinkAxis axis,
            CancellationToken token = default,
            int maxIterations = 20)
        {
            var parts = fillFunc(item, box);

            if (parts == null || parts.Count == 0)
                return new ShrinkResult { Parts = parts ?? new List<Part>(), Dimension = 0 };

            var targetCount = parts.Count;
            var bestParts = parts;
            var bestDim = MeasureDimension(parts, box, axis);

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

                var trialParts = fillFunc(item, trialBox);

                if (trialParts == null || trialParts.Count < targetCount)
                    break;

                bestParts = trialParts;
                bestDim = MeasureDimension(trialParts, box, axis);
            }

            return new ShrinkResult { Parts = bestParts, Dimension = bestDim };
        }

        private static double MeasureDimension(List<Part> parts, Box box, ShrinkAxis axis)
        {
            var placedBox = parts.Cast<IBoundable>().GetBoundingBox();

            return axis == ShrinkAxis.Width
                ? placedBox.Right - box.X
                : placedBox.Top - box.Y;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Tests --filter "ShrinkFillerTests" -v n`
Expected: All 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/ShrinkFiller.cs OpenNest.Tests/ShrinkFillerTests.cs
git commit -m "refactor(engine): extract ShrinkFiller from StripNestEngine"
```

---

## Chunk 2: AngleCandidateBuilder and PairFiller

### Task 3: Extract AngleCandidateBuilder

**Files:**
- Create: `OpenNest.Engine/AngleCandidateBuilder.cs`
- Create: `OpenNest.Tests/AngleCandidateBuilderTests.cs`
- Modify: `OpenNest.Engine/DefaultNestEngine.cs` — Add forwarding property

- [ ] **Step 1: Write the failing tests**

In `OpenNest.Tests/AngleCandidateBuilderTests.cs`:

```csharp
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class AngleCandidateBuilderTests
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

    [Fact]
    public void Build_ReturnsAtLeastTwoAngles()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 100);

        var angles = builder.Build(item, 0, workArea);

        Assert.True(angles.Count >= 2);
    }

    [Fact]
    public void Build_NarrowWorkArea_ProducesMoreAngles()
    {
        var builder = new AngleCandidateBuilder();
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var wideArea = new Box(0, 0, 100, 100);
        var narrowArea = new Box(0, 0, 100, 8); // narrower than part's longest side

        var wideAngles = builder.Build(item, 0, wideArea);
        var narrowAngles = builder.Build(item, 0, narrowArea);

        Assert.True(narrowAngles.Count > wideAngles.Count,
            $"Narrow ({narrowAngles.Count}) should have more angles than wide ({wideAngles.Count})");
    }

    [Fact]
    public void ForceFullSweep_ProducesFullSweep()
    {
        var builder = new AngleCandidateBuilder { ForceFullSweep = true };
        var item = new NestItem { Drawing = MakeRectDrawing(5, 5) };
        var workArea = new Box(0, 0, 100, 100);

        var angles = builder.Build(item, 0, workArea);

        // Full sweep at 5° steps = ~36 angles (0 to 175), plus base angles
        Assert.True(angles.Count > 10);
    }

    [Fact]
    public void RecordProductive_PrunesSubsequentBuilds()
    {
        var builder = new AngleCandidateBuilder { ForceFullSweep = true };
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 100, 8);

        // First build — full sweep
        var firstAngles = builder.Build(item, 0, workArea);

        // Record some as productive
        var productive = new List<AngleResult>
        {
            new AngleResult { AngleDeg = 0, PartCount = 5 },
            new AngleResult { AngleDeg = 45, PartCount = 3 },
        };
        builder.RecordProductive(productive);

        // Second build — should be pruned to known-good + base angles
        builder.ForceFullSweep = false;
        var secondAngles = builder.Build(item, 0, workArea);

        Assert.True(secondAngles.Count < firstAngles.Count,
            $"Pruned ({secondAngles.Count}) should be fewer than full ({firstAngles.Count})");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Tests --filter "AngleCandidateBuilderTests" -v n`
Expected: Build error — `AngleCandidateBuilder` does not exist.

- [ ] **Step 3: Create AngleCandidateBuilder class**

In `OpenNest.Engine/AngleCandidateBuilder.cs`:

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenNest.Engine.ML;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    /// <summary>
    /// Builds candidate rotation angles for single-item fill. Encapsulates the
    /// full pipeline: base angles, narrow-area sweep, ML prediction, and
    /// known-good pruning across fills.
    /// </summary>
    public class AngleCandidateBuilder
    {
        private readonly HashSet<double> knownGoodAngles = new();

        public bool ForceFullSweep { get; set; }

        public List<double> Build(NestItem item, double bestRotation, Box workArea)
        {
            var angles = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

            var testPart = new Part(item.Drawing);
            if (!bestRotation.IsEqualTo(0))
                testPart.Rotate(bestRotation);
            testPart.UpdateBounds();

            var partLongestSide = System.Math.Max(testPart.BoundingBox.Width, testPart.BoundingBox.Length);
            var workAreaShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var needsSweep = workAreaShortSide < partLongestSide || ForceFullSweep;

            if (needsSweep)
            {
                var step = Angle.ToRadians(5);
                for (var a = 0.0; a < System.Math.PI; a += step)
                {
                    if (!angles.Any(existing => existing.IsEqualTo(a)))
                        angles.Add(a);
                }
            }

            if (!ForceFullSweep && angles.Count > 2)
            {
                var features = FeatureExtractor.Extract(item.Drawing);
                if (features != null)
                {
                    var predicted = AnglePredictor.PredictAngles(
                        features, workArea.Width, workArea.Length);

                    if (predicted != null)
                    {
                        var mlAngles = new List<double>(predicted);

                        if (!mlAngles.Any(a => a.IsEqualTo(bestRotation)))
                            mlAngles.Add(bestRotation);
                        if (!mlAngles.Any(a => a.IsEqualTo(bestRotation + Angle.HalfPI)))
                            mlAngles.Add(bestRotation + Angle.HalfPI);

                        Debug.WriteLine($"[AngleCandidateBuilder] ML: {angles.Count} angles -> {mlAngles.Count} predicted");
                        angles = mlAngles;
                    }
                }
            }

            if (knownGoodAngles.Count > 0 && !ForceFullSweep)
            {
                var pruned = new List<double> { bestRotation, bestRotation + Angle.HalfPI };

                foreach (var a in knownGoodAngles)
                {
                    if (!pruned.Any(existing => existing.IsEqualTo(a)))
                        pruned.Add(a);
                }

                Debug.WriteLine($"[AngleCandidateBuilder] Pruned: {angles.Count} -> {pruned.Count} angles (known-good)");
                return pruned;
            }

            return angles;
        }

        /// <summary>
        /// Records angles that produced results. These are used to prune
        /// subsequent Build() calls.
        /// </summary>
        public void RecordProductive(List<AngleResult> angleResults)
        {
            foreach (var ar in angleResults)
            {
                if (ar.PartCount > 0)
                    knownGoodAngles.Add(Angle.ToRadians(ar.AngleDeg));
            }
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Tests --filter "AngleCandidateBuilderTests" -v n`
Expected: All 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/AngleCandidateBuilder.cs OpenNest.Tests/AngleCandidateBuilderTests.cs
git commit -m "refactor(engine): extract AngleCandidateBuilder from DefaultNestEngine"
```

---

### Task 4: Make BuildRotatedPattern and FillPattern internal static

**Files:**
- Modify: `OpenNest.Engine/DefaultNestEngine.cs:493-546`

This task prepares the pattern helpers for use by PairFiller. No tests needed — existing behavior is unchanged.

- [ ] **Step 1: Change method signatures to internal static**

In `DefaultNestEngine.cs`, change `BuildRotatedPattern` (line 493):
- From: `private Pattern BuildRotatedPattern(List<Part> groupParts, double angle)`
- To: `internal static Pattern BuildRotatedPattern(List<Part> groupParts, double angle)`

Change `FillPattern` (line 513):
- From: `private List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea)`
- To: `internal static List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea)`

These methods already have no instance state access — they only use their parameters. Verify this by confirming no `this.` or instance field/property references in their bodies.

- [ ] **Step 2: Build to verify no regressions**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

- [ ] **Step 3: Run all tests**

Run: `dotnet test OpenNest.Tests -v n`
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine/DefaultNestEngine.cs
git commit -m "refactor(engine): make BuildRotatedPattern and FillPattern internal static"
```

---

### Task 5: Extract PairFiller

**Files:**
- Create: `OpenNest.Engine/PairFiller.cs`
- Create: `OpenNest.Tests/PairFillerTests.cs`

- [ ] **Step 1: Write the failing tests**

In `OpenNest.Tests/PairFillerTests.cs`:

```csharp
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class PairFillerTests
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

    [Fact]
    public void Fill_ReturnsPartsForSimpleDrawing()
    {
        var plateSize = new Size(120, 60);
        var filler = new PairFiller(plateSize, 0.5);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 120, 60);

        var parts = filler.Fill(item, workArea);

        Assert.NotNull(parts);
        // Pair filling may or may not find interlocking pairs for rectangles,
        // but should return a non-null list.
    }

    [Fact]
    public void Fill_EmptyResult_WhenPartTooLarge()
    {
        var plateSize = new Size(10, 10);
        var filler = new PairFiller(plateSize, 0.5);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 20) };
        var workArea = new Box(0, 0, 10, 10);

        var parts = filler.Fill(item, workArea);

        Assert.NotNull(parts);
        Assert.Empty(parts);
    }

    [Fact]
    public void Fill_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var plateSize = new Size(120, 60);
        var filler = new PairFiller(plateSize, 0.5);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var workArea = new Box(0, 0, 120, 60);

        var parts = filler.Fill(item, workArea, token: cts.Token);

        // Should return empty or partial — not throw
        Assert.NotNull(parts);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Tests --filter "PairFillerTests" -v n`
Expected: Build error — `PairFiller` does not exist.

- [ ] **Step 3: Create PairFiller class**

In `OpenNest.Engine/PairFiller.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenNest.Engine.BestFit;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    /// <summary>
    /// Fills a work area using interlocking part pairs from BestFitCache.
    /// Extracted from DefaultNestEngine.FillWithPairs.
    /// </summary>
    public class PairFiller
    {
        private const int MinPairCandidates = 10;
        private static readonly TimeSpan PairTimeLimit = TimeSpan.FromSeconds(3);

        private readonly Size plateSize;
        private readonly double partSpacing;

        public PairFiller(Size plateSize, double partSpacing)
        {
            this.plateSize = plateSize;
            this.partSpacing = partSpacing;
        }

        public List<Part> Fill(NestItem item, Box workArea,
            int plateNumber = 0,
            CancellationToken token = default,
            IProgress<NestProgress> progress = null)
        {
            var bestFits = BestFitCache.GetOrCompute(
                item.Drawing, plateSize.Width, plateSize.Length, partSpacing);

            var candidates = SelectPairCandidates(bestFits, workArea);
            var remainderPatterns = BuildRemainderPatterns(candidates, item);
            Debug.WriteLine($"[PairFiller] Total: {bestFits.Count}, Kept: {bestFits.Count(r => r.Keep)}, Trying: {candidates.Count}");
            Debug.WriteLine($"[PairFiller] Plate: {plateSize.Width:F2}x{plateSize.Length:F2}, WorkArea: {workArea.Width:F2}x{workArea.Length:F2}");

            List<Part> best = null;
            var bestScore = default(FillScore);
            var deadline = Stopwatch.StartNew();

            try
            {
                for (var i = 0; i < candidates.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    if (i >= MinPairCandidates && deadline.Elapsed > PairTimeLimit)
                    {
                        Debug.WriteLine($"[PairFiller] Time limit at {i + 1}/{candidates.Count} ({deadline.ElapsedMilliseconds}ms)");
                        break;
                    }

                    var result = candidates[i];
                    var pairParts = result.BuildParts(item.Drawing);
                    var angles = result.HullAngles;
                    var engine = new FillLinear(workArea, partSpacing);
                    engine.RemainderPatterns = remainderPatterns;
                    var filled = DefaultNestEngine.FillPattern(engine, pairParts, angles, workArea);

                    if (filled != null && filled.Count > 0)
                    {
                        var score = FillScore.Compute(filled, workArea);
                        if (best == null || score > bestScore)
                        {
                            best = filled;
                            bestScore = score;
                        }
                    }

                    NestEngineBase.ReportProgress(progress, NestPhase.Pairs, plateNumber, best, workArea,
                        $"Pairs: {i + 1}/{candidates.Count} candidates, best = {bestScore.Count} parts");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[PairFiller] Cancelled mid-phase, using results so far");
            }

            Debug.WriteLine($"[PairFiller] Best pair result: {bestScore.Count} parts, density={bestScore.Density:P1} ({deadline.ElapsedMilliseconds}ms)");
            return best ?? new List<Part>();
        }

        private List<BestFitResult> SelectPairCandidates(List<BestFitResult> bestFits, Box workArea)
        {
            var kept = bestFits.Where(r => r.Keep).ToList();
            var top = kept.Take(50).ToList();

            var workShortSide = System.Math.Min(workArea.Width, workArea.Length);
            var plateShortSide = System.Math.Min(plateSize.Width, plateSize.Length);

            if (workShortSide < plateShortSide * 0.5)
            {
                var stripCandidates = bestFits
                    .Where(r => r.ShortestSide <= workShortSide + Tolerance.Epsilon
                             && r.Utilization >= 0.3)
                    .OrderByDescending(r => r.Utilization);

                var existing = new HashSet<BestFitResult>(top);

                foreach (var r in stripCandidates)
                {
                    if (top.Count >= 100)
                        break;

                    if (existing.Add(r))
                        top.Add(r);
                }

                Debug.WriteLine($"[PairFiller] Strip mode: {top.Count} candidates (shortSide <= {workShortSide:F1})");
            }

            return top;
        }

        private List<Pattern> BuildRemainderPatterns(List<BestFitResult> candidates, NestItem item)
        {
            var patterns = new List<Pattern>();

            foreach (var candidate in candidates.Take(5))
            {
                var pairParts = candidate.BuildParts(item.Drawing);
                var angles = candidate.HullAngles ?? new List<double> { 0 };

                foreach (var angle in angles.Take(3))
                {
                    var pattern = DefaultNestEngine.BuildRotatedPattern(pairParts, angle);
                    if (pattern.Parts.Count > 0)
                        patterns.Add(pattern);
                }
            }

            return patterns;
        }
    }
}
```

**Note:** `NestEngineBase.ReportProgress` is currently `protected static`. It needs to become `internal static` so PairFiller can call it. Change the access modifier in `NestEngineBase.cs` line 180 from `protected static` to `internal static`.

- [ ] **Step 4: Change ReportProgress to internal static**

In `NestEngineBase.cs` line 180, change:
- From: `protected static void ReportProgress(`
- To: `internal static void ReportProgress(`

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test OpenNest.Tests --filter "PairFillerTests" -v n`
Expected: All 3 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/PairFiller.cs OpenNest.Tests/PairFillerTests.cs OpenNest.Engine/NestEngineBase.cs
git commit -m "refactor(engine): extract PairFiller from DefaultNestEngine"
```

---

## Chunk 3: RemnantFiller and Engine Rewiring

### Task 6: Extract RemnantFiller

**Files:**
- Create: `OpenNest.Engine/RemnantFiller.cs`
- Create: `OpenNest.Tests/RemnantFillerTests2.cs` (using `2` to avoid conflict with existing `RemnantFinderTests.cs`)

- [ ] **Step 1: Write the failing tests**

In `OpenNest.Tests/RemnantFillerTests2.cs`:

```csharp
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class RemnantFillerTests2
{
    private static Drawing MakeSquareDrawing(double size)
    {
        var pgm = new OpenNest.CNC.Program();
        pgm.Codes.Add(new OpenNest.CNC.RapidMove(new Vector(0, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(size, 0)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(size, size)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, size)));
        pgm.Codes.Add(new OpenNest.CNC.LinearMove(new Vector(0, 0)));
        return new Drawing("sq", pgm);
    }

    [Fact]
    public void FillItems_PlacesPartsInRemnants()
    {
        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 1.0);

        // Place a large obstacle leaving a 40x100 strip on the right
        filler.AddObstacles(new[] { TestHelpers.MakePartAt(0, 0, 50) });

        var drawing = MakeSquareDrawing(10);
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

        var placed = filler.FillItems(items, fillFunc);

        Assert.True(placed.Count > 0, "Should place parts in remaining space");
    }

    [Fact]
    public void FillItems_DoesNotMutateItemQuantities()
    {
        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 1.0);

        var drawing = MakeSquareDrawing(10);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = drawing, Quantity = 3 }
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        {
            var plate = new Plate(b.Width, b.Length);
            var engine = new DefaultNestEngine(plate);
            return engine.Fill(ni, b, null, System.Threading.CancellationToken.None);
        };

        filler.FillItems(items, fillFunc);

        Assert.Equal(3, items[0].Quantity);
    }

    [Fact]
    public void FillItems_EmptyItems_ReturnsEmpty()
    {
        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 1.0);

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) => new List<Part>();

        var result = filler.FillItems(new List<NestItem>(), fillFunc);

        Assert.Empty(result);
    }

    [Fact]
    public void FillItems_RespectsCancellation()
    {
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var workArea = new Box(0, 0, 100, 100);
        var filler = new RemnantFiller(workArea, 1.0);

        var drawing = MakeSquareDrawing(10);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = drawing, Quantity = 5 }
        };

        Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
            new List<Part> { TestHelpers.MakePartAt(0, 0, 10) };

        var result = filler.FillItems(items, fillFunc, cts.Token);

        // Should not throw, returns whatever was placed
        Assert.NotNull(result);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test OpenNest.Tests --filter "RemnantFillerTests2" -v n`
Expected: Build error — `RemnantFiller` class (the new one, not `RemnantFinder`) does not exist.

- [ ] **Step 3: Create RemnantFiller class**

In `OpenNest.Engine/RemnantFiller.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest
{
    /// <summary>
    /// Iteratively fills remnant boxes with items using a RemnantFinder.
    /// After each fill, re-discovers free rectangles and tries again
    /// until no more items can be placed.
    /// </summary>
    public class RemnantFiller
    {
        private readonly RemnantFinder finder;
        private readonly double spacing;

        public RemnantFiller(Box workArea, double spacing)
        {
            this.spacing = spacing;
            finder = new RemnantFinder(workArea);
        }

        public void AddObstacles(IEnumerable<Part> parts)
        {
            foreach (var part in parts)
                finder.AddObstacle(part.BoundingBox.Offset(spacing));
        }

        public List<Part> FillItems(
            List<NestItem> items,
            Func<NestItem, Box, List<Part>> fillFunc,
            CancellationToken token = default,
            IProgress<NestProgress> progress = null)
        {
            if (items == null || items.Count == 0)
                return new List<Part>();

            var allParts = new List<Part>();
            var madeProgress = true;

            // Track quantities locally — do not mutate the input NestItem objects.
            // NOTE: Keyed by Drawing.Name — duplicate names would collide.
            // This matches the original StripNestEngine behavior.
            var localQty = new Dictionary<string, int>();
            foreach (var item in items)
                localQty[item.Drawing.Name] = item.Quantity;

            while (madeProgress && !token.IsCancellationRequested)
            {
                madeProgress = false;

                var minRemnantDim = double.MaxValue;
                foreach (var item in items)
                {
                    var qty = localQty[item.Drawing.Name];
                    if (qty <= 0)
                        continue;
                    var bb = item.Drawing.Program.BoundingBox();
                    var dim = System.Math.Min(bb.Width, bb.Length);
                    if (dim < minRemnantDim)
                        minRemnantDim = dim;
                }

                if (minRemnantDim == double.MaxValue)
                    break;

                var freeBoxes = finder.FindRemnants(minRemnantDim);

                if (freeBoxes.Count == 0)
                    break;

                foreach (var item in items)
                {
                    if (token.IsCancellationRequested)
                        break;

                    var qty = localQty[item.Drawing.Name];
                    if (qty == 0)
                        continue;

                    var itemBbox = item.Drawing.Program.BoundingBox();
                    var minItemDim = System.Math.Min(itemBbox.Width, itemBbox.Length);

                    foreach (var box in freeBoxes)
                    {
                        if (System.Math.Min(box.Width, box.Length) < minItemDim)
                            continue;

                        var fillItem = new NestItem { Drawing = item.Drawing, Quantity = qty };
                        var remnantParts = fillFunc(fillItem, box);

                        if (remnantParts != null && remnantParts.Count > 0)
                        {
                            allParts.AddRange(remnantParts);
                            localQty[item.Drawing.Name] = System.Math.Max(0, qty - remnantParts.Count);

                            foreach (var p in remnantParts)
                                finder.AddObstacle(p.BoundingBox.Offset(spacing));

                            madeProgress = true;
                            break;
                        }
                    }

                    if (madeProgress)
                        break;
                }
            }

            return allParts;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test OpenNest.Tests --filter "RemnantFillerTests2" -v n`
Expected: All 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add OpenNest.Engine/RemnantFiller.cs OpenNest.Tests/RemnantFillerTests2.cs
git commit -m "refactor(engine): extract RemnantFiller for iterative remnant filling"
```

---

### Task 7: Rewire DefaultNestEngine

**Files:**
- Modify: `OpenNest.Engine/DefaultNestEngine.cs`

This is the core rewiring. Replace extracted methods with calls to the new helper classes.

- [ ] **Step 1: Add AngleCandidateBuilder field and ForceFullAngleSweep forwarding property**

In `DefaultNestEngine.cs`, replace:

```csharp
public bool ForceFullAngleSweep { get; set; }

// Angles that have produced results across multiple Fill calls.
// Populated after each Fill; used to prune subsequent fills.
private readonly HashSet<double> knownGoodAngles = new();
```

With:

```csharp
private readonly AngleCandidateBuilder angleBuilder = new();

public bool ForceFullAngleSweep
{
    get => angleBuilder.ForceFullSweep;
    set => angleBuilder.ForceFullSweep = value;
}
```

- [ ] **Step 2: Replace BuildCandidateAngles call in FindBestFill**

In `FindBestFill` (line ~181), replace:
```csharp
var angles = BuildCandidateAngles(item, bestRotation, workArea);
```
With:
```csharp
var angles = angleBuilder.Build(item, bestRotation, workArea);
```

- [ ] **Step 3: Replace knownGoodAngles recording in FindBestFill**

In `FindBestFill`, replace the block (around line 243):
```csharp
// Record productive angles for future fills.
foreach (var ar in AngleResults)
{
    if (ar.PartCount > 0)
        knownGoodAngles.Add(Angle.ToRadians(ar.AngleDeg));
}
```
With:
```csharp
angleBuilder.RecordProductive(AngleResults);
```

- [ ] **Step 4: Replace FillWithPairs call in FindBestFill**

In `FindBestFill`, replace:
```csharp
var pairResult = FillWithPairs(item, workArea, token, progress);
```
With:
```csharp
var pairFiller = new PairFiller(Plate.Size, Plate.PartSpacing);
var pairResult = pairFiller.Fill(item, workArea, PlateNumber, token, progress);
```

- [ ] **Step 5: Replace FillWithPairs call in Fill(List<Part>) overload**

In the `Fill(List<Part> groupParts, Box workArea, ...)` method (around line 137), replace:
```csharp
var pairResult = FillWithPairs(nestItem, workArea, token, progress);
```
With:
```csharp
var pairFiller = new PairFiller(Plate.Size, Plate.PartSpacing);
var pairResult = pairFiller.Fill(nestItem, workArea, PlateNumber, token, progress);
```

- [ ] **Step 6: Delete the extracted methods**

Remove these methods and fields from DefaultNestEngine:
- `private readonly HashSet<double> knownGoodAngles` (already replaced in Step 1)
- `private List<double> BuildCandidateAngles(...)` (entire method, lines 279-347)
- `private List<Part> FillWithPairs(...)` (entire method, lines 365-421)
- `private List<BestFitResult> SelectPairCandidates(...)` (entire method, lines 428-462)
- `private List<Pattern> BuildRemainderPatterns(...)` (entire method, lines 471-489)
- `private const int MinPairCandidates = 10;` (line 362)
- `private static readonly TimeSpan PairTimeLimit = TimeSpan.FromSeconds(3);` (line 363)

Also remove now-unused `using` statements if any (e.g., `using OpenNest.Engine.BestFit;` if no longer referenced, though `QuickFillCount` still uses `BestFitCache`).

- [ ] **Step 7: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds with no errors.

- [ ] **Step 8: Run all tests**

Run: `dotnet test OpenNest.Tests -v n`
Expected: All tests PASS.

- [ ] **Step 9: Commit**

```bash
git add OpenNest.Engine/DefaultNestEngine.cs
git commit -m "refactor(engine): rewire DefaultNestEngine to use extracted helpers"
```

---

### Task 8: Rewire StripNestEngine

**Files:**
- Modify: `OpenNest.Engine/StripNestEngine.cs`

- [ ] **Step 1: Replace TryOrientation shrink loop with ShrinkFiller**

In `TryOrientation`, replace the shrink loop (lines 188-215) with:

```csharp
// Shrink to tightest strip.
var shrinkAxis = direction == StripDirection.Bottom
    ? ShrinkAxis.Height : ShrinkAxis.Width;

Func<NestItem, Box, List<Part>> shrinkFill = (ni, b) =>
{
    var trialInner = new DefaultNestEngine(Plate);
    return trialInner.Fill(ni, b, progress, token);
};

var shrinkResult = ShrinkFiller.Shrink(shrinkFill,
    new NestItem { Drawing = stripItem.Drawing, Quantity = stripItem.Quantity },
    stripBox, Plate.PartSpacing, shrinkAxis, token);

if (shrinkResult.Parts == null || shrinkResult.Parts.Count == 0)
    return result;

bestParts = shrinkResult.Parts;
bestDim = shrinkResult.Dimension;
```

Remove the local variables that the shrink loop used to compute (`actualDim`, `targetCount`, and the `for` loop itself). Keep the remnant section below it.

**Important:** The initial fill call (lines 169-172) stays as-is — ShrinkFiller handles both the initial fill AND the shrink loop. So replace from the initial fill through the shrink loop with the single `ShrinkFiller.Shrink()` call, since `Shrink` does its own initial fill internally.

Actually — looking more carefully, the initial fill (lines 170-172) uses `progress` while shrink trials in the original code create a new `DefaultNestEngine` each time. `ShrinkFiller.Shrink` does its own initial fill via `fillFunc`. So the replacement should:

1. Remove the initial fill (lines 169-175) and the shrink loop (lines 188-215)
2. Replace both with the single `ShrinkFiller.Shrink()` call

The `fillFunc` delegate handles both the initial fill and each shrink trial.

- [ ] **Step 2: Replace ShrinkFill method with ShrinkFiller calls**

Replace the entire `ShrinkFill` method (lines 358-419) with:

```csharp
private List<Part> ShrinkFill(NestItem item, Box box,
    IProgress<NestProgress> progress, CancellationToken token)
{
    Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
    {
        var inner = new DefaultNestEngine(Plate);
        return inner.Fill(ni, b, null, token);
    };

    // The original code shrinks width then height independently against the
    // original box. The height shrink's result overwrites the width shrink's,
    // so only the height result matters. We run both to preserve behavior
    // (width shrink is a no-op on the final result but we keep it for parity).
    var heightResult = ShrinkFiller.Shrink(fillFunc, item, box,
        Plate.PartSpacing, ShrinkAxis.Height, token);

    return heightResult.Parts;
}
```

**Note:** The original `ShrinkFill` runs width-shrink then height-shrink sequentially, but the height result always overwrites the width result (both shrink against the original box independently, `targetCount` never changes). Running only height-shrink is faithful to the original output while avoiding redundant initial fills.

- [ ] **Step 3: Replace remnant loop with RemnantFiller**

In `TryOrientation`, replace the remnant section (from `// Build remnant box with spacing gap` through the end of the `while (madeProgress ...)` loop) with:

```csharp
// Fill remnants
if (remnantBox.Width > 0 && remnantBox.Length > 0)
{
    var remnantProgress = progress != null
        ? new AccumulatingProgress(progress, allParts)
        : (IProgress<NestProgress>)null;

    var remnantFiller = new RemnantFiller(workArea, spacing);
    remnantFiller.AddObstacles(allParts);

    Func<NestItem, Box, List<Part>> remnantFillFunc = (ni, b) =>
        ShrinkFill(ni, b, remnantProgress, token);

    var additional = remnantFiller.FillItems(effectiveRemainder,
        remnantFillFunc, token, remnantProgress);

    allParts.AddRange(additional);
}
```

Keep the `effectiveRemainder` construction and sorting that precedes this block (lines 239-259). Keep the `remnantBox` computation (lines 228-233) since we need to check `remnantBox.Width > 0`.

Remove:
- The `localQty` dictionary (lines 276-278)
- The `while (madeProgress ...)` loop (lines 280-342)
- The `AccumulatingProgress` nested class (lines 425-449) — now using the standalone version

- [ ] **Step 4: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

- [ ] **Step 5: Run all tests**

Run: `dotnet test OpenNest.Tests -v n`
Expected: All tests PASS.

- [ ] **Step 6: Commit**

```bash
git add OpenNest.Engine/StripNestEngine.cs
git commit -m "refactor(engine): rewire StripNestEngine to use extracted helpers"
```

---

### Task 9: Rewire NestEngineBase.Nest

**Files:**
- Modify: `OpenNest.Engine/NestEngineBase.cs:74-97`

- [ ] **Step 1: Replace the fill loop with RemnantFiller**

In `NestEngineBase.Nest`, replace phase 1 (the `foreach (var item in fillItems)` loop, lines 74-98) with:

```csharp
// Phase 1: Fill multi-quantity drawings using RemnantFiller.
if (fillItems.Count > 0)
{
    var remnantFiller = new RemnantFiller(workArea, Plate.PartSpacing);

    Func<NestItem, Box, List<Part>> fillFunc = (ni, b) =>
        FillExact(ni, b, progress, token);

    var fillParts = remnantFiller.FillItems(fillItems, fillFunc, token, progress);

    if (fillParts.Count > 0)
    {
        allParts.AddRange(fillParts);

        // Deduct placed quantities
        foreach (var item in fillItems)
        {
            var placed = fillParts.Count(p =>
                p.BaseDrawing.Name == item.Drawing.Name);
            item.Quantity = System.Math.Max(0, item.Quantity - placed);
        }

        // Update workArea for pack phase
        var placedObstacles = fillParts.Select(p => p.BoundingBox.Offset(Plate.PartSpacing)).ToList();
        var finder = new RemnantFinder(workArea, placedObstacles);
        var remnants = finder.FindRemnants();
        if (remnants.Count > 0)
            workArea = remnants[0];
        else
            workArea = new Box(0, 0, 0, 0);
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds.

- [ ] **Step 3: Run all tests**

Run: `dotnet test OpenNest.Tests -v n`
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine/NestEngineBase.cs
git commit -m "refactor(engine): use RemnantFiller in NestEngineBase.Nest"
```

---

### Task 10: Integration Smoke Tests

**Files:**
- Create: `OpenNest.Tests/EngineRefactorSmokeTests.cs`

These tests verify that the refactored engines produce valid results end-to-end.

- [ ] **Step 1: Write smoke tests**

In `OpenNest.Tests/EngineRefactorSmokeTests.cs`:

```csharp
using OpenNest.Geometry;

namespace OpenNest.Tests;

public class EngineRefactorSmokeTests
{
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
    public void DefaultEngine_FillNestItem_ProducesResults()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "DefaultNestEngine should fill parts");
    }

    [Fact]
    public void DefaultEngine_FillGroupParts_ProducesResults()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var drawing = MakeRectDrawing(20, 10);
        var groupParts = new List<Part> { new Part(drawing) };

        var parts = engine.Fill(groupParts, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "DefaultNestEngine group fill should produce parts");
    }

    [Fact]
    public void DefaultEngine_ForceFullAngleSweep_StillWorks()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        engine.ForceFullAngleSweep = true;
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "ForceFullAngleSweep should still produce results");
    }

    [Fact]
    public void StripEngine_Nest_ProducesResults()
    {
        var plate = new Plate(120, 60);
        var engine = new StripNestEngine(plate);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10, "large"), Quantity = 10 },
            new NestItem { Drawing = MakeRectDrawing(8, 5, "small"), Quantity = 5 },
        };

        var parts = engine.Nest(items, null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "StripNestEngine should nest parts");
    }

    [Fact]
    public void DefaultEngine_Nest_ProducesResults()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var items = new List<NestItem>
        {
            new NestItem { Drawing = MakeRectDrawing(20, 10, "a"), Quantity = 5 },
            new NestItem { Drawing = MakeRectDrawing(15, 8, "b"), Quantity = 3 },
        };

        var parts = engine.Nest(items, null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0, "Base Nest method should place parts");
    }

    [Fact]
    public void BruteForceRunner_StillWorks()
    {
        var plate = new Plate(120, 60);
        var drawing = MakeRectDrawing(20, 10);

        var result = OpenNest.Engine.ML.BruteForceRunner.Run(drawing, plate, forceFullAngleSweep: true);

        Assert.NotNull(result);
        Assert.True(result.PartCount > 0);
    }
}
```

- [ ] **Step 2: Run all smoke tests**

Run: `dotnet test OpenNest.Tests --filter "EngineRefactorSmokeTests" -v n`
Expected: All 6 tests PASS.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test OpenNest.Tests -v n`
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Tests/EngineRefactorSmokeTests.cs
git commit -m "test(engine): add integration smoke tests for engine refactor"
```

---

### Task 11: Clean up unused imports

**Files:**
- Modify: `OpenNest.Engine/DefaultNestEngine.cs`
- Modify: `OpenNest.Engine/StripNestEngine.cs`

- [ ] **Step 1: Remove unused using statements from DefaultNestEngine**

After extracting the pair/angle code, check which `using` statements are no longer needed. Likely candidates:
- `using OpenNest.Engine.BestFit;` — may still be needed by `QuickFillCount`
- `using OpenNest.Engine.ML;` — no longer needed (moved to AngleCandidateBuilder)

Build to verify: `dotnet build OpenNest.sln`

- [ ] **Step 2: Remove AccumulatingProgress nested class from StripNestEngine if still present**

Verify `StripNestEngine.cs` no longer contains the `AccumulatingProgress` nested class definition. It should have been removed in Task 8.

- [ ] **Step 3: Build and run full test suite**

Run: `dotnet build OpenNest.sln && dotnet test OpenNest.Tests -v n`
Expected: Build succeeds, all tests PASS.

- [ ] **Step 4: Commit**

```bash
git add OpenNest.Engine/DefaultNestEngine.cs OpenNest.Engine/StripNestEngine.cs
git commit -m "refactor(engine): clean up unused imports after extraction"
```
