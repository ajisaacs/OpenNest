# Plate Processor Design — Per-Part Lead-In Assignment & Cut Sequencing

## Overview

Add a plate-level orchestrator (`PlateProcessor`) to `OpenNest.Engine` that sequences parts across a plate, assigns lead-ins per-part based on approach direction, and plans safe rapid paths between parts. This replaces the current `ContourCuttingStrategy` usage model where the exit point is derived from the plate corner alone — instead, each part's lead-in pierce point is computed from the actual approach direction (the previous part's last cut point).

The motivation is laser head safety: on a CL-980 fiber laser, head-down rapids are significantly faster than raising the head, but traversing over already-cut areas risks collision with tipped-up slugs. The orchestrator must track cut areas and choose safe rapid paths.

## Architecture

Three pipeline stages, wired by a thin orchestrator:

```
IPartSequencer → ContourCuttingStrategy → IRapidPlanner
       ↓                    ↓                    ↓
  ordered parts      lead-ins applied      safe rapid paths
       └──────────── PlateProcessor ─────────────┘
```

All new code lives in `OpenNest.Engine/` except the `ContourCuttingStrategy` signature change and `Part.HasManualLeadIns` flag which are in `OpenNest.Core`.

## Model Changes

### Part (OpenNest.Core)

Add a flag to indicate the user has manually assigned lead-ins to this part:

```csharp
public bool HasManualLeadIns { get; set; }
```

When `true`, the orchestrator skips `ContourCuttingStrategy.Apply()` for this part and uses the program as-is.

### ContourCuttingStrategy (OpenNest.Core)

Change the `Apply` signature to accept an approach point instead of a plate:

```csharp
// Before
public Program Apply(Program partProgram, Plate plate)

// After
public CuttingResult Apply(Program partProgram, Vector approachPoint)
```

Remove `GetExitPoint(Plate)` — the caller provides the approach point in part-local coordinates.

### CuttingResult (OpenNest.Core, namespace OpenNest.CNC.CuttingStrategy)

New readonly struct returned by `ContourCuttingStrategy.Apply()`. Lives in `CNC/CuttingStrategy/CuttingResult.cs`:

```csharp
public readonly struct CuttingResult
{
    public Program Program { get; init; }
    public Vector LastCutPoint { get; init; }
}
```

- `Program`: the program with lead-ins/lead-outs applied.
- `LastCutPoint`: where the last contour cut ends, in part-local coordinates. The orchestrator transforms this to plate coordinates to compute the approach point for the next part.

## Stage 1: IPartSequencer

### Interface

```csharp
namespace OpenNest.Engine
{
    public interface IPartSequencer
    {
        List<SequencedPart> Sequence(IReadOnlyList<Part> parts, Plate plate);
    }
}
```

### SequencedPart

```csharp
public readonly struct SequencedPart
{
    public Part Part { get; init; }
}
```

The sequencer only determines cut order. Approach points are computed by the orchestrator as it loops, since each part's approach point depends on the previous part's `CuttingResult.LastCutPoint`.

### Implementations

One class per `SequenceMethod`. All live in `OpenNest.Engine/Sequencing/`.

| Class | SequenceMethod | Algorithm |
|-------|---------------|-----------|
| `RightSideSequencer` | RightSide | Sort parts by X descending (rightmost first) |
| `LeftSideSequencer` | LeftSide | Sort parts by X ascending (leftmost first) |
| `BottomSideSequencer` | BottomSide | Sort parts by Y ascending (bottom first) |
| `LeastCodeSequencer` | LeastCode | Nearest-neighbor from exit point, then 2-opt improvement |
| `AdvancedSequencer` | Advanced | Nearest-neighbor with row/column grouping from `SequenceParameters` |
| `EdgeStartSequencer` | EdgeStart | Sort by distance from nearest plate edge, closest first |

#### Directional sequencers (RightSide, LeftSide, BottomSide)

Sort parts by their bounding box center along the relevant axis. Ties broken by the perpendicular axis. These are simple positional sorts — no TSP involved.

#### LeastCodeSequencer

1. Start from the plate exit point.
2. Nearest-neighbor greedy: pick the unvisited part whose bounding box center is closest to the current position.
3. 2-opt improvement: iterate over the sequence, try swapping pairs. If total travel distance decreases, keep the swap. Repeat until no improvement found (or max iterations).

#### AdvancedSequencer

Uses `SequenceParameters` to group parts into rows/columns based on `MinDistanceBetweenRowsColumns`, then sequences within each group. `AlternateRowsColumns` and `AlternateCutoutsWithinRowColumn` control serpentine vs. unidirectional ordering within rows.

#### EdgeStartSequencer

Sort parts by distance from the nearest plate edge (minimum of distances to all four edges). Parts closest to an edge cut first. Ties broken by nearest-neighbor.

### Parameter Flow

Sequencers that need configuration accept it through their constructor:
- `LeastCodeSequencer(int maxIterations = 100)` — max 2-opt iterations
- `AdvancedSequencer(SequenceParameters parameters)` — row/column grouping config
- Directional sequencers and `EdgeStartSequencer` need no configuration

### Factory

A static `PartSequencerFactory.Create(SequenceParameters parameters)` method in `OpenNest.Engine/Sequencing/` maps `parameters.Method` to the correct `IPartSequencer` implementation, passing constructor args as needed. Throws `NotSupportedException` for `RightSideAlt`.

## Stage 2: ContourCuttingStrategy

Already exists in `OpenNest.Core/CNC/CuttingStrategy/`. Only the signature and return type change:

1. `Apply(Program partProgram, Plate plate)` → `Apply(Program partProgram, Vector approachPoint)`
2. Return `CuttingResult` instead of `Program`
3. Remove `GetExitPoint(Plate)` — replaced by the `approachPoint` parameter
4. Set `CuttingResult.LastCutPoint` to the end point of the last contour (perimeter), which is the same as the perimeter's reindexed start point for closed contours

The internal logic (cutout sequencing, contour type detection, normal computation, lead-in/out selection) remains unchanged — only the source of the approach direction changes.

## Stage 3: IRapidPlanner

### Interface

```csharp
namespace OpenNest.Engine
{
    public interface IRapidPlanner
    {
        RapidPath Plan(Vector from, Vector to, IReadOnlyList<Shape> cutAreas);
    }
}
```

All coordinates are in plate space.

### RapidPath

```csharp
public readonly struct RapidPath
{
    public bool HeadUp { get; init; }
    public List<Vector> Waypoints { get; init; }
}
```

- `HeadUp = true`: the post-processor should raise Z before traversing. `Waypoints` is empty (direct move).
- `HeadUp = false`: head-down rapid. `Waypoints` contains the path (may be empty for a direct move, or contain intermediate points for obstacle avoidance in future implementations).

### Implementations

Both live in `OpenNest.Engine/RapidPlanning/`.

#### SafeHeightRapidPlanner

Always returns `HeadUp = true` with empty waypoints. Guaranteed safe, simplest possible implementation.

#### DirectRapidPlanner

Checks if the straight line from `from` to `to` intersects any shape in `cutAreas`:
- If clear: returns `HeadUp = false`, empty waypoints (direct head-down rapid).
- If blocked: returns `HeadUp = true`, empty waypoints (fall back to safe height).

Uses existing `Intersect` class from `OpenNest.Geometry` for line-shape intersection checks.

Future enhancement: obstacle-avoidance pathfinding that routes around cut areas with head down. This is a 2D pathfinding problem (visibility graph or similar) and is out of scope for the initial implementation.

## PlateProcessor (Orchestrator)

Lives in `OpenNest.Engine/PlateProcessor.cs`.

```csharp
public class PlateProcessor
{
    public IPartSequencer Sequencer { get; set; }
    public ContourCuttingStrategy CuttingStrategy { get; set; }
    public IRapidPlanner RapidPlanner { get; set; }

    public PlateResult Process(Plate plate)
    {
        // 1. Sequence parts
        var ordered = Sequencer.Sequence(plate.Parts, plate);

        var results = new List<ProcessedPart>();
        var cutAreas = new List<Shape>();
        var currentPoint = GetExitPoint(plate);  // plate-space starting point

        foreach (var sequenced in ordered)
        {
            var part = sequenced.Part;

            // 2. Transform approach point from plate space to part-local space
            var localApproach = ToPartLocal(currentPoint, part);

            // 3. Apply lead-ins (or skip if manual)
            CuttingResult cutResult;
            if (!part.HasManualLeadIns && CuttingStrategy != null)
            {
                cutResult = CuttingStrategy.Apply(part.Program, localApproach);
            }
            else
            {
                cutResult = new CuttingResult
                {
                    Program = part.Program,
                    LastCutPoint = GetProgramEndPoint(part.Program)
                };
            }

            // 4. Get pierce point in plate space for rapid planning
            var piercePoint = ToPlateSpace(GetProgramStartPoint(cutResult.Program), part);

            // 5. Plan rapid from current position to this part's pierce point
            var rapid = RapidPlanner.Plan(currentPoint, piercePoint, cutAreas);

            results.Add(new ProcessedPart
            {
                Part = part,
                ProcessedProgram = cutResult.Program,
                RapidPath = rapid
            });

            // 6. Track cut area (part perimeter in plate space) for future rapid planning
            cutAreas.Add(GetPartPerimeter(part));

            // 7. Update current position to this part's last cut point (plate space)
            currentPoint = ToPlateSpace(cutResult.LastCutPoint, part);
        }

        return new PlateResult { Parts = results };
    }
}
```

### Coordinate Transforms

Part programs already have rotation baked in (the `Part` constructor calls `Program.Rotate()`). `Part.Location` is a pure translation offset. Therefore, coordinate transforms are simple vector addition/subtraction — no rotation involved:

- `ToPartLocal(Vector platePoint, Part part)`: `platePoint - part.Location`
- `ToPlateSpace(Vector localPoint, Part part)`: `localPoint + part.Location`

This matches how `Part.Intersects` converts to plate space (offset by `Location` only).

### Helper Methods

- `GetExitPoint(Plate)`: moved from `ContourCuttingStrategy` — returns the plate corner opposite the quadrant origin.
- `GetProgramStartPoint(Program)`: first `RapidMove` position in the program (the pierce point).
- `GetProgramEndPoint(Program)`: last move's end position in the program.
- `GetPartPerimeter(Part)`: converts the part's program to geometry, builds `ShapeProfile`, returns the perimeter `Shape` offset by `part.Location` (translation only — rotation is already baked in).

### PlateResult

```csharp
public class PlateResult
{
    public List<ProcessedPart> Parts { get; init; }
}

public readonly struct ProcessedPart
{
    public Part Part { get; init; }
    public Program ProcessedProgram { get; init; }  // with lead-ins applied (original Part.Program unchanged)
    public RapidPath RapidPath { get; init; }
}
```

The orchestrator is non-destructive — it does not mutate `Part.Program` (which has a `private set`). Instead, the processed program with lead-ins is stored in `ProcessedPart.ProcessedProgram`. The post-processor consumes `PlateResult` to generate machine-specific G-code, using `ProcessedProgram` for cut paths and `RapidPath.HeadUp` for Z-axis commands.

Note: the caller is responsible for configuring `CuttingStrategy.Parameters` (the `CuttingParameters` instance with lead-in/lead-out settings) before calling `Process()`. Parameters typically vary by material/thickness.

## File Structure

```
OpenNest.Core/
├── Part.cs                                    # add HasManualLeadIns property
└── CNC/CuttingStrategy/
    ├── ContourCuttingStrategy.cs               # signature change + CuttingResult return
    └── CuttingResult.cs                        # new struct

OpenNest.Engine/
├── PlateProcessor.cs                           # orchestrator
├── Sequencing/
│   ├── IPartSequencer.cs
│   ├── SequencedPart.cs                        # removed ApproachPoint (orchestrator tracks it)
│   ├── RightSideSequencer.cs
│   ├── LeftSideSequencer.cs
│   ├── BottomSideSequencer.cs
│   ├── LeastCodeSequencer.cs
│   ├── AdvancedSequencer.cs
│   └── EdgeStartSequencer.cs
└── RapidPlanning/
    ├── IRapidPlanner.cs
    ├── RapidPath.cs
    ├── SafeHeightRapidPlanner.cs
    └── DirectRapidPlanner.cs
```

## Known Limitations

- `DirectRapidPlanner` checks edge intersection only — a rapid that passes entirely through the interior of a concave cut part without crossing a perimeter edge would not be detected. Unlikely in practice (parts have material around them) but worth noting.
- `LeastCodeSequencer` uses bounding box centers for nearest-neighbor distance. For highly irregular parts, closest-point-on-perimeter could yield better results, but the simpler approach is sufficient for the initial implementation.

## Out of Scope

- Obstacle-avoidance pathfinding for head-down rapids (future enhancement to `DirectRapidPlanner`)
- UI integration (selecting sequencing method, configuring rapid planner)
- Post-processor changes to consume `PlateResult` — interim state: `PlateResult` is returned from `Process()` and the caller bridges it to the existing `IPostProcessor` interface
- `RightSideAlt` sequencer (unclear how it differs from `RightSide` — defer until behavior is defined; `PlateProcessor` should throw `NotSupportedException` if selected)
- Serialization of `PlateResult`
