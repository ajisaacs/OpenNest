# Engine Refactor: Extract Shared Algorithms from DefaultNestEngine and StripNestEngine

## Problem

`DefaultNestEngine` (~550 lines) mixes phase orchestration with strategy-specific logic (pair candidate selection, angle building, pattern helpers). `StripNestEngine` (~450 lines) duplicates patterns that DefaultNestEngine also uses: shrink-to-fit loops, iterative remnant filling, and progress accumulation. Both engines would benefit from extracting shared algorithms into focused, reusable classes.

## Approach

Extract five classes from the two engines. No new interfaces or strategy patterns — just focused helper classes that each engine composes.

## Extracted Classes

### 1. PairFiller

**Source:** DefaultNestEngine lines 362-489 (`FillWithPairs`, `SelectPairCandidates`, `BuildRemainderPatterns`, `MinPairCandidates`, `PairTimeLimit`).

**API:**
```csharp
public class PairFiller
{
    public PairFiller(Size plateSize, double partSpacing) { }

    public List<Part> Fill(NestItem item, Box workArea,
        int plateNumber = 0,
        CancellationToken token = default,
        IProgress<NestProgress> progress = null);
}
```

**Details:**
- Constructor takes plate size and spacing — decoupled from `Plate` object.
- `SelectPairCandidates` and `BuildRemainderPatterns` become private methods.
- Uses `BestFitCache.GetOrCompute()` internally (same as today).
- Calls `BuildRotatedPattern` and `FillPattern` — these become `internal static` methods on DefaultNestEngine so PairFiller can call them without ceremony.
- Returns `List<Part>` (empty list if no result), same contract as today.
- Progress reporting: PairFiller accepts `IProgress<NestProgress>` and `int plateNumber` in its `Fill` method to maintain per-candidate progress updates. The caller passes these through from the engine.

**Caller:** `DefaultNestEngine.FindBestFill` replaces `this.FillWithPairs(...)` with `new PairFiller(Plate.Size, Plate.PartSpacing).Fill(...)`.

### 2. AngleCandidateBuilder

**Source:** DefaultNestEngine lines 279-347 (`BuildCandidateAngles`, `knownGoodAngles` HashSet, `ForceFullAngleSweep` property).

**API:**
```csharp
public class AngleCandidateBuilder
{
    public bool ForceFullSweep { get; set; }

    public List<double> Build(NestItem item, double bestRotation, Box workArea);

    public void RecordProductive(List<AngleResult> angleResults);
}
```

**Details:**
- Owns `knownGoodAngles` state — lives as long as the engine instance so pruning accumulates across fills.
- `Build()` encapsulates the full pipeline: base angles, sweep check, ML prediction, known-good pruning.
- `RecordProductive()` replaces the inline loop that feeds `knownGoodAngles` after the linear phase.
- `ForceFullAngleSweep` moves from DefaultNestEngine to `AngleCandidateBuilder.ForceFullSweep`. DefaultNestEngine keeps a forwarding property `ForceFullAngleSweep` that delegates to its `AngleCandidateBuilder` instance, so `BruteForceRunner` (which sets `engine.ForceFullAngleSweep = true`) continues to work without changes.

**Caller:** DefaultNestEngine creates one `AngleCandidateBuilder` instance as a field and calls `Build()`/`RecordProductive()` from `FindBestFill`.

### 3. ShrinkFiller

**Source:** StripNestEngine `TryOrientation` shrink loop (lines 188-215) and `ShrinkFill` (lines 358-418).

**API:**
```csharp
public static class ShrinkFiller
{
    public static ShrinkResult Shrink(
        Func<NestItem, Box, List<Part>> fillFunc,
        NestItem item, Box box,
        double spacing,
        ShrinkAxis axis,
        CancellationToken token = default,
        int maxIterations = 20);
}

public enum ShrinkAxis { Width, Height }

public class ShrinkResult
{
    public List<Part> Parts { get; set; }
    public double Dimension { get; set; }
}
```

**Details:**
- `fillFunc` delegate decouples ShrinkFiller from any specific engine — the caller provides how to fill.
- `ShrinkAxis` determines which dimension to reduce. `TryOrientation` maps strip direction to axis: `StripDirection.Bottom` → `ShrinkAxis.Height`, `StripDirection.Left` → `ShrinkAxis.Width`. `ShrinkFill` calls `Shrink` twice (width then height).
- Loop logic: fill initial box, measure placed bounding box, reduce dimension by `spacing`, retry until count drops below initial count. Dimension is measured as `placedBox.Right - box.X` for Width or `placedBox.Top - box.Y` for Height.
- Returns both the best parts and the final tight dimension (needed by `TryOrientation` to compute the remnant box).
- **Two-axis independence:** When `ShrinkFill` calls `Shrink` twice, each axis shrinks against the **original** box dimensions, not the result of the prior axis. This preserves the current behavior where width and height are shrunk independently.

**Callers:**
- `StripNestEngine.TryOrientation` replaces its inline shrink loop.
- `StripNestEngine.ShrinkFill` replaces its two-axis inline shrink loops.

### 4. RemnantFiller

**Source:** StripNestEngine remnant loop (lines 253-343) and the simpler version in NestEngineBase.Nest (lines 74-97).

**API:**
```csharp
public class RemnantFiller
{
    public RemnantFiller(Box workArea, double spacing) { }

    public void AddObstacles(IEnumerable<Part> parts);

    public List<Part> FillItems(
        List<NestItem> items,
        Func<NestItem, Box, List<Part>> fillFunc,
        CancellationToken token = default,
        IProgress<NestProgress> progress = null);
}
```

**Details:**
- Owns a `RemnantFinder` instance internally.
- `AddObstacles` registers already-placed parts (bounding boxes offset by spacing).
- `FillItems` runs the iterative loop: find remnants, try each item in each remnant, fill, update obstacles, repeat until no progress.
- Local quantity tracking (dictionary keyed by drawing name) stays internal — does not mutate the input `NestItem` quantities. Returns the placed parts; the caller deducts quantities.
- Uses minimum-remnant-size filtering (smallest remaining part dimension), same as StripNestEngine today.
- `fillFunc` delegate allows callers to provide any fill strategy (DefaultNestEngine.Fill, ShrinkFill, etc.).

**Callers:**
- `StripNestEngine.TryOrientation` replaces its inline remnant loop with `RemnantFiller.FillItems(...)`.
- `NestEngineBase.Nest` replaces its hand-rolled largest-remnant loop. **Note:** This is a deliberate behavioral improvement — the base class currently uses only the single largest remnant, while `RemnantFiller` tries all remnants iteratively with minimum-size filtering. This may produce better fill results for engines that rely on the base `Nest` method.

**Unchanged:** `NestEngineBase.Nest` phase 2 (bin-packing single-quantity items via `PackArea`, lines 100-119) is not affected by this change.

### 5. AccumulatingProgress

**Source:** StripNestEngine nested class (lines 425-449).

**API:**
```csharp
internal class AccumulatingProgress : IProgress<NestProgress>
{
    public AccumulatingProgress(IProgress<NestProgress> inner, List<Part> previousParts) { }
    public void Report(NestProgress value);
}
```

**Details:**
- Moved from private nested class to standalone `internal` class in OpenNest.Engine.
- No behavioral change — wraps an `IProgress<NestProgress>` and prepends previously placed parts to each report.

## What Stays on Each Engine

### DefaultNestEngine (~200 lines after extraction)

- `Fill(NestItem, Box, ...)` — public entry point, unchanged.
- `Fill(List<Part>, Box, ...)` — group-parts overload, unchanged.
- `PackArea` — bin packing delegation, unchanged.
- `FindBestFill` — orchestration, now ~30 lines: calls `AngleCandidateBuilder.Build()`, `PairFiller.Fill()`, linear angle loop, `FillRectangleBestFit`, picks best.
- `FillRectangleBestFit` — 6-line private method, too small to extract.
- `BuildRotatedPattern` / `FillPattern` — become `internal static`, used by both the linear loop and PairFiller.
- `QuickFillCount` — stays (used by binary search, not shared).

### StripNestEngine (~200 lines after extraction)

- `Nest` — orchestration, unchanged.
- `TryOrientation` — becomes thinner: calls `DefaultNestEngine.Fill` for initial fill, `ShrinkFiller.Shrink()` for tightening, `RemnantFiller.FillItems()` for remnants.
- `ShrinkFill` — replaced by two `ShrinkFiller.Shrink()` calls.
- `SelectStripItemIndex` / `EstimateStripDimension` — stay private, strip-specific.
- `AccumulatingProgress` — removed, uses shared class.

### NestEngineBase

- `Nest` — switches from hand-rolled remnant loop to `RemnantFiller.FillItems()`.
- All other methods unchanged.

## File Layout

All new classes go in `OpenNest.Engine/`:

```
OpenNest.Engine/
    PairFiller.cs
    AngleCandidateBuilder.cs
    ShrinkFiller.cs
    RemnantFiller.cs
    AccumulatingProgress.cs
```

## Non-Goals

- No new interfaces or strategy patterns.
- No changes to FillLinear, FillBestFit, PackBottomLeft, or any other existing algorithm.
- No changes to NestEngineRegistry or the plugin system.
- No changes to public API surface — all existing callers continue to work unchanged. One deliberate behavioral improvement: `NestEngineBase.Nest` gains multi-remnant filling (see RemnantFiller section).
- PatternHelper extraction deferred — `BuildRotatedPattern`/`FillPattern` become `internal static` on DefaultNestEngine for now. Extract if a third consumer appears.
- StripNestEngine continues to create fresh `DefaultNestEngine` instances per fill call. Sharing an `AngleCandidateBuilder` across sub-fills to enable angle pruning is a potential future optimization, not part of this refactor.
