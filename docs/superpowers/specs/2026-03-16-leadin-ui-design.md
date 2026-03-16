# Lead-In Assignment UI Design (Revised)

## Overview

Add a dialog and menu item for assigning lead-ins to parts on a plate. The dialog provides separate parameter sets for external (perimeter) and internal (cutout/hole) contours. Lead-in/lead-out moves are tagged with the existing `LayerType.Leadin`/`LayerType.Leadout` enum on each code, making them distinguishable from normal cut code and easy to strip and re-apply.

## Design Principles

- **LayerType tagging.** Every lead-in move gets `Layer = LayerType.Leadin`, every lead-out move gets `Layer = LayerType.Leadout`. Normal contour cuts keep `Layer = LayerType.Cut` (the default). This uses the existing `LayerType` enum and `LinearMove.Layer`/`ArcMove.Layer` properties — no new enums or flags.
- **Always rebuild from base.** `ContourCuttingStrategy.Apply` converts the input program to geometry via `Program.ToGeometry()` and `ShapeProfile`. These do NOT filter by layer — all entities (including lead-in/out codes if present) would be processed. Therefore, the strategy must always receive a clean program (cut codes only). The flow always clones from `Part.BaseDrawing.Program` and re-rotates before applying.
- **Non-destructive.** `Part.BaseDrawing.Program` is never modified. The strategy builds a fresh `Program` with lead-ins baked in. `Part.HasManualLeadIns` (existing property) is set to `true` when lead-ins are assigned, so the automated `PlateProcessor` pipeline skips these parts.

## Lead-In Dialog (`LeadInForm`)

A WinForms dialog in `OpenNest/Forms/LeadInForm.cs` with two parameter groups, one checkbox, and OK/Cancel buttons.

### External Group (Perimeter)
- Lead-in angle (degrees) — default 90
- Lead-in length (inches) — default 0.125
- Overtravel (inches) — default 0.03

### Internal Group (Cutouts & Holes)
- Lead-in angle (degrees) — default 90
- Lead-in length (inches) — default 0.125
- Overtravel (inches) — default 0.03

### Update Existing Checkbox
- **"Update existing lead-ins"** — checked by default
- When checked: strip all existing lead-in/lead-out codes from every part before re-applying
- When unchecked: only process parts that have no `LayerType.Leadin` codes in their program

### Dialog Result

```csharp
public class LeadInSettings
{
    // External (perimeter) parameters
    public double ExternalLeadInAngle { get; set; } = 90;
    public double ExternalLeadInLength { get; set; } = 0.125;
    public double ExternalOvertravel { get; set; } = 0.03;

    // Internal (cutout/hole) parameters
    public double InternalLeadInAngle { get; set; } = 90;
    public double InternalLeadInLength { get; set; } = 0.125;
    public double InternalOvertravel { get; set; } = 0.03;

    // Behavior
    public bool UpdateExisting { get; set; } = true;
}
```

Note: `LineLeadIn.ApproachAngle` and `LineLeadOut.ApproachAngle` store degrees (not radians), converting internally via `Angle.ToRadians()`. The `LeadInSettings` values are degrees and can be passed directly.

## LeadInSettings to CuttingParameters Mapping

The caller builds one `CuttingParameters` instance with separate external and internal settings. ArcCircle shares the internal settings:

```
ExternalLeadIn  = new LineLeadIn { ApproachAngle = settings.ExternalLeadInAngle, Length = settings.ExternalLeadInLength }
ExternalLeadOut = new LineLeadOut { Length = settings.ExternalOvertravel }
InternalLeadIn  = new LineLeadIn { ApproachAngle = settings.InternalLeadInAngle, Length = settings.InternalLeadInLength }
InternalLeadOut = new LineLeadOut { Length = settings.InternalOvertravel }
ArcCircleLeadIn = (same as Internal)
ArcCircleLeadOut = (same as Internal)
```

## Detecting Existing Lead-Ins

Check whether a part's program contains lead-in codes by inspecting `LayerType`:

```csharp
bool HasLeadIns(Program program)
{
    foreach (var code in program.Codes)
    {
        if (code is LinearMove lm && lm.Layer == LayerType.Leadin)
            return true;
        if (code is ArcMove am && am.Layer == LayerType.Leadin)
            return true;
    }
    return false;
}
```

## Preparing a Clean Program

**Important:** `Program.ToGeometry()` and `ShapeProfile` process ALL entities regardless of layer. They do NOT filter out lead-in/lead-out codes. If the strategy receives a program that already has lead-in codes baked in, those codes would be converted to geometry entities and corrupt the perimeter/cutout detection.

Therefore, the flow always starts from a clean base:

```csharp
var cleanProgram = part.BaseDrawing.Program.Clone() as Program;
cleanProgram.Rotate(part.Rotation);
```

This produces a program with only the original cut geometry at the part's current rotation angle, safe to feed into `ContourCuttingStrategy.Apply`.

## Menu Integration

Add "Assign Lead-Ins" to the Plate menu in `MainForm`, after "Sequence Parts" and before "Calculate Cut Time".

Click handler in `MainForm` delegates to `EditNestForm.AssignLeadIns()`.

## AssignLeadIns Flow (EditNestForm)

```
1. Open LeadInForm dialog
2. If user clicks OK:
   a. Get LeadInSettings from dialog (includes UpdateExisting flag)
   b. Build one ContourCuttingStrategy with CuttingParameters from settings
   c. Get exit point: PlateHelper.GetExitPoint(plate)  [now public]
   d. Set currentPoint = exitPoint
   e. For each part on the current plate (in sequence order):
      - If !updateExisting and part already has lead-in codes → skip
      - Build clean program: clone BaseDrawing.Program, rotate to part.Rotation
      - Compute localApproach = currentPoint - part.Location
      - Call strategy.Apply(cleanProgram, localApproach) → CuttingResult
      - Call part.ApplyLeadIns(cutResult.Program)
        (this sets Program, HasManualLeadIns = true, and recalculates bounds)
      - Update currentPoint = cutResult.LastCutPoint + part.Location
   f. Invalidate PlateView to show updated geometry
```

Note: The clean program is always rebuilt from `BaseDrawing.Program` — never from the current `Part.Program` — because `Program.ToGeometry()` and `ShapeProfile` do not filter by layer and would be corrupted by existing lead-in codes.

Note: Setting `Part.Program` requires a public method since the setter is `private`. See Model Changes below.

## Model Changes

### Part (OpenNest.Core)

Add a method to apply lead-ins and mark the part:

```csharp
public void ApplyLeadIns(Program processedProgram)
{
    Program = processedProgram;
    HasManualLeadIns = true;
    UpdateBounds();
}
```

This atomically sets the processed program, marks `HasManualLeadIns = true` (so `PlateProcessor` skips this part), and recalculates bounds. The private setter on `Program` stays private — `ApplyLeadIns` is the public API.

### PlateHelper (OpenNest.Engine)

Change `PlateHelper` from `internal static` to `public static` so the UI project can access `GetExitPoint`.

## ContourCuttingStrategy Changes

### LayerType Tagging

When emitting lead-in moves, stamp each code with `Layer = LayerType.Leadin`. When emitting lead-out moves, stamp with `Layer = LayerType.Leadout`. This applies to all move types (`LinearMove`, `ArcMove`) generated by `LeadIn.Generate()` and `LeadOut.Generate()`.

The `LeadIn.Generate()` and `LeadOut.Generate()` methods return `List<ICode>`. After calling them, the strategy sets the `Layer` property on each returned code:

```csharp
var leadInCodes = leadIn.Generate(piercePoint, normal, winding);
foreach (var code in leadInCodes)
{
    if (code is LinearMove lm) lm.Layer = LayerType.Leadin;
    else if (code is ArcMove am) am.Layer = LayerType.Leadin;
}
result.Codes.AddRange(leadInCodes);
```

Same pattern for lead-out codes with `LayerType.Leadout`.

### Corner vs Mid-Entity Auto-Detection

When generating the lead-out, the strategy detects whether the pierce point landed on a corner or mid-entity. Detection uses the `out Entity` from `ClosestPointTo` with type-specific endpoint checks:

```csharp
private static bool IsCornerPierce(Vector closestPt, Entity entity)
{
    if (entity is Line line)
        return closestPt.DistanceTo(line.StartPoint) < Tolerance.Epsilon
            || closestPt.DistanceTo(line.EndPoint) < Tolerance.Epsilon;
    if (entity is Arc arc)
        return closestPt.DistanceTo(arc.StartPoint()) < Tolerance.Epsilon
            || closestPt.DistanceTo(arc.EndPoint()) < Tolerance.Epsilon;
    return false;
}
```

Note: `Entity` has no polymorphic `StartPoint`/`EndPoint` — `Line` has properties, `Arc` has methods, `Circle` has neither.

### Corner Lead-Out

Delegates to `LeadOut.Generate()` as normal — `LineLeadOut` extends past the corner along the contour normal. Moves are tagged `LayerType.Leadout`.

### Mid-Entity Lead-Out (Contour-Follow Overtravel)

Handled at the `ContourCuttingStrategy` level, NOT via `LeadOut.Generate()` (which lacks access to the contour shape). The overtravel distance is read from the selected `LeadOut` for the current contour type — `SelectLeadOut(contourType)`. Since external and internal have separate `LineLeadOut` instances in `CuttingParameters`, the overtravel distance automatically varies by contour type.

```csharp
var leadOut = SelectLeadOut(contourType);
if (IsCornerPierce(closestPt, entity))
{
    // Corner: delegate to LeadOut.Generate() as normal
    var codes = leadOut.Generate(closestPt, normal, winding);
    // tag as LayerType.Leadout
}
else if (leadOut is LineLeadOut lineLeadOut && lineLeadOut.Length > 0)
{
    // Mid-entity: retrace the start of the contour for overtravel distance
    var codes = GenerateOvertravelMoves(reindexed, lineLeadOut.Length);
    // tag as LayerType.Leadout
}
```

The contour-follow retraces the beginning of the reindexed shape:

1. Walking the reindexed shape's entities from the start
2. Accumulating distance until overtravel is reached
3. Emitting `LinearMove`/`ArcMove` codes for those segments (splitting the last segment if needed)
4. Tagging all emitted moves as `LayerType.Leadout`

This produces a clean overcut that ensures the contour fully closes.

### Lead-out behavior summary

| Contour Type | Pierce Location | Lead-Out Behavior |
|---|---|---|
| External | Corner | `LineLeadOut.Generate()` — extends past corner |
| External | Mid-entity | Contour-follow overtravel moves |
| Internal | Corner | `LineLeadOut.Generate()` — extends past corner |
| Internal | Mid-entity | Contour-follow overtravel moves |
| ArcCircle | N/A (always mid-entity) | Contour-follow overtravel moves |

## File Structure

```
OpenNest.Core/
├── Part.cs                                    # add ApplyLeadIns method
└── CNC/CuttingStrategy/
    └── ContourCuttingStrategy.cs              # LayerType tagging, Overtravel, corner detection

OpenNest.Engine/
└── Sequencing/
    └── PlateHelper.cs                         # change internal → public

OpenNest/
├── Forms/
│   ├── LeadInForm.cs                          # new dialog
│   ├── LeadInForm.Designer.cs                 # new dialog designer
│   ├── MainForm.Designer.cs                   # add menu item
│   ├── MainForm.cs                            # add click handler
│   └── EditNestForm.cs                        # add AssignLeadIns method
└── LeadInSettings.cs                          # settings DTO
```

## Out of Scope

- Tabbed (V lead-in/out) parameters and `Part.IsTabbed` — deferred until tab assignment UI
- Slug destruct for internal cutouts
- Lead-in visualization colors in PlateView (separate enhancement)
- Database storage of lead-in presets by material/thickness
- MicrotabLeadOut integration
- Nest file serialization changes
