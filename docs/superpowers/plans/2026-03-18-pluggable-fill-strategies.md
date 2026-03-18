# Pluggable Fill Strategies Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the four hard-wired fill phases from `DefaultNestEngine.FindBestFill` into pluggable `IFillStrategy` implementations behind a pipeline orchestrator.

**Architecture:** Each fill phase (Pairs, RectBestFit, Extents, Linear) becomes a stateless `IFillStrategy` adapter around its existing filler class. A `FillContext` carries inputs and pipeline state. `FillStrategyRegistry` discovers strategies via reflection. `DefaultNestEngine.FindBestFill` is replaced by `RunPipeline` which iterates strategies in order.

**Tech Stack:** .NET 8, C#, xUnit (OpenNest.Tests)

**Spec:** `docs/superpowers/specs/2026-03-18-pluggable-fill-strategies-design.md`

**Deliberate behavioral change:** The phase execution order changes from Pairs/Linear/RectBestFit/Extents to Pairs/RectBestFit/Extents/Linear. Linear is moved last because it is the most expensive phase and rarely wins. The final result is equivalent (the pipeline always picks the globally best result), but intermediate progress reports during the fill will differ.

---

### Task 1: Add `NestPhase.Custom` enum value

**Files:**
- Modify: `OpenNest.Engine/NestProgress.cs:6-13`

- [ ] **Step 1: Add Custom to NestPhase enum**

In `OpenNest.Engine/NestProgress.cs`, add `Custom` after `Extents`:

```csharp
public enum NestPhase
{
    Linear,
    RectBestFit,
    Pairs,
    Nfp,
    Extents,
    Custom
}
```

- [ ] **Step 2: Add Custom to FormatPhaseName in NestEngineBase**

In `OpenNest.Engine/NestEngineBase.cs`, add a case in `FormatPhaseName`:

```csharp
case NestPhase.Custom: return "Custom";
```

- [ ] **Step 3: Build to verify no errors**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 4: Run existing tests to verify no regression**

Run: `dotnet test OpenNest.Tests`
Expected: All tests pass

- [ ] **Step 5: Commit**

```
feat(engine): add NestPhase.Custom for plugin fill strategies
```

---

### Task 2: Create `IFillStrategy` and `FillContext`

**Files:**
- Create: `OpenNest.Engine/Strategies/IFillStrategy.cs`
- Create: `OpenNest.Engine/Strategies/FillContext.cs`

- [ ] **Step 1: Create the Strategies directory**

Verify `OpenNest.Engine/Strategies/` exists (create if needed).

- [ ] **Step 2: Write IFillStrategy.cs**

Create `OpenNest.Engine/Strategies/IFillStrategy.cs`:

```csharp
using System.Collections.Generic;

namespace OpenNest
{
    public interface IFillStrategy
    {
        string Name { get; }
        NestPhase Phase { get; }
        int Order { get; }
        List<Part> Fill(FillContext context);
    }
}
```

- [ ] **Step 3: Write FillContext.cs**

Create `OpenNest.Engine/Strategies/FillContext.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Geometry;

namespace OpenNest
{
    public class FillContext
    {
        public NestItem Item { get; init; }
        public Box WorkArea { get; init; }
        public Plate Plate { get; init; }
        public int PlateNumber { get; init; }
        public CancellationToken Token { get; init; }
        public IProgress<NestProgress> Progress { get; init; }

        public List<Part> CurrentBest { get; set; }
        public FillScore CurrentBestScore { get; set; }
        public NestPhase WinnerPhase { get; set; }
        public List<PhaseResult> PhaseResults { get; } = new();
        public List<AngleResult> AngleResults { get; } = new();

        public Dictionary<string, object> SharedState { get; } = new();
    }
}
```

- [ ] **Step 4: Build to verify no errors**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```
feat(engine): add IFillStrategy interface and FillContext
```

---

### Task 3: Create `FillStrategyRegistry`

**Files:**
- Create: `OpenNest.Engine/Strategies/FillStrategyRegistry.cs`

- [ ] **Step 1: Write FillStrategyRegistry.cs**

Create `OpenNest.Engine/Strategies/FillStrategyRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace OpenNest
{
    public static class FillStrategyRegistry
    {
        private static readonly List<IFillStrategy> strategies = new();
        private static List<IFillStrategy> sorted;

        static FillStrategyRegistry()
        {
            LoadFrom(typeof(FillStrategyRegistry).Assembly);
        }

        public static IReadOnlyList<IFillStrategy> Strategies =>
            sorted ??= strategies.OrderBy(s => s.Order).ToList();

        public static void LoadFrom(Assembly assembly)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IFillStrategy).IsAssignableFrom(type))
                    continue;

                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    Debug.WriteLine($"[FillStrategyRegistry] Skipping {type.Name}: no parameterless constructor");
                    continue;
                }

                try
                {
                    var instance = (IFillStrategy)ctor.Invoke(null);

                    if (strategies.Any(s => s.Name.Equals(instance.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Debug.WriteLine($"[FillStrategyRegistry] Duplicate strategy '{instance.Name}' skipped");
                        continue;
                    }

                    strategies.Add(instance);
                    Debug.WriteLine($"[FillStrategyRegistry] Registered: {instance.Name} (Order={instance.Order})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FillStrategyRegistry] Failed to instantiate {type.Name}: {ex.Message}");
                }
            }

            sorted = null;
        }

        public static void LoadPlugins(string directory)
        {
            if (!Directory.Exists(directory))
                return;

            foreach (var dll in Directory.GetFiles(directory, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    LoadFrom(assembly);
                    Debug.WriteLine($"[FillStrategyRegistry] Loaded plugin assembly: {Path.GetFileName(dll)}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[FillStrategyRegistry] Failed to load {Path.GetFileName(dll)}: {ex.Message}");
                }
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify no errors**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded (no strategies registered yet — static constructor finds nothing)

- [ ] **Step 3: Commit**

```
feat(engine): add FillStrategyRegistry with reflection-based discovery
```

---

### Task 4: Extract `FillHelpers` from `DefaultNestEngine`

**Files:**
- Create: `OpenNest.Engine/Strategies/FillHelpers.cs`
- Modify: `OpenNest.Engine/DefaultNestEngine.cs` (remove `BuildRotatedPattern` and `FillPattern`)
- Modify: `OpenNest.Engine/PairFiller.cs` (update references)

- [ ] **Step 1: Create FillHelpers.cs**

Create `OpenNest.Engine/Strategies/FillHelpers.cs` with the two static methods moved from `DefaultNestEngine`:

```csharp
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest
{
    public static class FillHelpers
    {
        public static Pattern BuildRotatedPattern(List<Part> groupParts, double angle)
        {
            var pattern = new Pattern();
            var center = ((IEnumerable<IBoundable>)groupParts).GetBoundingBox().Center;

            foreach (var part in groupParts)
            {
                var clone = (Part)part.Clone();
                clone.UpdateBounds();

                if (!angle.IsEqualTo(0))
                    clone.Rotate(angle, center);

                pattern.Parts.Add(clone);
            }

            pattern.UpdateBounds();
            return pattern;
        }

        public static List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea)
        {
            var results = new ConcurrentBag<(List<Part> Parts, FillScore Score)>();

            Parallel.ForEach(angles, angle =>
            {
                var pattern = BuildRotatedPattern(groupParts, angle);

                if (pattern.Parts.Count == 0)
                    return;

                var h = engine.Fill(pattern, NestDirection.Horizontal);
                if (h != null && h.Count > 0)
                    results.Add((h, FillScore.Compute(h, workArea)));

                var v = engine.Fill(pattern, NestDirection.Vertical);
                if (v != null && v.Count > 0)
                    results.Add((v, FillScore.Compute(v, workArea)));
            });

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var res in results)
            {
                if (best == null || res.Score > bestScore)
                {
                    best = res.Parts;
                    bestScore = res.Score;
                }
            }

            return best;
        }
    }
}
```

- [ ] **Step 2: Update DefaultNestEngine to delegate to FillHelpers**

In `OpenNest.Engine/DefaultNestEngine.cs`:
- Change `BuildRotatedPattern` and `FillPattern` to forward to `FillHelpers`:

```csharp
internal static Pattern BuildRotatedPattern(List<Part> groupParts, double angle)
    => FillHelpers.BuildRotatedPattern(groupParts, angle);

internal static List<Part> FillPattern(FillLinear engine, List<Part> groupParts, List<double> angles, Box workArea)
    => FillHelpers.FillPattern(engine, groupParts, angles, workArea);
```

This preserves the existing `internal static` API so `PairFiller` and `Fill(List<Part> groupParts, ...)` don't break.

- [ ] **Step 3: Build and run tests**

Run: `dotnet build OpenNest.sln && dotnet test OpenNest.Tests`
Expected: Build succeeded, all tests pass

- [ ] **Step 4: Commit**

```
refactor(engine): extract FillHelpers from DefaultNestEngine
```

---

### Task 5: Create `PairsFillStrategy`

**Files:**
- Create: `OpenNest.Engine/Strategies/PairsFillStrategy.cs`

- [ ] **Step 1: Write PairsFillStrategy.cs**

Create `OpenNest.Engine/Strategies/PairsFillStrategy.cs`:

```csharp
using System.Collections.Generic;
using OpenNest.Engine.BestFit;

namespace OpenNest
{
    public class PairsFillStrategy : IFillStrategy
    {
        public string Name => "Pairs";
        public NestPhase Phase => NestPhase.Pairs;
        public int Order => 100;

        public List<Part> Fill(FillContext context)
        {
            var filler = new PairFiller(context.Plate.Size, context.Plate.PartSpacing);
            var result = filler.Fill(context.Item, context.WorkArea,
                context.PlateNumber, context.Token, context.Progress);

            // Cache hit — PairFiller already called GetOrCompute internally.
            var bestFits = BestFitCache.GetOrCompute(
                context.Item.Drawing, context.Plate.Size.Length,
                context.Plate.Size.Width, context.Plate.PartSpacing);
            context.SharedState["BestFits"] = bestFits;

            return result;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded (strategy auto-registered by `FillStrategyRegistry`)

- [ ] **Step 3: Commit**

```
feat(engine): add PairsFillStrategy adapter
```

---

### Task 6: Create `RectBestFitStrategy`

**Files:**
- Create: `OpenNest.Engine/Strategies/RectBestFitStrategy.cs`

- [ ] **Step 1: Write RectBestFitStrategy.cs**

Create `OpenNest.Engine/Strategies/RectBestFitStrategy.cs`:

```csharp
using System.Collections.Generic;
using OpenNest.RectanglePacking;

namespace OpenNest
{
    public class RectBestFitStrategy : IFillStrategy
    {
        public string Name => "RectBestFit";
        public NestPhase Phase => NestPhase.RectBestFit;
        public int Order => 200;

        public List<Part> Fill(FillContext context)
        {
            var binItem = BinConverter.ToItem(context.Item, context.Plate.PartSpacing);
            var bin = BinConverter.CreateBin(context.WorkArea, context.Plate.PartSpacing);

            var engine = new FillBestFit(bin);
            engine.Fill(binItem);

            return BinConverter.ToParts(bin, new List<NestItem> { context.Item });
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```
feat(engine): add RectBestFitStrategy adapter
```

---

### Task 7: Create `ExtentsFillStrategy`

**Files:**
- Create: `OpenNest.Engine/Strategies/ExtentsFillStrategy.cs`

- [ ] **Step 1: Write ExtentsFillStrategy.cs**

Create `OpenNest.Engine/Strategies/ExtentsFillStrategy.cs`:

```csharp
using System.Collections.Generic;
using OpenNest.Engine.BestFit;
using OpenNest.Math;

namespace OpenNest
{
    public class ExtentsFillStrategy : IFillStrategy
    {
        public string Name => "Extents";
        public NestPhase Phase => NestPhase.Extents;
        public int Order => 300;

        public List<Part> Fill(FillContext context)
        {
            var filler = new FillExtents(context.WorkArea, context.Plate.PartSpacing);

            var bestRotation = context.SharedState.TryGetValue("BestRotation", out var rot)
                ? (double)rot
                : RotationAnalysis.FindBestRotation(context.Item);

            var angles = new[] { bestRotation, bestRotation + Angle.HalfPI };

            var bestFits = context.SharedState.TryGetValue("BestFits", out var cached)
                ? (List<BestFitResult>)cached
                : null;

            List<Part> best = null;
            var bestScore = default(FillScore);

            foreach (var angle in angles)
            {
                context.Token.ThrowIfCancellationRequested();
                var result = filler.Fill(context.Item.Drawing, angle,
                    context.PlateNumber, context.Token, context.Progress, bestFits);
                if (result != null && result.Count > 0)
                {
                    var score = FillScore.Compute(result, context.WorkArea);
                    if (best == null || score > bestScore)
                    {
                        best = result;
                        bestScore = score;
                    }
                }
            }

            return best ?? new List<Part>();
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```
feat(engine): add ExtentsFillStrategy adapter
```

---

### Task 8: Create `LinearFillStrategy`

**Files:**
- Create: `OpenNest.Engine/Strategies/LinearFillStrategy.cs`

- [ ] **Step 1: Write LinearFillStrategy.cs**

Create `OpenNest.Engine/Strategies/LinearFillStrategy.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using OpenNest.Math;

namespace OpenNest
{
    public class LinearFillStrategy : IFillStrategy
    {
        public string Name => "Linear";
        public NestPhase Phase => NestPhase.Linear;
        public int Order => 400;

        public List<Part> Fill(FillContext context)
        {
            var angles = context.SharedState.TryGetValue("AngleCandidates", out var cached)
                ? (List<double>)cached
                : new List<double> { 0, Angle.HalfPI };

            var workArea = context.WorkArea;
            List<Part> best = null;
            var bestScore = default(FillScore);

            for (var ai = 0; ai < angles.Count; ai++)
            {
                context.Token.ThrowIfCancellationRequested();

                var angle = angles[ai];
                var engine = new FillLinear(workArea, context.Plate.PartSpacing);
                var h = engine.Fill(context.Item.Drawing, angle, NestDirection.Horizontal);
                var v = engine.Fill(context.Item.Drawing, angle, NestDirection.Vertical);

                var angleDeg = Angle.ToDegrees(angle);

                if (h != null && h.Count > 0)
                {
                    var scoreH = FillScore.Compute(h, workArea);
                    context.AngleResults.Add(new AngleResult
                    {
                        AngleDeg = angleDeg,
                        Direction = NestDirection.Horizontal,
                        PartCount = h.Count
                    });

                    if (best == null || scoreH > bestScore)
                    {
                        best = h;
                        bestScore = scoreH;
                    }
                }

                if (v != null && v.Count > 0)
                {
                    var scoreV = FillScore.Compute(v, workArea);
                    context.AngleResults.Add(new AngleResult
                    {
                        AngleDeg = angleDeg,
                        Direction = NestDirection.Vertical,
                        PartCount = v.Count
                    });

                    if (best == null || scoreV > bestScore)
                    {
                        best = v;
                        bestScore = scoreV;
                    }
                }

                NestEngineBase.ReportProgress(context.Progress, NestPhase.Linear,
                    context.PlateNumber, best, workArea,
                    $"Linear: {ai + 1}/{angles.Count} angles, {angleDeg:F0}° best = {bestScore.Count} parts");
            }

            return best ?? new List<Part>();
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```
feat(engine): add LinearFillStrategy adapter
```

---

### Task 9: Wire `RunPipeline` into `DefaultNestEngine`

**Files:**
- Modify: `OpenNest.Engine/DefaultNestEngine.cs`

This is the key refactoring step. Replace `FindBestFill` with `RunPipeline` and delete dead code.

- [ ] **Step 1: Replace FindBestFill with RunPipeline**

Delete the entire `FindBestFill` method (the large `private List<Part> FindBestFill(...)` method). Replace with:

```csharp
private void RunPipeline(FillContext context)
{
    var bestRotation = RotationAnalysis.FindBestRotation(context.Item);
    context.SharedState["BestRotation"] = bestRotation;

    var angles = angleBuilder.Build(context.Item, bestRotation, context.WorkArea);
    context.SharedState["AngleCandidates"] = angles;

    try
    {
        foreach (var strategy in FillStrategyRegistry.Strategies)
        {
            context.Token.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            var result = strategy.Fill(context);
            sw.Stop();

            var phaseResult = new PhaseResult(
                strategy.Phase, result?.Count ?? 0, sw.ElapsedMilliseconds);
            context.PhaseResults.Add(phaseResult);

            // Keep engine's PhaseResults in sync so BuildProgressSummary() works
            // during progress reporting.
            PhaseResults.Add(phaseResult);

            if (IsBetterFill(result, context.CurrentBest, context.WorkArea))
            {
                context.CurrentBest = result;
                context.CurrentBestScore = FillScore.Compute(result, context.WorkArea);
                context.WinnerPhase = strategy.Phase;
                ReportProgress(context.Progress, strategy.Phase, PlateNumber,
                    result, context.WorkArea, BuildProgressSummary());
            }
        }
    }
    catch (OperationCanceledException)
    {
        Debug.WriteLine("[RunPipeline] Cancelled, returning current best");
    }

    angleBuilder.RecordProductive(context.AngleResults);
}
```

- [ ] **Step 2: Update Fill(NestItem, ...) to use RunPipeline**

Replace the body of the `Fill(NestItem item, Box workArea, ...)` override:

```csharp
public override List<Part> Fill(NestItem item, Box workArea,
    IProgress<NestProgress> progress, CancellationToken token)
{
    PhaseResults.Clear();
    AngleResults.Clear();

    var context = new FillContext
    {
        Item = item,
        WorkArea = workArea,
        Plate = Plate,
        PlateNumber = PlateNumber,
        Token = token,
        Progress = progress,
    };

    RunPipeline(context);

    // PhaseResults already synced during RunPipeline.
    AngleResults.AddRange(context.AngleResults);
    WinnerPhase = context.WinnerPhase;

    var best = context.CurrentBest ?? new List<Part>();

    if (item.Quantity > 0 && best.Count > item.Quantity)
        best = best.Take(item.Quantity).ToList();

    ReportProgress(progress, WinnerPhase, PlateNumber, best, workArea, BuildProgressSummary());

    return best;
}
```

- [ ] **Step 3: Delete FillRectangleBestFit**

Remove the private `FillRectangleBestFit` method entirely. It is now inside `RectBestFitStrategy`.

Note: `Fill(List<Part> groupParts, ...)` also calls `FillRectangleBestFit` at line 125. Inline the logic there:

```csharp
var binItem = BinConverter.ToItem(nestItem, Plate.PartSpacing);
var bin = BinConverter.CreateBin(workArea, Plate.PartSpacing);
var rectEngine = new FillBestFit(bin);
rectEngine.Fill(binItem);
var rectResult = BinConverter.ToParts(bin, new List<NestItem> { nestItem });
```

- [ ] **Step 4: Delete QuickFillCount**

Remove the `QuickFillCount` method entirely (dead code, zero callers).

- [ ] **Step 5: Build and run tests**

Run: `dotnet build OpenNest.sln && dotnet test OpenNest.Tests`
Expected: Build succeeded, **all existing tests pass** — this is the critical regression gate.

- [ ] **Step 6: Commit**

```
refactor(engine): replace FindBestFill with strategy pipeline

DefaultNestEngine.Fill(NestItem, ...) now delegates to RunPipeline
which iterates FillStrategyRegistry.Strategies in order.

Removed: FindBestFill, FillRectangleBestFit, QuickFillCount.
Kept: AngleCandidateBuilder, ForceFullAngleSweep, group-fill overload.
```

---

### Task 10: Add pipeline-specific tests

**Files:**
- Create: `OpenNest.Tests/Strategies/FillStrategyRegistryTests.cs`
- Create: `OpenNest.Tests/Strategies/FillPipelineTests.cs`

- [ ] **Step 1: Write FillStrategyRegistryTests.cs**

Create `OpenNest.Tests/Strategies/FillStrategyRegistryTests.cs`:

```csharp

namespace OpenNest.Tests.Strategies;

public class FillStrategyRegistryTests
{
    [Fact]
    public void Registry_DiscoversBuiltInStrategies()
    {
        var strategies = FillStrategyRegistry.Strategies;

        Assert.True(strategies.Count >= 4, $"Expected at least 4 built-in strategies, got {strategies.Count}");
        Assert.Contains(strategies, s => s.Name == "Pairs");
        Assert.Contains(strategies, s => s.Name == "RectBestFit");
        Assert.Contains(strategies, s => s.Name == "Extents");
        Assert.Contains(strategies, s => s.Name == "Linear");
    }

    [Fact]
    public void Registry_StrategiesAreOrderedByOrder()
    {
        var strategies = FillStrategyRegistry.Strategies;

        for (var i = 1; i < strategies.Count; i++)
            Assert.True(strategies[i].Order >= strategies[i - 1].Order,
                $"Strategy '{strategies[i].Name}' (Order={strategies[i].Order}) should not precede '{strategies[i - 1].Name}' (Order={strategies[i - 1].Order})");
    }

    [Fact]
    public void Registry_LinearIsLast()
    {
        var strategies = FillStrategyRegistry.Strategies;
        var last = strategies[strategies.Count - 1];

        Assert.Equal("Linear", last.Name);
    }
}
```

- [ ] **Step 2: Write FillPipelineTests.cs**

Create `OpenNest.Tests/Strategies/FillPipelineTests.cs`:

```csharp
using OpenNest.Geometry;

namespace OpenNest.Tests.Strategies;

public class FillPipelineTests
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
    public void Pipeline_PopulatesPhaseResults()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(engine.PhaseResults.Count >= 4,
            $"Expected phase results from all strategies, got {engine.PhaseResults.Count}");
    }

    [Fact]
    public void Pipeline_SetsWinnerPhase()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };

        var parts = engine.Fill(item, plate.WorkArea(), null, System.Threading.CancellationToken.None);

        Assert.True(parts.Count > 0);
        // WinnerPhase should be set to one of the built-in phases
        Assert.True(engine.WinnerPhase == NestPhase.Pairs ||
                    engine.WinnerPhase == NestPhase.Linear ||
                    engine.WinnerPhase == NestPhase.RectBestFit ||
                    engine.WinnerPhase == NestPhase.Extents);
    }

    [Fact]
    public void Pipeline_RespectsCancellation()
    {
        var plate = new Plate(120, 60);
        var engine = new DefaultNestEngine(plate);
        var item = new NestItem { Drawing = MakeRectDrawing(20, 10) };
        var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        // Pre-cancelled token should return empty or partial results without throwing
        var parts = engine.Fill(item, plate.WorkArea(), null, cts.Token);

        // Should not throw — graceful degradation
        Assert.NotNull(parts);
    }
}
```

- [ ] **Step 3: Run all tests**

Run: `dotnet test OpenNest.Tests`
Expected: All tests pass (old and new)

- [ ] **Step 4: Commit**

```
test(engine): add FillStrategyRegistry and pipeline tests
```

---

### Task 11: Final verification

- [ ] **Step 1: Run full test suite**

Run: `dotnet test OpenNest.Tests -v normal`
Expected: All tests pass

- [ ] **Step 2: Build entire solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded, no warnings from Engine project

- [ ] **Step 3: Verify EngineRefactorSmokeTests still pass**

Run: `dotnet test OpenNest.Tests --filter EngineRefactorSmokeTests`
Expected: All 5 smoke tests pass (DefaultEngine_FillNestItem, DefaultEngine_FillGroupParts, DefaultEngine_ForceFullAngleSweep, StripEngine_Nest, BruteForceRunner_StillWorks)

- [ ] **Step 4: Verify file layout matches spec**

Confirm these files exist under `OpenNest.Engine/Strategies/`:
- `IFillStrategy.cs`
- `FillContext.cs`
- `FillStrategyRegistry.cs`
- `FillHelpers.cs`
- `PairsFillStrategy.cs`
- `RectBestFitStrategy.cs`
- `ExtentsFillStrategy.cs`
- `LinearFillStrategy.cs`
