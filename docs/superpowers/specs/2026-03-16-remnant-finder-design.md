# Remnant Finder Design

## Problem

Remnant detection is currently scattered across four places in the codebase, all using simple edge-strip heuristics that miss interior gaps and produce unreliable results:

- `Plate.GetRemnants()` — finds strips along plate edges from global min/max of part bounding boxes
- `DefaultNestEngine.TryRemainderImprovement()` / `TryStripRefill()` / `ClusterParts()` — clusters parts into rows/columns and refills the last incomplete cluster
- `FillScore.ComputeUsableRemnantArea()` — estimates remnant area from rightmost/topmost part edges for fill scoring
- `NestEngineBase.ComputeRemainderWithin()` — picks the larger of one horizontal or vertical strip

These approaches only find single edge strips and cannot discover multiple or interior empty regions.

## Solution

A standalone `RemnantFinder` class in `OpenNest.Engine` that uses edge projection to find all rectangular empty regions in a work area given a set of obstacle bounding boxes. This decouples remnant detection from the nesting engine and enables an iterative workflow:

1. Fill an area
2. Get all remnants
3. Pick a remnant, fill it
4. Get all remnants again (repeat)

## API

### `RemnantFinder` — `OpenNest.Engine`

```csharp
public class RemnantFinder
{
    // Constructor
    public RemnantFinder(Box workArea, List<Box> obstacles = null);

    // Mutable obstacle management
    public List<Box> Obstacles { get; }
    public void AddObstacle(Box obstacle);
    public void AddObstacles(IEnumerable<Box> obstacles);
    public void ClearObstacles();

    // Core method
    public List<Box> FindRemnants(double minDimension = 0);

    // Convenience factory
    public static RemnantFinder FromPlate(Plate plate);
}
```

### `FindRemnants` Algorithm (Edge Projection)

1. Collect all unique X coordinates from obstacle left/right edges + work area left/right.
2. Collect all unique Y coordinates from obstacle bottom/top edges + work area bottom/top.
3. Form candidate rectangles from every adjacent `(x[i], x[i+1])` x `(y[j], y[j+1])` cell in the grid.
4. Filter out any candidate that overlaps any obstacle.
5. Merge adjacent empty cells into larger rectangles — greedy row-first merge: scan cells left-to-right within each row and merge horizontally where cells share the same Y span, then merge vertically where resulting rectangles share the same X span and are adjacent in Y. This produces "good enough" large rectangles without requiring maximal rectangle decomposition.
6. Filter by `minDimension` — both width and height must be >= the threshold.
7. Return sorted by area descending.

### `FromPlate` Factory

Extracts `plate.WorkArea()` as the work area and each part's bounding box offset by `plate.PartSpacing` as obstacles.

## Scoping

The `RemnantFinder` operates on whatever work area it's given. When used within the strip nester or sub-region fills, pass the sub-region's work area and only the parts placed within it — not the full plate. This prevents remnants from spanning into unrelated layout regions.

## Thread Safety

`RemnantFinder` is not thread-safe. Each thread/task should use its own instance. The `FromPlate` factory creates a snapshot of obstacles at construction time, so concurrent reads of the plate during construction should be avoided.

## Removals

### `DefaultNestEngine`

Remove the entire remainder phase:
- `TryRemainderImprovement()`
- `TryStripRefill()`
- `ClusterParts()`
- `NestPhase.Remainder` reporting in both `Fill()` overrides

The engine's `Fill()` becomes single-pass. Iterative remnant filling is the caller's responsibility.

### `NestPhase.Remainder`

Remove the `Remainder` value from the `NestPhase` enum. Clean up corresponding switch cases in:
- `NestEngineBase.FormatPhaseName()`
- `NestProgressForm.FormatPhase()`

### `Plate`

Remove `GetRemnants()` — fully replaced by `RemnantFinder.FromPlate(plate)`.

### `FillScore`

Remove remnant-related members:
- `MinRemnantDimension` constant
- `UsableRemnantArea` property
- `ComputeUsableRemnantArea()` method
- Remnant area from the `CompareTo` ordering

Constructor simplifies from `FillScore(int count, double usableRemnantArea, double density)` to `FillScore(int count, double density)`. The `Compute` factory method drops the `ComputeUsableRemnantArea` call accordingly.

### `NestProgress`

Remove `UsableRemnantArea` property. Update `NestEngineBase.ReportProgress()` to stop computing/setting it. Update `NestProgressForm` to stop displaying it.

### `NestEngineBase`

Replace `ComputeRemainderWithin()` with `RemnantFinder` in the `Nest()` method. The current `Nest()` fills an item, then calls `ComputeRemainderWithin` to get a single remainder box for the next item. Updated behavior: after filling, create a `RemnantFinder` with the current work area and all placed parts, call `FindRemnants()`, and use the largest remnant as the next work area. If no remnants exist, the fill loop stops.

### `StripNestResult`

Remove `RemnantBox` property. The `StripNestEngine.TryOrientation` assignment to `result.RemnantBox` is removed — the value was stored but never read externally. The `StripNestResult` class itself is retained (it still carries `Parts`, `StripBox`, `Score`, `Direction`).

## Caller Updates

### `NestingTools` (MCP)

`fill_remnants` switches from `plate.GetRemnants()` to:
```csharp
var finder = RemnantFinder.FromPlate(plate);
var remnants = finder.FindRemnants(minDimension);
```

### `InspectionTools` (MCP)

`get_plate_info` switches from `plate.GetRemnants()` to `RemnantFinder.FromPlate(plate).FindRemnants()`.

### Debug Logging

`DefaultNestEngine.FillWithPairs()` logs `bestScore.UsableRemnantArea` — update to log only count and density after the `FillScore` simplification.

### UI / Console callers

Any caller that previously relied on `TryRemainderImprovement` getting called automatically inside `Fill()` will need to implement the iterative loop: fill -> find remnants -> fill remnant -> repeat.

## PlateView Remnant Visualization

When a remnant is being filled (during the iterative workflow), the `PlateView` control should display the active remnant's outline as a dashed rectangle in a contrasting color (orange). The outline persists while that remnant is being filled and disappears when the next remnant starts or the fill completes.

**Implementation:** Add a `Box? ActiveRemnant` property to `PlateView`. When set, the paint handler draws a dashed rectangle at that location. The caller sets it before filling a remnant and clears it (sets to `null`) when done or moving to the next remnant. The `NestProgress` class gets a new `Box? ActiveRemnant` property so the progress pipeline can carry the current remnant box from the engine caller to the UI.

## Future

The edge projection algorithm is embarrassingly parallel — each candidate rectangle's overlap check is independent. This makes it a natural fit for GPU acceleration via `OpenNest.Gpu` in the future.
