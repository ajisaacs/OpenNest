# Two-Bucket Preview Parts

## Problem

During nesting, the PlateView preview shows whatever the latest progress report contains. When the engine runs multiple strategies sequentially (Pairs, Linear, RectBestFit, Extents), each strategy reports its own intermediate results. A later strategy starting fresh can report fewer parts than an earlier winner, causing the preview to visually regress. The user sees the part count drop and the layout change, even though the engine internally tracks the overall best.

A simple high-water-mark filter at the UI level prevents regression but freezes the preview and can diverge from the engine's actual result, causing the wrong layout to be accepted.

## Solution

Split the preview into two visual layers:

- **Stationary parts**: The overall best result found so far across all strategies. Never regresses. Drawn at full preview opacity.
- **Active parts**: The current strategy's work-in-progress. Updates freely as strategies iterate. Drawn at reduced opacity (~50% alpha).

The engine flags progress reports as `IsOverallBest` so the UI knows which bucket to update. On acceptance, the engine's returned result is used directly, not the preview state. This also fixes an existing bug where `AcceptTemporaryParts()` could accept stale preview parts instead of the engine's actual output.

## Changes

### NestProgress

Add one property:

```csharp
public bool IsOverallBest { get; set; }
```

Default `false`. Set to `true` by `RunPipeline` when reporting the overall winner, and by the final `ReportProgress` calls in `DefaultNestEngine.Fill`.

### NestEngineBase.ReportProgress

Add an optional `isOverallBest` parameter:

```csharp
internal static void ReportProgress(
    IProgress<NestProgress> progress,
    NestPhase phase,
    int plateNumber,
    List<Part> best,
    Box workArea,
    string description,
    bool isOverallBest = false)
```

Pass through to the `NestProgress` object.

### DefaultNestEngine.RunPipeline

Remove the existing `ReportProgress` call from inside the `if (IsBetterFill(...))` block. Replace it with an unconditional report of the overall best after each strategy completes:

```csharp
if (IsBetterFill(result, context.CurrentBest, context.WorkArea))
{
    context.CurrentBest = result;
    context.CurrentBestScore = FillScore.Compute(result, context.WorkArea);
    context.WinnerPhase = strategy.Phase;
}

if (context.CurrentBest != null && context.CurrentBest.Count > 0)
{
    ReportProgress(context.Progress, context.WinnerPhase, PlateNumber,
        context.CurrentBest, context.WorkArea, BuildProgressSummary(),
        isOverallBest: true);
}
```

Strategy-internal progress reports (PairFiller, LinearFillStrategy, etc.) continue using the default `isOverallBest: false`.

### DefaultNestEngine.Fill — final reports

Both `Fill` overloads have a final `ReportProgress` call after the pipeline/fill completes. These must pass `isOverallBest: true` so the final preview goes to stationary parts at full opacity:

- `Fill(NestItem, Box, ...)` line 58 — reports the pipeline winner after quantity trimming
- `Fill(List<Part>, Box, ...)` line 85 — reports the single-strategy linear result

### ColorScheme

Add two members for the active (transparent) preview style, created alongside the existing preview resources in the `PreviewPartColor` setter with the same disposal pattern:

- `ActivePreviewPartBrush` — same color as `PreviewPartBrush` at ~50% alpha
- `ActivePreviewPartPen` — same color as `PreviewPartPen` at ~50% alpha

### PlateView

Rename `temporaryParts` to `activeParts`. Add `stationaryParts` (both `List<LayoutPart>`).

**New public API:**

- `SetStationaryParts(List<Part>)` — sets the overall-best preview, calls `Invalidate()`
- `SetActiveParts(List<Part>)` — sets the current-strategy preview, calls `Invalidate()`
- `ClearPreviewParts()` — clears both lists, calls `Invalidate()` (replaces `ClearTemporaryParts()`)
- `AcceptPreviewParts(List<Part> parts)` — adds the engine's returned `parts` directly to the plate, clears both lists. Decouples acceptance from preview state.

**Internal references:** `Refresh()`, `UpdateMatrix()`, and `SetPlate()` currently reference `temporaryParts`. All must be updated to handle both `stationaryParts` and `activeParts` (clear both in `SetPlate`, update both in `Refresh`/`UpdateMatrix`).

**Drawing order in `DrawParts`:**

1. Stationary parts: `PreviewPartBrush` / `PreviewPartPen` (full opacity)
2. Active parts: `ActivePreviewPartBrush` / `ActivePreviewPartPen` (~50% alpha)

**Remove:** `SetTemporaryParts`, `ClearTemporaryParts`, `AcceptTemporaryParts`.

### Progress callbacks

All four progress callback sites (PlateView.FillWithProgress, 3x MainForm) change from:

```csharp
SetTemporaryParts(p.BestParts);
```

to:

```csharp
if (p.IsOverallBest)
    plateView.SetStationaryParts(p.BestParts);
else
    plateView.SetActiveParts(p.BestParts);
```

### Acceptance points

All acceptance points change from `AcceptTemporaryParts()` to `AcceptPreviewParts(engineResult)` where `engineResult` is the `List<Part>` returned by the engine. The multi-plate nest path in MainForm already uses `plate.Parts.AddRange(nestParts)` directly and needs no change beyond clearing preview parts.

## Threading

All `SetStationaryParts`/`SetActiveParts` calls arrive on the UI thread via `Progress<T>` (which captures `SynchronizationContext` at construction). `DrawParts` also runs on the UI thread. No concurrent access to either list.

## Files Modified

| File | Change |
|------|--------|
| `OpenNest.Engine/NestProgress.cs` | Add `IsOverallBest` property |
| `OpenNest.Engine/NestEngineBase.cs` | Add `isOverallBest` parameter to `ReportProgress` |
| `OpenNest.Engine/DefaultNestEngine.cs` | Report overall best after each strategy; flag final reports |
| `OpenNest/Controls/PlateView.cs` | Two-bucket temp parts, new API, update internal references |
| `OpenNest/Controls/ColorScheme.cs` | Add active preview brush/pen with disposal |
| `OpenNest/Forms/MainForm.cs` | Update 3 progress callbacks and acceptance points |
| `OpenNest/Actions/ActionFillArea.cs` | No change needed (uses PlateView API) |
