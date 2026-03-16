# Lead-In Assignment UI Design

## Overview

Add a dialog and menu item for assigning lead-ins to parts on a plate. The dialog provides two parameter sets — tabbed (V lead-in/out) and standard (straight lead-in with overtravel) — and applies them per-part based on a new `Part.IsTabbed` flag. The `ContourCuttingStrategy` auto-detects corner vs mid-entity pierce points to determine lead-out behavior.

This is the "manual override" path — when the user assigns lead-ins via this dialog, each part gets `HasManualLeadIns = true` so the automated `PlateProcessor` pipeline skips it.

## Model Changes

### Part (OpenNest.Core)

Add two properties:

```csharp
public bool IsTabbed { get; set; }
```

Indicates the part uses tabbed lead-in parameters (V lead-in/out). Defaults to `false` — all parts use standard parameters until a tab assignment UI is built.

```csharp
public void ApplyLeadIns(Program processedProgram)
{
    Program = processedProgram;
    HasManualLeadIns = true;
}
```

Atomically sets the processed program and marks lead-ins as manually assigned. The original drawing program is preserved on `Part.BaseDrawing.Program`. This is the intentional "manual" path — `PlateProcessor` (the automated path) stores results non-destructively in `ProcessedPart.ProcessedProgram` and does not call this method.

### PlateHelper (OpenNest.Engine)

Change `PlateHelper` from `internal static` to `public static` so the UI project can access `GetExitPoint`.

## Lead-In Dialog (`LeadInForm`)

A WinForms dialog in `OpenNest/Forms/LeadInForm.cs` with two groups of numeric inputs.

### Tabbed Group (V lead-in/lead-out)
- Lead-in angle (degrees) — default 60
- Lead-in length (inches) — default 0.15
- Lead-out angle (degrees) — default 60
- Lead-out length (inches) — default 0.08

These form a V shape at the pierce point where the breaking point lands on the part edge, leaving a less noticeable tab spot.

### Standard Group
- Lead-in angle (degrees) — default 90
- Lead-in length (inches) — default 0.125
- Overtravel distance (inches) — default 0.03

The lead-out behavior for standard parts depends on pierce point location (auto-detected by `ContourCuttingStrategy`):
- **Corner pierce:** straight `LineLeadOut` extending past the corner for the overtravel distance
- **Mid-entity pierce:** handled at the `ContourCuttingStrategy` level (not via `LeadOut.Generate`) — the strategy appends overcut moves that follow the contour path for the overtravel distance after the shape's closing segment

### Dialog Result

```csharp
public class LeadInSettings
{
    // Tabbed parameters (V lead-in/out)
    public double TabbedLeadInAngle { get; set; } = 60;
    public double TabbedLeadInLength { get; set; } = 0.15;
    public double TabbedLeadOutAngle { get; set; } = 60;
    public double TabbedLeadOutLength { get; set; } = 0.08;

    // Standard parameters
    public double StandardLeadInAngle { get; set; } = 90;
    public double StandardLeadInLength { get; set; } = 0.125;
    public double StandardOvertravel { get; set; } = 0.03;
}
```

Note: `LineLeadIn.ApproachAngle` and `LineLeadOut.ApproachAngle` store degrees (not radians), converting internally via `Angle.ToRadians()`. The `LeadInSettings` values are degrees and can be passed directly.

## LeadInSettings to CuttingParameters Mapping

The caller builds two `CuttingParameters` instances up front — one for tabbed parts, one for standard — rather than swapping parameters per iteration:

**Tabbed:**
```
ExternalLeadIn  = new LineLeadIn { ApproachAngle = settings.TabbedLeadInAngle, Length = settings.TabbedLeadInLength }
ExternalLeadOut = new LineLeadOut { ApproachAngle = settings.TabbedLeadOutAngle, Length = settings.TabbedLeadOutLength }
InternalLeadIn  = (same)
InternalLeadOut = (same)
ArcCircleLeadIn = (same)
ArcCircleLeadOut = (same)
```

**Standard:**
```
ExternalLeadIn  = new LineLeadIn { ApproachAngle = settings.StandardLeadInAngle, Length = settings.StandardLeadInLength }
ExternalLeadOut = new LineLeadOut { Length = settings.StandardOvertravel }
InternalLeadIn  = (same)
InternalLeadOut = (same)
ArcCircleLeadIn = (same)
ArcCircleLeadOut = (same)
```

For standard parts, the `LineLeadOut` handles the corner case. The mid-entity contour-follow case is handled at the `ContourCuttingStrategy` level (see below).

All three contour types (external, internal, arc/circle) get the same settings for this iteration.

## Menu Integration

Add "Assign Lead-Ins" to the Plate menu in `MainForm`, after "Sequence Parts" and before "Calculate Cut Time".

Click handler in `MainForm` delegates to `EditNestForm.AssignLeadIns()`.

## AssignLeadIns Flow (EditNestForm)

```
1. Open LeadInForm dialog
2. If user clicks OK:
   a. Get LeadInSettings from dialog
   b. Build two ContourCuttingStrategy instances:
      - tabbedStrategy with tabbed CuttingParameters
      - standardStrategy with standard CuttingParameters
   c. Get exit point: PlateHelper.GetExitPoint(plate)  [now public]
   d. Set currentPoint = exitPoint
   e. For each part on the current plate (in list order):
      - Skip if part.HasManualLeadIns is true
      - Compute localApproach = currentPoint - part.Location
      - Pick strategy = part.IsTabbed ? tabbedStrategy : standardStrategy
      - Call strategy.Apply(part.Program, localApproach) → CuttingResult
      - Call part.ApplyLeadIns(cutResult.Program)
        (this sets Program AND HasManualLeadIns = true atomically)
      - Update currentPoint = cutResult.LastCutPoint + part.Location
   f. Invalidate PlateView to show updated geometry
```

Note: `ContourCuttingStrategy.Apply` builds a new `Program` from scratch — it reads `part.Program` but does not modify it. The returned `CuttingResult.Program` is a fresh instance with lead-ins baked in.

## ContourCuttingStrategy Changes

### Corner vs Mid-Entity Auto-Detection

When generating the lead-out for standard (non-tabbed) parts, the strategy detects whether the pierce point landed on a corner or mid-entity. Detection uses the `out Entity` from `ClosestPointTo` with type-specific endpoint checks:

```csharp
bool isCorner;
if (entity is Line line)
    isCorner = closestPt.DistanceTo(line.StartPoint) < Tolerance.Epsilon
            || closestPt.DistanceTo(line.EndPoint) < Tolerance.Epsilon;
else if (entity is Arc arc)
    isCorner = closestPt.DistanceTo(arc.StartPoint()) < Tolerance.Epsilon
            || closestPt.DistanceTo(arc.EndPoint()) < Tolerance.Epsilon;
else
    isCorner = false;
```

Note: `Entity` has no polymorphic `StartPoint`/`EndPoint` — `Line` has properties, `Arc` has methods, `Circle` has neither.

### Corner Lead-Out

Delegates to `LeadOut.Generate()` as normal — `LineLeadOut` extends past the corner along the contour normal.

### Mid-Entity Lead-Out (Contour-Follow Overtravel)

Handled at the `ContourCuttingStrategy` level, NOT via `LeadOut.Generate()` (which lacks access to the contour shape). After the reindexed shape's moves are emitted, the strategy appends additional moves that retrace the beginning of the contour for the overtravel distance. This is done by:

1. Walking the reindexed shape's entities from the start
2. Accumulating distance until `overtravel` is reached
3. Emitting `LinearMove`/`ArcMove` codes for those segments (splitting the last segment if needed)

This produces a clean overcut that ensures the contour fully closes.

### Tabbed Lead-Out

For tabbed parts, the lead-out is always a `LineLeadOut` at the specified angle and length, regardless of corner/mid-entity. This creates the V shape.

## File Structure

```
OpenNest.Core/
├── Part.cs                                    # add IsTabbed, ApplyLeadIns
└── CNC/CuttingStrategy/
    └── ContourCuttingStrategy.cs               # corner vs mid-entity lead-out detection

OpenNest.Engine/
└── Sequencing/
    └── PlateHelper.cs                          # change internal → public

OpenNest/
├── Forms/
│   ├── LeadInForm.cs                          # new dialog
│   ├── LeadInForm.Designer.cs                 # new dialog designer
│   ├── MainForm.Designer.cs                   # add menu item
│   ├── MainForm.cs                            # add click handler
│   └── EditNestForm.cs                        # add AssignLeadIns method
└── LeadInSettings.cs                          # settings DTO
```

## Known Limitations

- `Part.IsTabbed` defaults to `false` with no UI to set it yet. All parts use standard parameters until a tab assignment UI is built. The tabbed code path is present but exercised only programmatically or via MCP tools for now.
- `IsTabbed` is not yet serialized through the nest file format (NestWriter/NestReader). Will need serialization support when the tab assignment UI is added.

## Out of Scope

- Per-contour-type lead-in configuration (deferred to database/datagrid UI)
- Lead-in visualization in PlateView (separate enhancement)
- Database storage of lead-in presets by material/thickness
- Tab assignment UI (setting `Part.IsTabbed`)
- MicrotabLeadOut integration
- Nest file serialization of `IsTabbed`
