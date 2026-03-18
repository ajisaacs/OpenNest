# NestProgressForm Redesign v2

## Problem

The current `NestProgressForm` is a flat `TableLayoutPanel` of label/value pairs with default WinForms styling, MS Sans Serif font, and no visual hierarchy. It's functional but looks plain and gives no sense of where the engine is in its process or whether results are improving.

## Solution

Four combined improvements:

1. A custom-drawn **phase stepper** control showing all 6 nesting phases with visited/active/pending states
2. **Grouped sections** separating Results from Status with white panels on a gray background
3. **Modern typography** — Segoe UI for labels, Consolas for values (monospaced so numbers don't shift width)
4. **Flash & fade with color-coded density** — values flash green on change and fade back; density flash color varies by quality (red < 50%, yellow 50-70%, green > 70%)

## Phase Stepper Control

**New file: `OpenNest/Controls/PhaseStepperControl.cs`**

A custom `UserControl` that draws 6 circles with labels beneath, connected by lines:

```
  ●━━━●━━━●━━━○━━━○━━━○
Linear BestFit Pairs NFP Extents Custom
```

### All 6 phases

The stepper displays all values from the `NestPhase` enum in enum order: `Linear`, `RectBestFit` (labeled "BestFit"), `Pairs`, `Nfp` (labeled "NFP"), `Extents`, `Custom`. This ensures the control stays accurate as new phases are added or existing ones start being used.

### Non-sequential design

The engine does not execute phases in a fixed order. `DefaultNestEngine` runs strategies in registration order (Linear → Pairs → RectBestFit → Extents by default), and custom engines may run any subset in any order. Some phases may never execute.

The stepper tracks **which phases have been visited**, not a left-to-right progression. Each circle independently lights up when its phase reports progress. The connecting lines are purely decorative (always light gray).

### Visual states

- **Active:** Filled circle with accent color (`#0078D4`), slightly larger radius (11px vs 9px), subtle glow (`Color.FromArgb(60, 0, 120, 212)` drawn as a larger circle behind), bold label
- **Visited:** Filled circle with accent color, normal radius, bold label
- **Pending:** Hollow circle with gray border (`#C0C0C0`), dimmed label text (`#999999`)
- **All complete:** All 6 circles filled (set when `IsComplete = true`)
- **Initial state:** All 6 circles in Pending state

### Implementation

- Single `OnPaint` override. Circles evenly spaced across control width. Connecting lines drawn between circle centers in light gray (`#D0D0D0`).
- Colors and fonts defined as `static readonly` fields. Fonts cached (not created per paint call) to avoid GDI handle leaks during frequent progress updates.
- State: `HashSet<NestPhase> VisitedPhases`, `NestPhase? ActivePhase` property. Setting `ActivePhase` adds to `VisitedPhases` and calls `Invalidate()`. `bool IsComplete` marks all phases done.
- `DoubleBuffered = true`.
- Fixed height: 60px. Docks to fill width.
- Namespace: `OpenNest.Controls`.
- Phase display order matches `NestPhase` enum order. Display names: `RectBestFit` → "BestFit", `Nfp` → "NFP", others use `ToString()`.

## Form Layout

Four vertical zones using `DockStyle.Top` stacking:

```
┌──────────────────────────────────────────────┐
│  ●━━━●━━━●━━━○━━━○━━━○                       │  Phase stepper
│ Linear BestFit Pairs Extents NFP Custom      │
├──────────────────────────────────────────────┤
│  RESULTS                                     │  Results group
│  Parts:    156                               │
│  Density:  68.3%  ████████░░                 │
│  Nested:   24.1 x 36.0 (867.6 sq in)        │
├──────────────────────────────────────────────┤
│  STATUS                                      │  Status group
│  Plate:    2                                 │
│  Elapsed:  1:24                              │
│  Detail:   Trying best fit...                │
├──────────────────────────────────────────────┤
│                                    [ Stop ]  │  Button bar
└──────────────────────────────────────────────┘
```

### Group panels

Each group is a `Panel` containing:
- A header label ("RESULTS" / "STATUS") — Segoe UI 9pt bold, uppercase, color `#555555`, with `0.5px` letter spacing effect (achieved by drawing or just using uppercase text)
- A `TableLayoutPanel` with label/value rows beneath

Group panels use `Color.White` `BackColor` against the form's `SystemColors.Control` (gray) background. Small padding (10px horizontal, 4px vertical gap between groups).

### Typography

- All fonts: Segoe UI
- Group headers: 9pt bold, uppercase, color `#555555`
- Row labels: 8.25pt bold, color `#333333`
- Row values: Consolas 8.25pt regular — monospaced so numeric values don't shift width as digits change
- Detail value: Segoe UI 8.25pt regular (not monospaced, since it's descriptive text)

### Sizing

- Width: ~450px
- Height: fixed `ClientSize` calculated to fit stepper (~60px) + results group (~115px) + status group (~95px) + button bar (~45px) + padding
- `FormBorderStyle.FixedToolWindow`, `StartPosition.CenterParent`, `ShowInTaskbar = false`

### Plate row visibility

The Plate row in the Status group is hidden when `showPlateRow: false` is passed to the constructor (same as current behavior).

### Phase description text

The phase stepper replaces the old Phase row. The descriptive text ("Trying rotations...") moves to the Detail row. `UpdateProgress` writes `FormatPhase(progress.Phase)` to the Detail value when `progress.Description` is empty, and writes `progress.Description` when set.

### Unused row removed

The current form has `remnantLabel`/`remnantValue` but `NestProgress` has no unused/remnant property — these labels are never updated and always show "—". The redesign drops this row entirely.

### FormatPhase updates

`FormatPhase` currently handles Linear, RectBestFit, and Pairs. Add entries for the three remaining phases:
- `Extents` → "Trying extents..."
- `Nfp` → "Trying NFP..."
- `Custom` → phase name via `ToString()`

## Density Sparkline Bar

A small inline visual next to the density percentage value:

- Size: 60px wide, 8px tall
- Background: `#E0E0E0` (light gray track)
- Fill: gradient from orange (`#F5A623`) on the left to green (`#4CAF50`) on the right, clipped to the density percentage width
- Border radius: 4px
- Position: inline, 8px margin-left from the density text

### Implementation

Owner-drawn directly in a custom `Label` subclass or a small `Panel` placed next to the density value in the table. The simplest approach: a small `Panel` with `OnPaint` override that draws the track and fill. Updated whenever density changes.

**New file: `OpenNest/Controls/DensityBar.cs`** — a lightweight `Control` subclass:
- `double Value` property (0.0 to 1.0), calls `Invalidate()` on set
- `OnPaint`: fills rounded rect background, then fills gradient portion proportional to `Value`
- Fixed size: 60 x 8px
- `DoubleBuffered = true`

## Flash & Fade

### Current implementation (keep, with modification)

Values flash green (`Color.FromArgb(0, 160, 0)`) when they change and fade back to `SystemColors.ControlText` over ~1 second (20 steps at 50ms). A `SetValueWithFlash` helper checks if text actually changed before triggering. A single `System.Windows.Forms.Timer` drives all active fades.

### Color-coded density flash

Extend the flash color for the density value based on quality:
- Below 50%: red (`Color.FromArgb(200, 40, 40)`)
- 50% to 70%: yellow/orange (`Color.FromArgb(200, 160, 0)`)
- Above 70%: green (`Color.FromArgb(0, 160, 0)`) — same as current

### Fade state changes

The `SetValueWithFlash` method gains an optional `Color? flashColor` parameter. The fade dictionary changes from `Dictionary<Label, int>` to `Dictionary<Label, (int remaining, Color flashColor)>` so that each label fades from its own flash color. `FadeTimer_Tick` reads the per-label `flashColor` from the tuple when interpolating back to `SystemColors.ControlText`, rather than using the static `FlashColor` constant. `FlashColor` becomes the default when `flashColor` is null.

`UpdateProgress` passes the density-appropriate color when updating `densityValue`. All other values continue using the default green.

## Accept & Stop Buttons

Currently the form has a single "Stop" button that cancels the `CancellationTokenSource`. Callers check `token.IsCancellationRequested` and discard results when true. This means there's no way to stop early and keep the current best result.

### New button layout

Two buttons in the button bar, right-aligned:

```
                              [ Accept ]  [ Stop ]
```

- **Accept:** Stops the engine and keeps the current best result. Sets `Accepted = true`, then cancels the token.
- **Stop:** Stops the engine and discards results. Leaves `Accepted = false`, cancels the token.

Both buttons are disabled until the first progress update arrives (so there's something to accept). After `ShowCompleted()`, both are replaced by a single "Close" button (same as current behavior).

### Accepted property

`bool Accepted { get; private set; }` — defaults to `false`. Set to `true` only by the Accept button click handler.

### Caller changes

Four callsites create a `NestProgressForm`. Each needs to honor the `Accepted` property:

**`MainForm.cs` — `RunAutoNest_Click`** (line ~868):
```csharp
// Before:
if (nestParts.Count > 0 && !token.IsCancellationRequested)
// After:
if (nestParts.Count > 0 && (!token.IsCancellationRequested || progressForm.Accepted))
```

**`MainForm.cs` — `FillPlate_Click`** (line ~983): No change needed — this path already accepts regardless of cancellation state (`if (parts.Count > 0)`).

**`MainForm.cs` — `FillArea_Click`** (line ~1024): No change needed — this path delegates to `ActionFillArea` which handles its own completion via a callback.

**`PlateView.cs` — `FillWithProgress`** (line ~933):
```csharp
// Before:
if (parts.Count > 0 && !cts.IsCancellationRequested)
// After:
if (parts.Count > 0 && (!cts.IsCancellationRequested || progressForm.Accepted))
```

## Public API

### Constructor

`NestProgressForm(CancellationTokenSource cts, bool showPlateRow = true)` — unchanged.

### Properties

- `bool Accepted { get; }` — **new**. True if user clicked Accept, false if user clicked Stop or form was closed.

### UpdateProgress(NestProgress progress)

Same as today, plus:
- Sets `phaseStepperControl.ActivePhase = progress.Phase` to update the stepper
- Updates `densityBar.Value = progress.BestDensity`
- Passes color-coded flash color for density value
- Writes `FormatPhase(progress.Phase)` to Detail row as fallback when `progress.Description` is empty
- Enables Accept/Stop buttons on first call (if not already enabled)

### ShowCompleted()

Same as today (stops timer, changes button to "Close"), plus sets `phaseStepperControl.IsComplete = true` to fill all circles.

## Files Touched

| File | Change |
|------|--------|
| `OpenNest/Controls/PhaseStepperControl.cs` | New — custom-drawn phase stepper control |
| `OpenNest/Controls/DensityBar.cs` | New — small density sparkline bar control |
| `OpenNest/Forms/NestProgressForm.cs` | Rewritten — grouped layout, stepper integration, color-coded flash, Accept/Stop buttons |
| `OpenNest/Forms/NestProgressForm.Designer.cs` | Rewritten — new control layout |
| `OpenNest/Forms/MainForm.cs` | Update `RunAutoNest_Click` to check `progressForm.Accepted` |
| `OpenNest/Controls/PlateView.cs` | Update `FillWithProgress` to check `progressForm.Accepted` |
