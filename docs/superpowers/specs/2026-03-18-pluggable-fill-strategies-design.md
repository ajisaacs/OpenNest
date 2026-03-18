# Pluggable Fill Strategies Design

## Problem

`DefaultNestEngine.FindBestFill` is a monolithic method that hard-wires four fill phases (Pairs, Linear, RectBestFit, Extents) in a fixed order. Adding a new fill strategy or changing the execution order requires modifying `DefaultNestEngine` directly. The Linear phase is expensive and rarely wins, but there's no way to skip or reorder it without editing the orchestration code.

## Goal

Extract fill strategies into pluggable components behind a common interface. Engines compose strategies in a pipeline where each strategy receives the current best result from prior strategies and can decide whether to run. New strategies can be added by implementing the interface — including from plugin DLLs discovered via reflection.

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

`SharedState` enables cross-strategy data sharing without direct coupling. For example, `PairsFillStrategy` stores the `BestFitCache` results and `ExtentsFillStrategy` retrieves them.

### `FillStrategyRegistry`

Discovers strategies via reflection, similar to `NestEngineRegistry.LoadPlugins`:

```csharp
public static class FillStrategyRegistry
{
    private static readonly List<IFillStrategy> strategies = new();

    static FillStrategyRegistry()
    {
        LoadFrom(typeof(FillStrategyRegistry).Assembly);
    }

    public static IReadOnlyList<IFillStrategy> Strategies =>
        strategies.OrderBy(s => s.Order).ToList();

    public static void LoadFrom(Assembly assembly) { /* scan for IFillStrategy implementations */ }

    public static void LoadPlugins(string directory) { /* load DLLs and scan each */ }
}
```

### Built-in Strategy Order

| Strategy | Order | Notes |
|----------|-------|-------|
| `PairsFillStrategy` | 100 | Populates `SharedState["BestFits"]` for Extents |
| `RectBestFitStrategy` | 200 | |
| `ExtentsFillStrategy` | 300 | Reads `SharedState["BestFits"]` from Pairs |
| `LinearFillStrategy` | 400 | Expensive, rarely wins, runs last |

Gaps of 100 allow plugins to slot in between (e.g., Order 150 runs after Pairs, before RectBestFit).

### Strategy Implementations

Each strategy is a thin adapter around the existing filler class:

- **`PairsFillStrategy`** — wraps `PairFiller`, stores `BestFitCache` in `SharedState`
- **`RectBestFitStrategy`** — wraps `FillBestFit` via `BinConverter`
- **`ExtentsFillStrategy`** — wraps `FillExtents`, reads shared `BestFitCache`
- **`LinearFillStrategy`** — wraps `FillLinear` + `AngleCandidateBuilder`

The underlying classes (`PairFiller`, `FillLinear`, `FillExtents`, `FillBestFit`) are unchanged.

### Changes to `DefaultNestEngine`

`FindBestFill` is replaced by `RunPipeline`:

```csharp
private void RunPipeline(FillContext context)
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
```

**Removed from `DefaultNestEngine`:**
- `FindBestFill` method (replaced by `RunPipeline`)
- `FillRectangleBestFit` method (moves into `RectBestFitStrategy`)
- `QuickFillCount` method (moves into relevant strategy)
- `AngleCandidateBuilder` field and `ForceFullAngleSweep` property (move into `LinearFillStrategy`)

**Stays in `DefaultNestEngine`:**
- `Fill(List<Part> groupParts, ...)` overload — separate group-fill concern
- `PackArea` — packing is not part of the fill pipeline

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
