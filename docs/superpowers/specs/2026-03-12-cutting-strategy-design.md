# CNC Cutting Strategy Design

## Overview

Add lead-in, lead-out, and tab classes to `OpenNest.Core` that generate `ICode` instructions for CNC cutting approach/exit geometry. The strategy runs at nest-time — `ContourCuttingStrategy.Apply()` produces a new `Program` with lead-ins, lead-outs, start points, and contour ordering baked in. This modified program is what gets saved to the nest file and later fed to the post-processor for machine-specific G-code translation. The original `Drawing.Program` stays untouched; the strategy output lives on the `Part`.

All new code lives in `OpenNest.Core/CNC/CuttingStrategy/`.

## File Structure

```
OpenNest.Core/CNC/CuttingStrategy/
├── LeadIns/
│   ├── LeadIn.cs
│   ├── NoLeadIn.cs
│   ├── LineLeadIn.cs
│   ├── LineArcLeadIn.cs
│   ├── ArcLeadIn.cs
│   ├── LineLineLeadIn.cs
│   └── CleanHoleLeadIn.cs
├── LeadOuts/
│   ├── LeadOut.cs
│   ├── NoLeadOut.cs
│   ├── LineLeadOut.cs
│   ├── ArcLeadOut.cs
│   └── MicrotabLeadOut.cs
├── Tabs/
│   ├── Tab.cs
│   ├── NormalTab.cs
│   ├── BreakerTab.cs
│   └── MachineTab.cs
├── ContourType.cs
├── CuttingParameters.cs
├── ContourCuttingStrategy.cs
├── SequenceParameters.cs
└── AssignmentParameters.cs
```

## Namespace

All classes use `namespace OpenNest.CNC.CuttingStrategy`.

## Type Mappings from Original Spec

The original spec used placeholder names. These are the correct codebase types:

| Spec type | Actual type | Notes |
|-----------|------------|-------|
| `PointD` | `Vector` | `OpenNest.Geometry.Vector` — struct with `X`, `Y` fields |
| `CircularMove` | `ArcMove` | Constructor: `ArcMove(Vector endPoint, Vector centerPoint, RotationType rotation)` |
| `CircularDirection` | `RotationType` | Enum with `CW`, `CCW` |
| `value.ToRadians()` | `Angle.ToRadians(value)` | Static method on `OpenNest.Math.Angle` |
| `new Program(codes)` | Build manually | Create `Program()`, add to `.Codes` list |

## LeadIn Hierarchy

### Abstract Base: `LeadIn`

```csharp
public abstract class LeadIn
{
    public abstract List<ICode> Generate(Vector contourStartPoint, double contourNormalAngle,
        RotationType winding = RotationType.CW);
    public abstract Vector GetPiercePoint(Vector contourStartPoint, double contourNormalAngle);
}
```

- `contourStartPoint`: where the contour cut begins (first point of the part profile).
- `contourNormalAngle`: normal angle (radians) at the contour start point, pointing **away from the part material** (outward from perimeter, into scrap for cutouts).
- `winding`: contour winding direction — arc-based lead-ins use this for their `ArcMove` rotation.
- `Generate` returns ICode instructions starting with a `RapidMove` to the pierce point, followed by cutting moves to reach the contour start.
- `GetPiercePoint` computes where the head rapids to before firing — useful for visualization and collision detection.

### NoLeadIn (Type 0)

Pierce directly on the contour start point. Returns a single `RapidMove(contourStartPoint)`.

### LineLeadIn (Type 1)

Straight line approach.

Properties:
- `Length` (double): distance from pierce point to contour start (inches)
- `ApproachAngle` (double): approach angle in degrees relative to contour tangent. 90 = perpendicular, 135 = acute angle (common for plasma). Default: 90.

Pierce point offset: `contourStartPoint + Length` along `contourNormalAngle + Angle.ToRadians(ApproachAngle)`.

Generates: `RapidMove(piercePoint)` → `LinearMove(contourStartPoint)`.

> **Note:** Properties are named `ApproachAngle` (not `Angle`) to avoid shadowing the `OpenNest.Math.Angle` static class. This applies to all lead-in/lead-out/tab classes.

### LineArcLeadIn (Type 2)

Line followed by tangential arc meeting the contour. Most common for plasma.

Properties:
- `LineLength` (double): straight approach segment length
- `ApproachAngle` (double): line angle relative to contour. Default: 135.
- `ArcRadius` (double): radius of tangential arc

Geometry: Pierce → [Line] → Arc start → [Arc] → Contour start. Arc center is at `contourStartPoint + ArcRadius` along normal. Arc rotation direction matches contour winding (CW for CW contours, CCW for CCW).

Generates: `RapidMove(piercePoint)` → `LinearMove(arcStart)` → `ArcMove(contourStartPoint, arcCenter, rotation)`.

### ArcLeadIn (Type 3)

Pure arc approach, no straight line segment.

Properties:
- `Radius` (double): arc radius

Pierce point is diametrically opposite the contour start on the arc circle. Arc center at `contourStartPoint + Radius` along normal.

Arc rotation direction matches contour winding.

Generates: `RapidMove(piercePoint)` → `ArcMove(contourStartPoint, arcCenter, rotation)`.

### LineLineLeadIn (Type 5)

Two-segment straight line approach.

Properties:
- `Length1` (double): first segment length
- `ApproachAngle1` (double): first segment angle. Default: 90.
- `Length2` (double): second segment length
- `ApproachAngle2` (double): direction change. Default: 90.

Generates: `RapidMove(piercePoint)` → `LinearMove(midPoint)` → `LinearMove(contourStartPoint)`.

### CleanHoleLeadIn

Specialized for precision circular holes. Same geometry as `LineArcLeadIn` but with hard-coded 135° angle and a `Kerf` property. The overcut (cutting past start to close the hole) is handled at the lead-out, not here.

Properties:
- `LineLength` (double)
- `ArcRadius` (double)
- `Kerf` (double)

## LeadOut Hierarchy

### Abstract Base: `LeadOut`

```csharp
public abstract class LeadOut
{
    public abstract List<ICode> Generate(Vector contourEndPoint, double contourNormalAngle,
        RotationType winding = RotationType.CW);
}
```

- `contourEndPoint`: where the contour cut ends. For closed contours, same as start.
- Returns ICode instructions appended after the contour's last cut point.

### NoLeadOut (Type 0)

Returns empty list. Cut ends exactly at contour end.

### LineLeadOut (Type 1)

Straight line overcut past contour end.

Properties:
- `Length` (double): overcut distance
- `ApproachAngle` (double): direction relative to contour tangent. Default: 90.

Generates: `LinearMove(endPoint)` where endPoint is offset from contourEndPoint.

### ArcLeadOut (Type 3)

Arc overcut curving away from the part.

Properties:
- `Radius` (double)

Arc center at `contourEndPoint + Radius` along normal. End point is a quarter turn away. Arc rotation direction matches contour winding.

Generates: `ArcMove(endPoint, arcCenter, rotation)`.

### MicrotabLeadOut (Type 4)

Stops short of contour end, leaving an uncut bridge. Laser only.

Properties:
- `GapSize` (double): uncut material length. Default: 0.03".

Does NOT add instructions — returns empty list. The `ContourCuttingStrategy` detects this type and trims the last cutting move by `GapSize` instead.

## Tab Hierarchy

Tabs are mid-contour features that temporarily lift the beam to leave bridges holding the part in place.

### Abstract Base: `Tab`

```csharp
public abstract class Tab
{
    public double Size { get; set; } = 0.03;
    public LeadIn TabLeadIn { get; set; }
    public LeadOut TabLeadOut { get; set; }

    public abstract List<ICode> Generate(
        Vector tabStartPoint, Vector tabEndPoint, double contourNormalAngle);
}
```

### NormalTab

Standard tab: cut up to tab start, lift/rapid over gap, resume cutting.

Additional properties:
- `CutoutMinWidth`, `CutoutMinHeight` (double): minimum cutout size to receive this tab
- `CutoutMaxWidth`, `CutoutMaxHeight` (double): maximum cutout size to receive this tab
- `AppliesToCutout(double width, double height)` method for size filtering

Generates: TabLeadOut codes → `RapidMove(tabEndPoint)` → TabLeadIn codes.

### BreakerTab

Like NormalTab but adds a scoring cut into the part at the tab location to make snapping easier.

Additional properties:
- `BreakerDepth` (double): how far the score cuts into the part
- `BreakerLeadInLength` (double)
- `BreakerAngle` (double)

Generates: TabLeadOut codes → `LinearMove(scoreEnd)` → `RapidMove(tabEndPoint)` → TabLeadIn codes.

### MachineTab

Tab behavior configured at the CNC controller level. OpenNest just signals the controller.

Additional properties:
- `MachineTabId` (int): passed to post-processor for M-code translation

Returns a placeholder `RapidMove(tabEndPoint)` — the post-processor plugin replaces this with machine-specific commands.

## CuttingParameters

One instance per material/machine combination. Ties everything together.

```csharp
public class CuttingParameters
{
    public int Id { get; set; }

    // Material/Machine identification
    public string MachineName { get; set; }
    public string MaterialName { get; set; }
    public string Grade { get; set; }
    public double Thickness { get; set; }

    // Kerf and spacing
    public double Kerf { get; set; }
    public double PartSpacing { get; set; }

    // External contour lead-in/out
    public LeadIn ExternalLeadIn { get; set; } = new NoLeadIn();
    public LeadOut ExternalLeadOut { get; set; } = new NoLeadOut();

    // Internal contour lead-in/out
    public LeadIn InternalLeadIn { get; set; } = new LineLeadIn { Length = 0.125, Angle = 90 };
    public LeadOut InternalLeadOut { get; set; } = new NoLeadOut();

    // Arc/circle specific (overrides internal for circular features)
    public LeadIn ArcCircleLeadIn { get; set; } = new NoLeadIn();
    public LeadOut ArcCircleLeadOut { get; set; } = new NoLeadOut();

    // Tab configuration
    public Tab TabConfig { get; set; }
    public bool TabsEnabled { get; set; } = false;

    // Sequencing and assignment
    public SequenceParameters Sequencing { get; set; } = new SequenceParameters();
    public AssignmentParameters Assignment { get; set; } = new AssignmentParameters();
}
```

## SequenceParameters and AssignmentParameters

```csharp
// Values match PEP Technology's numbering scheme (value 6 intentionally skipped)
public enum SequenceMethod
{
    RightSide = 1, LeastCode = 2, Advanced = 3,
    BottomSide = 4, EdgeStart = 5, LeftSide = 7, RightSideAlt = 8
}

public class SequenceParameters
{
    public SequenceMethod Method { get; set; } = SequenceMethod.Advanced;
    public double SmallCutoutWidth { get; set; } = 1.5;
    public double SmallCutoutHeight { get; set; } = 1.5;
    public double MediumCutoutWidth { get; set; } = 8.0;
    public double MediumCutoutHeight { get; set; } = 8.0;
    public double DistanceMediumSmall { get; set; }
    public bool AlternateRowsColumns { get; set; } = true;
    public bool AlternateCutoutsWithinRowColumn { get; set; } = true;
    public double MinDistanceBetweenRowsColumns { get; set; } = 0.25;
}

public class AssignmentParameters
{
    public SequenceMethod Method { get; set; } = SequenceMethod.Advanced;
    public string Preference { get; set; } = "ILAT";
    public double MinGeometryLength { get; set; } = 0.01;
}
```

## ContourCuttingStrategy

The orchestrator. Uses `ShapeProfile` to decompose a part into perimeter + cutouts, then sequences and applies cutting parameters using nearest-neighbor chaining from an exit point.

### Exit Point from Plate Quadrant

The exit point is the **opposite corner** of the plate from the quadrant origin. This is where the head ends up after traversing the plate, and is the starting point for backwards nearest-neighbor sequencing.

| Quadrant | Origin | Exit Point |
|----------|--------|------------|
| 1 | TopRight | BottomLeft (0, 0) |
| 2 | TopLeft | BottomRight (width, 0) |
| 3 | BottomLeft | TopRight (width, length) |
| 4 | BottomRight | TopLeft (0, length) |

The exit point is derived from `Plate.Quadrant` and `Plate.Size` — not passed in manually.

### Approach

Instead of requiring `Program.GetStartPoint()` / `GetNormalAtStart()` (which don't exist), the strategy:

1. Computes the **exit point** from the plate's quadrant and size
2. Converts the program to geometry via `Program.ToGeometry()`
3. Builds a `ShapeProfile` from the geometry — gives `Perimeter` (Shape) and `Cutouts` (List&lt;Shape&gt;)
4. Uses `Shape.ClosestPointTo(point, out Entity entity)` to find lead-in points and the entity for normal computation
5. Chains cutouts by nearest-neighbor distance from the perimeter closest point
6. Reverses the chain → cut order is cutouts first (nearest-last), perimeter last

### Contour Re-Indexing

After `ClosestPointTo` finds the lead-in point on a shape, the shape's entity list must be reordered so that cutting starts at that point. This means:

1. Find which entity in `Shape.Entities` contains the closest point
2. Split that entity at the closest point into two segments
3. Reorder: second half of split entity → remaining entities in order → first half of split entity
4. The contour now starts and ends at the lead-in point (for closed contours)

This produces the `List<ICode>` for the contour body that goes between the lead-in and lead-out codes.

### ContourType Detection

- `ShapeProfile.Perimeter` → `ContourType.External`
- Each cutout in `ShapeProfile.Cutouts`:
  - If single entity and entity is `Circle` → `ContourType.ArcCircle`
  - Otherwise → `ContourType.Internal`

### Normal Angle Computation

Derived from the `out Entity` returned by `ClosestPointTo`:

- **Line**: normal is perpendicular to line direction. Use the line's tangent angle, then add π/2 for the normal pointing away from the part interior.
- **Arc/Circle**: normal is radial direction from arc center to the closest point: `closestPoint.AngleFrom(arc.Center)`.

Normal direction convention: always points **away from the part material** (outward from perimeter, inward toward scrap for cutouts). The lead-in approaches from this direction.

### Arc Rotation Direction

Lead-in/lead-out arcs must match the **contour winding direction**, not be hardcoded CW. Determine winding from the shape's entity traversal order. Pass the appropriate `RotationType` to `ArcMove`.

### Method Signature

```csharp
public class ContourCuttingStrategy
{
    public CuttingParameters Parameters { get; set; }

    /// <summary>
    /// Apply cutting strategy to a part's program.
    /// </summary>
    /// <param name="partProgram">Original part program (unmodified).</param>
    /// <param name="plate">Plate for quadrant/size to compute exit point.</param>
    /// <returns>New Program with lead-ins, lead-outs, and tabs applied. Cutouts first, perimeter last.</returns>
    public Program Apply(Program partProgram, Plate plate)
    {
        // 1. Compute exit point from plate quadrant + size
        // 2. Convert to geometry, build ShapeProfile
        // 3. Find closest point on perimeter from exitPoint
        // 4. Chain cutouts by nearest-neighbor from perimeter point
        // 5. Reverse chain → cut order
        // 6. For each contour:
        //    a. Re-index shape entities to start at closest point
        //    b. Detect ContourType
        //    c. Compute normal angle from entity
        //    d. Select lead-in/out from CuttingParameters by ContourType
        //    e. Generate lead-in codes + contour body + lead-out codes
        // 7. Handle MicrotabLeadOut by trimming last segment
        // 8. Assemble and return new Program
    }
}
```

### ContourType Enum

```csharp
public enum ContourType
{
    External,
    Internal,
    ArcCircle
}
```

## Integration Point

`ContourCuttingStrategy.Apply()` runs at nest-time (when parts are placed or cutting parameters are assigned), not at post-processing time. The output `Program` — with lead-ins, lead-outs, start points, and contour ordering — is stored on the `Part` and saved through the normal `NestWriter` path. The post-processor receives this already-complete program and only translates it to machine-specific G-code.

## Out of Scope (Deferred)

- **Serialization** of CuttingParameters (JSON/XML discriminators)
- **UI integration** (parameter editor forms in WinForms app)
- **Part.CutProgram property** (storing the strategy-applied program on `Part`, separate from `Drawing.Program`)
- **Tab insertion logic** (`InsertTabs` / `TrimLastSegment` — stubbed with `NotImplementedException`)
