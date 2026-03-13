# Nesting Progress Window Design

## Problem

The auto-nest and fill operations run synchronously on the UI thread, freezing the application until complete. The user has no visibility into what the engine is doing, no way to stop early, and no preview of intermediate results.

## Solution

Run nesting on a background thread with `IProgress<NestProgress>` callbacks. Show a modeless progress dialog with current-best stats and a Stop button. Render the current best layout as temporary parts on the PlateView in a distinct preview color.

## Progress Data Model

**New file: `OpenNest.Engine/NestProgress.cs`**

A class carrying progress updates from the engine to the UI:

- `Phase` (NestPhase enum): Current strategy — `Linear`, `RectBestFit`, `Pairs`, `Remainder`
- `PlateNumber` (int): Current plate number (for auto-nest multi-plate loop)
- `BestPartCount` (int): Part count of current best result
- `BestDensity` (double): Density percentage of current best
- `UsableRemnantArea` (double): Usable remnant area of current best (matches `FillScore.UsableRemnantArea`)
- `BestParts` (List\<Part\>): Cloned snapshot of the best parts for preview

`Phase` uses a `NestPhase` enum (defined in the same file) to prevent typos and allow the progress form to map to display-friendly text (e.g., `NestPhase.Pairs` → "Trying pairs...").

`BestParts` must be a cloned list (using `Part.Clone()`) so the UI thread can safely read it while the engine continues on the background thread. The clones share `BaseDrawing` references (not deep copies of drawings) since drawings are read-only templates during nesting. Progress is reported only after each phase completes (3-4 reports per fill call), so the cloning cost is negligible.

## Engine Changes

**Modified file: `OpenNest.Engine/NestEngine.cs`**

### Return type change

The new overloads return `List<Part>` instead of `bool`. They do **not** call `Plate.Parts.AddRange()` — the caller is responsible for committing parts to the plate. This is critical because:

1. The engine runs on a background thread and must not touch `Plate.Parts` (an `ObservableList` that fires UI events).
2. It cleanly separates the "compute" phase from the "commit" phase, allowing the UI to preview results as temporary parts before committing.

Existing `bool Fill(...)` overloads remain unchanged — they delegate to the new overloads and call `Plate.Parts.AddRange()` themselves, preserving current behavior for all existing callers (MCP tools, etc.).

### Fill overloads

New signatures:

- `List<Part> Fill(NestItem item, Box workArea, IProgress<NestProgress> progress, CancellationToken token)`
- `List<Part> Fill(List<Part> groupParts, Box workArea, IProgress<NestProgress> progress, CancellationToken token)`

**Note on `Fill(List<Part>, ...)` overload:** When `groupParts.Count > 1`, only the Linear phase runs (no RectBestFit, Pairs, or Remainder). The additional phases only apply when `groupParts.Count == 1`, matching the existing engine behavior.

Inside `FindBestFill`, after each strategy completes:

1. **Linear phase**: Try all rotation angles (already uses `Parallel.ForEach`). If new best found, report progress with `Phase=Linear`. Check cancellation token.
2. **RectBestFit phase**: If new best found, report progress with `Phase=RectBestFit`. Check cancellation token.
3. **Pairs phase**: Try all pair candidates (already uses `Parallel.For`). If new best found, report progress with `Phase=Pairs`. Check cancellation token.
4. **Remainder improvement**: If new best found, report progress with `Phase=Remainder`.

### Cancellation Behavior

On cancellation, the engine returns its current best result (not null/empty). `OperationCanceledException` is caught internally so the caller always gets a usable result. This enables "stop early, keep best result."

The `CancellationToken` is also passed into `ParallelOptions` for the existing `Parallel.ForEach` (linear phase) and `Parallel.For` (pairs phase) loops, so cancellation is responsive even mid-phase rather than only at phase boundaries.

## PlateView Temporary Parts

**Modified file: `OpenNest/Controls/PlateView.cs`**

Add a separate temporary parts list alongside the existing `parts` list:

```csharp
private List<LayoutPart> temporaryParts = new List<LayoutPart>();
```

### Drawing

In `DrawParts`, after drawing real parts, iterate `temporaryParts` and draw them using a distinct preview color. The preview color is added to `ColorScheme` (e.g., `PreviewPart`) so it follows the existing theming pattern. Same drawing logic, different pen/brush.

### Public API

- `SetTemporaryParts(List<Part> parts)` — Clears existing temp parts, builds `LayoutPart` wrappers from the provided parts, triggers redraw.
- `ClearTemporaryParts()` — Clears temp parts and redraws.
- `AcceptTemporaryParts()` — Adds the temp parts to the real `Plate.Parts` collection (which triggers quantity tracking via `ObservableList` events), then clears the temp list.

`AcceptTemporaryParts()` is the sole "commit" path. The engine never writes to `Plate.Parts` directly when using the progress overloads.

### Thread Safety

`SetTemporaryParts` is called from `IProgress<T>` callbacks. When using `Progress<T>` constructed on the UI thread, callbacks are automatically marshalled via `SynchronizationContext`. No extra marshalling needed.

## NestProgressForm (Modeless Dialog)

**New files: `OpenNest/Forms/NestProgressForm.cs`, `NestProgressForm.Designer.cs`, `NestProgressForm.resx`**

A small modeless dialog with a `TableLayoutPanel` layout:

```
┌──────────────────────────────┐
│  Phase:    Trying pairs...   │
│  Plate:    2                 │
│  Parts:    156               │
│  Density:  68.3%             │
│  Remnant:  0.0 sq in         │
│                              │
│         [ Stop ]             │
└──────────────────────────────┘
```

Two-column `TableLayoutPanel`: left column is fixed-width labels, right column is auto-sized values. Stop button below the table.

The Plate row shows just the current plate number (no total — the total is not known in advance since it depends on how many parts fit per plate). The Plate row is hidden when running a single-plate fill.

### Behavior

- Opened via `form.Show(owner)` — modeless, stays on top of MainForm
- Receives `NestProgress` updates and refreshes labels
- Stop button triggers `CancellationTokenSource.Cancel()`, changes text to "Stopping..." (disabled)
- On engine completion (natural or cancelled), auto-closes or shows "Done" with Close button
- Closing via X button acts the same as Stop — cancels and accepts current best

## MainForm Integration

**Modified file: `OpenNest/Forms/MainForm.cs`**

### RunAutoNest_Click (auto-nest)

1. Show `AutoNestForm` dialog as before to get `NestItems`
2. Create `CancellationTokenSource`, `Progress<NestProgress>`, `NestProgressForm`
3. Open progress form modeless
4. `Task.Run` with the multi-plate loop. The background work computes results only — all UI/plate mutation happens on the UI thread via `Progress<T>` callbacks and the `await` continuation:
   - The loop iterates items, calling the new `Fill(item, workArea, progress, token)` overloads which return `List<Part>` without modifying the plate.
   - Progress callbacks update the preview via `SetTemporaryParts()` on the UI thread.
   - When a plate's fill completes, the continuation (back on the UI thread) counts the returned parts per drawing (from the last `NestProgress.BestParts`) to decrement `NestItem.Quantity`, then calls `AcceptTemporaryParts()` to commit to the plate. `Nest.CreatePlate()` for the next plate also happens on the UI thread.
   - On cancellation, breaks out of the loop and commits whatever was last previewed.
5. On completion, call `Nest.UpdateDrawingQuantities()`, close progress form
6. Disable nesting-related menu items while running, re-enable on completion
7. Dispose `CancellationTokenSource` when done

### ActionFillArea / single-plate Fill

Same pattern but simpler — no plate loop, single fill call with progress/cancellation. The progress form is created and owned by the code that launches the fill (in MainForm, not inside ActionFillArea). The Plate row is hidden. Escape key during the action cancels the token (same as clicking Stop).

### UI Lockout

While the engine runs, the user can pan/zoom the PlateView (read-only interaction) but editing actions (add/remove parts, change plates, plate navigation) are disabled. Plate navigation is locked during auto-nest to prevent the PlateView from switching away from the plate being filled. Re-enabled when nesting completes or is stopped.

If the user closes the `EditNestForm` (MDI child) while nesting is running, the cancellation token is triggered and the progress form is closed. No partial results are committed.

### Error Handling

The `Task.Run` body is wrapped in try/catch. If the engine throws an unexpected exception (not `OperationCanceledException`), the continuation shows a `MessageBox` with the error, clears temporary parts, and re-enables the UI. No partial results are committed on unexpected errors.

## Files Touched

| File | Change |
|------|--------|
| `OpenNest.Engine/NestProgress.cs` | New — progress data model + `NestPhase` enum |
| `OpenNest.Engine/NestEngine.cs` | New `List<Part>`-returning overloads with `IProgress`/`CancellationToken` |
| `OpenNest/Controls/PlateView.cs` | Temporary parts list + drawing |
| `OpenNest/Forms/NestProgressForm.cs` (+Designer, +resx) | New — modeless progress dialog |
| `OpenNest/Forms/MainForm.cs` | Rewire auto-nest and fill to async with progress |

## Not Changed

OpenNest.Core, OpenNest.IO, OpenNest.Mcp, EditNestForm, existing engine callers (MCP tools, etc.). The existing `bool Fill(...)` overloads continue to work as before by delegating to the new overloads and calling `Plate.Parts.AddRange()` themselves.
