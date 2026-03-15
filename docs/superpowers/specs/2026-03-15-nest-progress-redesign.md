# NestProgressForm Redesign

## Problem

The current `NestProgressForm` is a flat list of label/value pairs with no visual hierarchy, no progress indicator, and default WinForms styling. It's functional but looks basic and gives no sense of where the engine is in its process.

## Solution

Redesign the form with three changes:
1. A custom-drawn **phase stepper** control showing which nesting phase is active
2. **Grouped sections** separating results from status information
3. **Modern styling** — Segoe UI fonts, subtle background contrast, better spacing

## Phase Stepper Control

**New file: `OpenNest/Controls/PhaseStepperControl.cs`**

A custom `UserControl` that draws 4 connected circles with labels beneath:

```
  ●━━━━━━━●━━━━━━━○━━━━━━━○
Linear   BestFit   Pairs   Remainder
```

### Visual States

- **Completed:** Filled circle with accent color, bold label
- **Active:** Filled circle with accent color and slightly larger radius, bold label
- **Pending:** Hollow circle with border only, dimmed label text
- **All complete:** All 4 circles filled (used when `ShowCompleted()` is called)

### Implementation

- Single `OnPaint` override. Circles evenly spaced across control width. Connecting lines drawn between circle centers — completed segments use accent color, pending segments use a light gray.
- Colors defined as `static readonly Color` fields at the top of the class for easy tweaking.
- Exposes a `CurrentPhase` property (type `NestPhase?`). Setting it calls `Invalidate()`. A `null` value means no phase is active yet. An additional `bool IsComplete` property marks all phases as done.
- `DoubleBuffered = true` to prevent flicker on repaint.
- Fixed height (~60px), docks to fill width.

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
- Height: auto-sizes to content via `AutoSize = true` on the table panels
- `FormBorderStyle.FixedToolWindow`, `StartPosition.CenterParent`, `ShowInTaskbar = false`

### Plate Row Visibility

The Plate row in the Status group is hidden when `showPlateRow: false` is passed to the constructor (same as current behavior).

## Public API

No signature changes. The form remains a drop-in replacement.

### Constructor

`NestProgressForm(CancellationTokenSource cts, bool showPlateRow = true)` — unchanged.

### UpdateProgress(NestProgress progress)

Same as today, plus sets `phaseStepperControl.CurrentPhase = progress.Phase` to advance the stepper visual.

### ShowCompleted()

Same as today (stops timer, changes button to "Close"), plus sets `phaseStepperControl.IsComplete = true` to fill all circles.

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
