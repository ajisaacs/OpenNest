# NestProgressForm Redesign

## Problem

The current `NestProgressForm` is a flat list of label/value pairs with no visual hierarchy, no progress indicator, and default WinForms styling. It's functional but looks basic and gives no sense of where the engine is in its process.

## Solution

Redesign the form with three changes:
1. A custom-drawn **phase stepper** control showing which nesting phases have been visited
2. **Grouped sections** separating results from status information
3. **Modern styling** — Segoe UI fonts, subtle background contrast, better spacing

## Phase Stepper Control

**New file: `OpenNest/Controls/PhaseStepperControl.cs`**

A custom `UserControl` that draws 4 circles with labels beneath, connected by lines:

```
  ●━━━━━━━●━━━━━━━○━━━━━━━○
Linear   BestFit   Pairs   Remainder
```

### Non-sequential design

The engine does **not** execute phases in a fixed order. `FindBestFill` runs Pairs → Linear → BestFit → Remainder, while the group fill path runs Linear → BestFit → Pairs → Remainder. Some phases may not execute at all (e.g., multi-part fills only run Linear).

The stepper therefore tracks **which phases have been visited**, not a left-to-right progression. Each circle independently lights up when its phase reports progress, regardless of position. The connecting lines between circles are purely decorative (always light gray) — they do not indicate sequential flow.

### Visual States

- **Completed/visited:** Filled circle with accent color, bold label — the phase has reported at least one progress update
- **Active:** Filled circle with accent color and slightly larger radius, bold label — the phase currently executing
- **Pending:** Hollow circle with border only, dimmed label text — the phase has not yet reported progress
- **Skipped:** Same as Pending — phases that never execute simply remain hollow. No special "skipped" visual needed.
- **All complete:** All 4 circles filled (used when `ShowCompleted()` is called)
- **Initial state (before first `UpdateProgress`):** All 4 circles in Pending (hollow) state

### Implementation

- Single `OnPaint` override. Circles evenly spaced across control width. Connecting lines drawn between circle centers in light gray.
- Colors and fonts defined as `static readonly` fields at the top of the class. Fonts are cached (not created per paint call) to avoid GDI handle leaks during frequent progress updates.
- Tracks state via a `HashSet<NestPhase> VisitedPhases` and a `NestPhase? ActivePhase` property. When `ActivePhase` is set, it is added to `VisitedPhases` and `Invalidate()` is called. A `bool IsComplete` property marks all phases as done.
- `DoubleBuffered = true` to prevent flicker on repaint.
- Fixed height (~60px), docks to fill width.
- Namespace: `OpenNest.Controls` (follows existing convention, e.g., `QuadrantSelect`).

## Form Layout

Three vertical zones using `DockStyle.Top` stacking:

```
┌─────────────────────────────────────┐
│  ●━━━━━━━●━━━━━━━○━━━━━━━○          │  Phase stepper
│ Linear  BestFit  Pairs  Remainder   │
├─────────────────────────────────────┤
│  Results                            │  Results group
│  Parts:    156                      │
│  Density:  68.3%                    │
│  Nested:   24.1 x 36.0 (867.6 sq in)│
│  Unused:   43.2 sq in              │
├─────────────────────────────────────┤
│  Status                            │  Status group
│  Plate:    2                        │
│  Elapsed:  1:24                     │
│  Detail:   Trying 45° rotation...   │
├─────────────────────────────────────┤
│                           [ Stop ]  │  Button bar
└─────────────────────────────────────┘
```

### Group Panels

Each group is a `Panel` containing:
- A header label (Segoe UI 9pt bold) at the top
- A `TableLayoutPanel` with label/value rows beneath

Group panels use `Color.White` (or very light gray) `BackColor` against the form's `SystemColors.Control` background to create visual separation without borders. Small padding/margins between groups.

### Typography

- All fonts: Segoe UI (replaces MS Sans Serif)
- Group headers: 9pt bold
- Row labels: 8.25pt bold
- Row values: 8.25pt regular
- Value labels use `ForeColor = SystemColors.ControlText`

### Sizing

- Width: ~450px (slightly wider than current 425px for breathing room)
- Height: fixed `ClientSize` calculated to fit stepper (~60px) + results group (~110px) + status group (~90px) + button bar (~45px) + padding. The form uses `FixedToolWindow` which does not auto-resize, so the height is set explicitly in the designer.
- `FormBorderStyle.FixedToolWindow`, `StartPosition.CenterParent`, `ShowInTaskbar = false`

### Plate Row Visibility

The Plate row in the Status group is hidden when `showPlateRow: false` is passed to the constructor (same as current behavior).

### Phase description text

The current form's `FormatPhase()` method produces friendly text like "Trying rotations..." which was displayed in the Phase row. Since the phase stepper replaces the Phase row visually, this descriptive text moves to the **Detail** row. `UpdateProgress` writes `FormatPhase(progress.Phase)` to the Detail value when `progress.Description` is empty, and writes `progress.Description` when it's set (the engine's per-iteration descriptions like "Linear: 3/12 angles" take precedence).

## Public API

No signature changes. The form remains a drop-in replacement.

### Constructor

`NestProgressForm(CancellationTokenSource cts, bool showPlateRow = true)` — unchanged.

### UpdateProgress(NestProgress progress)

Same as today, plus:
- Sets `phaseStepperControl.ActivePhase = progress.Phase` to update the stepper
- Writes `FormatPhase(progress.Phase)` to the Detail row as a fallback when `progress.Description` is empty

### ShowCompleted()

Same as today (stops timer, changes button to "Close"), plus sets `phaseStepperControl.IsComplete = true` to fill all circles.

Note: `MainForm.FillArea_Click` currently calls `progressForm.Close()` without calling `ShowCompleted()` first. This is existing behavior and is fine — the form closes immediately so the "all complete" visual is not needed in that path.

## No External Changes

- `NestProgress` and `NestPhase` are unchanged.
- All callers (`MainForm`, `PlateView.FillWithProgress`) continue calling `UpdateProgress` and `ShowCompleted` with no code changes.
- The form file paths remain the same — this is a modification, not a new form.

## Files Touched

| File | Change |
|------|--------|
| `OpenNest/Controls/PhaseStepperControl.cs` | New — custom-drawn phase stepper control |
| `OpenNest/Forms/NestProgressForm.cs` | Rewritten — grouped layout, stepper integration |
| `OpenNest/Forms/NestProgressForm.Designer.cs` | Rewritten — new control layout |
