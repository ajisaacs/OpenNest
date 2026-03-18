# Pluggable Fill Strategies Design

## Problem

`DefaultNestEngine.FindBestFill` is a monolithic method that hard-wires four fill phases (Pairs, Linear, RectBestFit, Extents) in a fixed order. Adding a new fill strategy or changing the execution order requires modifying `DefaultNestEngine` directly. The Linear phase is expensive and rarely wins, but there's no way to skip or reorder it without editing the orchestration code.

## Goal

Extract fill strategies into pluggable components behind a common interface. Engines compose strategies in a pipeline where each strategy receives the current best result from prior strategies and can decide whether to run. New strategies can be added by implementing the interface — including from plugin DLLs discovered via reflection.

## Scope

This refactoring targets only the **single-item fill path** (`DefaultNestEngine.FindBestFill`, called from the `Fill(NestItem, ...)` overload). The following are explicitly **out of scope** and remain unchanged:

- `Fill(List<Part> groupParts, ...)` — group-fill overload, has its own inline orchestration with different conditions (multi-phase block only runs when `groupParts.Count == 1`). May be refactored to use strategies in a future pass once the single-item pipeline is proven.
- `PackArea` — packing is a different operation (bin-packing single-quantity items).
- `Nest` — multi-item orchestration on `NestEngineBase`, uses `Fill` and `PackArea` as building blocks.

## Design

### `IFillStrategy` Interface

```csharp
public interface IFillStrategy
{
    string Name { get; }
    NestPhase Phase { get; }
    int Order { get; }  // lower runs first; gaps of 100 for plugin insertion
    List<Part> Fill(FillContext context);
}
```

Strategies must be **stateless**. All mutable state lives in `FillContext`. This avoids leaking state between calls when strategies are shared across invocations.

Strategies **may** call `NestEngineBase.ReportProgress` for intermediate progress updates (e.g., `LinearFillStrategy` reports per-angle progress). The `FillContext` carries `Progress` and `PlateNumber` for this purpose. The pipeline orchestrator reports progress only when the overall best improves; strategies report their own internal progress as they work.

For plugin strategies that don't map to a built-in `NestPhase`, use `NestPhase.Custom` (a new enum value added as part of this work). The `Name` property provides the human-readable label.

### `FillContext`

Carries inputs and pipeline state through the strategy chain:

```csharp
public class FillContext
{
    // Inputs
    public NestItem Item { get; init; }
    public Box WorkArea { get; init; }
    public Plate Plate { get; init; }
    public int PlateNumber { get; init; }
    public CancellationToken Token { get; init; }
    public IProgress<NestProgress> Progress { get; init; }

    // Pipeline state
    public List<Part> CurrentBest { get; set; }
    public FillScore CurrentBestScore { get; set; }
    public NestPhase WinnerPhase { get; set; }
    public List<PhaseResult> PhaseResults { get; } = new();
    public List<AngleResult> AngleResults { get; } = new();

    // Shared resources (populated by earlier strategies, available to later ones)
    public Dictionary<string, object> SharedState { get; } = new();
}
```

`SharedState` enables cross-strategy data sharing without direct coupling. Well-known keys:

| Key | Type | Producer | Consumer |
|-----|------|----------|----------|
| `"BestFits"` | `List<BestFitResult>` | `PairsFillStrategy` | `ExtentsFillStrategy` |
| `"BestRotation"` | `double` | Pipeline setup | `ExtentsFillStrategy`, `LinearFillStrategy` |
| `"AngleCandidates"` | `List<double>` | Pipeline setup | `LinearFillStrategy` |

### Pipeline Setup

Before iterating strategies, `RunPipeline` performs shared pre-computation and stores results in `SharedState`:

```csharp
private void RunPipeline(FillContext context)
{
    // Pre-pipeline setup: shared across strategies
    var bestRotation = RotationAnalysis.FindBestRotation(context.Item);
    context.SharedState["BestRotation"] = bestRotation;

    var angles = angleBuilder.Build(context.Item, bestRotation, context.WorkArea);
    context.SharedState["AngleCandidates"] = angles;

    foreach (var strategy in FillStrategyRegistry.Strategies)
    {
        // ... strategy loop ...
    }

    // Post-pipeline: record productive angles for cross-run learning
    angleBuilder.RecordProductive(context.AngleResults);
}
```

The `AngleCandidateBuilder` instance stays on `DefaultNestEngine` (not inside a strategy) because it accumulates cross-run learning state via `RecordProductive`. Strategies read the pre-built angle list from `SharedState["AngleCandidates"]`.

### `FillStrategyRegistry`

Discovers strategies via reflection, similar to `NestEngineRegistry.LoadPlugins`. Stores strategy **instances** (not factories) because strategies are stateless:

```csharp
public static class FillStrategyRegistry
{
    private static readonly List<IFillStrategy> strategies = new();

    static FillStrategyRegistry()
    {
        LoadFrom(typeof(FillStrategyRegistry).Assembly);
    }

    private static List<IFillStrategy> sorted;

    public static IReadOnlyList<IFillStrategy> Strategies =>
        sorted ??= strategies.OrderBy(s => s.Order).ToList();

    public static void LoadFrom(Assembly assembly)
    {
        /* scan for IFillStrategy implementations */
        sorted = null; // invalidate cache
    }

    public static void LoadPlugins(string directory)
    {
        /* load DLLs and scan each */
        sorted = null; // invalidate cache
    }
}
```

Strategy plugins use a `Strategies/` directory (separate from the `Engines/` directory used by `NestEngineRegistry`). Note: plugin strategies cannot use `internal` types like `BinConverter` from `OpenNest.Engine`. If a plugin needs rectangle packing, `BinConverter` would need to be made `public` — defer this until a plugin actually needs it.

### Built-in Strategy Order

| Strategy | Order | Notes |
|----------|-------|-------|
| `PairsFillStrategy` | 100 | Populates `SharedState["BestFits"]` for Extents |
| `RectBestFitStrategy` | 200 | |
| `ExtentsFillStrategy` | 300 | Reads `SharedState["BestFits"]` from Pairs |
| `LinearFillStrategy` | 400 | Expensive, rarely wins, runs last |

Gaps of 100 allow plugins to slot in between (e.g., Order 150 runs after Pairs, before RectBestFit).

### Strategy Implementations

Each strategy is a thin stateless adapter around the existing filler class. Strategies construct filler instances using `context.Plate` properties:

```csharp
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

        // Share the BestFitCache for Extents to use later.
        // This is a cache hit (PairFiller already called GetOrCompute internally),
        // so it's a dictionary lookup, not a recomputation.
        var bestFits = BestFitCache.GetOrCompute(
            context.Item.Drawing, context.Plate.Size.Length,
            context.Plate.Size.Width, context.Plate.PartSpacing);
        context.SharedState["BestFits"] = bestFits;

        return result;
    }
}
```

Summary of all four:

- **`PairsFillStrategy`** — constructs `PairFiller(context.Plate.Size, context.Plate.PartSpacing)`, stores `BestFitCache` in `SharedState`
- **`RectBestFitStrategy`** — uses `BinConverter.ToItem(item, partSpacing)` and `BinConverter.CreateBin(workArea, partSpacing)` to delegate to `FillBestFit`
- **`ExtentsFillStrategy`** — constructs `FillExtents(context.WorkArea, context.Plate.PartSpacing)`, reads `SharedState["BestRotation"]` for angles, reads `SharedState["BestFits"]` from Pairs
- **`LinearFillStrategy`** — constructs `FillLinear(context.WorkArea, context.Plate.PartSpacing)`, reads `SharedState["AngleCandidates"]` for angle list. Internally iterates all angle candidates, tracks its own best, writes per-angle `AngleResults` to context, and calls `ReportProgress` for per-angle updates (preserving the existing UX). Returns only its single best result.

The underlying classes (`PairFiller`, `FillLinear`, `FillExtents`, `FillBestFit`) are unchanged.

### Changes to `DefaultNestEngine`

`FindBestFill` is replaced by `RunPipeline`:

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

            context.PhaseResults.Add(new PhaseResult(
                strategy.Phase, result?.Count ?? 0, sw.ElapsedMilliseconds));

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
        // Graceful degradation: return whatever best has been accumulated so far.
        Debug.WriteLine("[RunPipeline] Cancelled, returning current best");
    }

    angleBuilder.RecordProductive(context.AngleResults);
}
```

After `RunPipeline`, the engine copies `context.PhaseResults` and `context.AngleResults` back to the `NestEngineBase` properties so existing UI and test consumers continue to work:

```csharp
PhaseResults.AddRange(context.PhaseResults);
AngleResults.AddRange(context.AngleResults);
WinnerPhase = context.WinnerPhase;
```

**Removed from `DefaultNestEngine`:**
- `FindBestFill` method (replaced by `RunPipeline`)
- `FillRectangleBestFit` method (moves into `RectBestFitStrategy`)
- `QuickFillCount` method (dead code — has zero callers, delete it)

**Stays on `DefaultNestEngine`:**
- `AngleCandidateBuilder` field — owns cross-run learning state, used in pipeline setup/teardown
- `ForceFullAngleSweep` property — forwards to `angleBuilder.ForceFullSweep`, keeps existing public API for `BruteForceRunner` and tests
- `Fill(List<Part> groupParts, ...)` overload — out of scope (see Scope section)
- `PackArea` — out of scope

**Static helpers `BuildRotatedPattern` and `FillPattern`** move to `Strategies/FillHelpers.cs`.

### File Layout

```
OpenNest.Engine/
  Strategies/
    IFillStrategy.cs
    FillContext.cs
    FillStrategyRegistry.cs
    FillHelpers.cs
    PairsFillStrategy.cs
    LinearFillStrategy.cs
    RectBestFitStrategy.cs
    ExtentsFillStrategy.cs
```

### What Doesn't Change

- `PairFiller.cs`, `FillLinear.cs`, `FillExtents.cs`, `RectanglePacking/FillBestFit.cs` — underlying implementations
- `FillScore.cs`, `NestProgress.cs`, `Compactor.cs` — shared infrastructure
- `NestEngineBase.cs` — base class
- `NestEngineRegistry.cs` — engine-level registry (separate concern)
- `StripNestEngine.cs` — delegates to `DefaultNestEngine` internally

### Minor Changes to `NestPhase`

Add `Custom` to the `NestPhase` enum for plugin strategies that don't map to a built-in phase:

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

### Testing

- Existing `EngineRefactorSmokeTests` serve as the regression gate — they must pass unchanged after refactoring.
- `BruteForceRunner` continues to access `ForceFullAngleSweep` via the forwarding property on `DefaultNestEngine`.
- Individual strategy adapters do not need their own unit tests initially — the existing smoke tests cover the end-to-end pipeline. Strategy-level tests can be added as the strategy count grows.
