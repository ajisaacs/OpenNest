# NFP-Based Autonesting Design

## Problem

OpenNest's current nesting engine handles single-drawing plate fills well (FillLinear, FillBestFit, FillWithPairs), but cannot produce mixed-part layouts — placing multiple different drawings together on a plate with true geometry-aware interlocking. The existing `PackBottomLeft` supports mixed parts but operates on bounding-box rectangles only, wasting the concave space around irregular shapes.

Commercial nesting software (e.g., PEP at $30k) produces mixed-part layouts but with mediocre results — significant gaps and poor material utilization.

## Goal

Build an NFP-based mixed-part autonester that:
- Places multiple different drawings on a plate with true geometry-aware collision avoidance
- Produces tighter layouts than bounding-box packing by allowing parts to interlock
- Uses simulated annealing to optimize part ordering and rotation
- Integrates cleanly alongside existing Fill/Pack methods in NestEngine
- Provides an abstracted optimizer interface for future GA/parallel upgrades

## Dependencies

**Clipper2** (NuGet: `Clipper2Lib`, MIT license) — provides polygon boolean operations (union, difference, intersection) and polygon offsetting. Required for feasible region computation and perimeter inflation. Added to `OpenNest.Core`.

## Architecture

Three layers, built bottom-up:

```
┌──────────────────────────────────┐
│  Simulated Annealing Optimizer   │  Layer 3: searches orderings/rotations
│  (implements INestOptimizer)     │
├──────────────────────────────────┤
│  NFP-Based Bottom-Left Fill      │  Layer 2: places parts using feasible regions
│  (BLF placement engine)         │
├──────────────────────────────────┤
│  NFP / IFP Computation + Cache   │  Layer 1: geometric foundation
└──────────────────────────────────┘
```

## Layer 1: No-Fit Polygon (NFP) Computation

### What is an NFP?

The No-Fit Polygon between a stationary polygon A and an orbiting polygon B defines all positions where B's reference point would cause A and B to overlap. If B's reference point is:
- **Inside** the NFP → overlap
- **On the boundary** → touching
- **Outside** → no overlap

### Computation Method: Convex Decomposition

Use the **Minkowski sum** approach: NFP(A, B) = A ⊕ (-B), where -B is B reflected through its reference point.

For convex polygons this is a simple merge-sort of edge vectors — O(n+m) where n and m are vertex counts.

For concave polygons (most real CNC parts), use decomposition:
1. Decompose each concave polygon into convex sub-polygons using ear-clipping triangulation (produces O(n) triangles per polygon)
2. For each pair of convex pieces (one from A, one from -B), compute the convex Minkowski sum
3. Union all partial results using Clipper2 to produce the final NFP

The number of convex pair computations is O(t_A * t_B) where t_A and t_B are the triangle counts. Each convex Minkowski sum is O(n+m) ≈ O(6) for triangle pairs, so the cost is dominated by the Clipper2 union step.

### Input

Polygons come from the existing conversion chain:

```
Drawing.Program
  → ConvertProgram.ToGeometry()           // recursively expands SubProgramCalls
  → Helper.GetShapes()                    // chains connected entities into Shapes
  → DefinedShape                          // separates perimeter from cutouts
  → .Perimeter                            // outer boundary
  → .OffsetEntity(halfSpacing)            // inflate at Shape level (offset is implemented on Shape)
  → .ToPolygonWithTolerance(circumscribe) // polygon with arcs circumscribed
```

Only `DefinedShape.Perimeter` is used for NFP — cutouts do not affect part-to-part collision. Spacing is applied by inflating the perimeter shape by half-spacing on each part (so two adjacent parts have full spacing between them). Inflation is done at the `Shape` level via `Shape.OffsetEntity()` before polygon conversion, consistent with how `PartBoundary` works today.

### Polygon Vertex Budget

`Shape.ToPolygonWithTolerance` with tolerance 0.01 can produce hundreds of vertices for arc-heavy parts. For NFP computation, a coarser tolerance (e.g., 0.05-0.1) may be used to keep triangle counts reasonable. The exact tolerance should be tuned during implementation — start with the existing 0.01 and coarsen only if profiling shows NFP computation is a bottleneck.

### NFP Cache

NFPs are keyed by `(DrawingA.Id, RotationA, DrawingB.Id, RotationB)` and computed once per unique combination. During SA optimization, the same NFPs are reused thousands of times.

`Drawing.Id` is a new `int` property added to the `Drawing` class, auto-assigned on construction.

**Type:** `NfpCache` — dictionary-based lookup with lazy computation (compute on first access, store for reuse).

For N drawings with R candidate rotations, the cache holds up to N^2 * R^2 entries. With typical values (6 drawings, 10 rotations), that's ~3,600 entries — well within memory.

### New Types

| Type | Project | Namespace | Purpose |
|------|---------|-----------|---------|
| `NoFitPolygon` | Core | `OpenNest.Geometry` | Static methods for NFP computation between two polygons |
| `InnerFitPolygon` | Core | `OpenNest.Geometry` | Static methods for IFP (part inside plate boundary) |
| `ConvexDecomposition` | Core | `OpenNest.Geometry` | Ear-clipping triangulation of concave polygons |
| `NfpCache` | Engine | `OpenNest` | Caches computed NFPs keyed by drawing ID/rotation pairs |

## Layer 2: Inner-Fit Polygon (IFP)

The IFP answers "where can this part be placed inside the plate?" It is the NFP of the plate boundary with the part, but inverted — the feasible region is the interior.

For a rectangular plate and a convex part, the IFP is a smaller rectangle (plate shrunk by part dimensions). For concave parts the IFP boundary follows the plate edges inset by the part's profile.

### Feasible Region

For placing part P(i) given already-placed parts P(1)...P(i-1):

```
FeasibleRegion = IFP(plate, P(i))  minus  union(NFP(P(1), P(i)), ..., NFP(P(i-1), P(i)))
```

Both the union and difference operations use Clipper2.

The placement point is the bottom-left-most point on the feasible region boundary.

## Layer 3: NFP-Based Bottom-Left Fill (BLF)

### Algorithm

```
BLF(sequence, plate, nfpCache):
    placedParts = []
    for each (drawing, rotation) in sequence:
        polygon = getPerimeterPolygon(drawing, rotation)
        ifp = InnerFitPolygon.Compute(plate.WorkArea, polygon)
        nfps = [nfpCache.Get(placed, polygon) for placed in placedParts]
        feasible = Clipper2.Difference(ifp, Clipper2.Union(nfps))
        point = bottomLeftMost(feasible)
        if point exists:
            place part at point
            placedParts.append(part)
    return FillScore.Compute(placedParts, plate.WorkArea)
```

### Bottom-Left Point Selection

"Bottom-left" means: minimize Y first (lowest), then minimize X (leftmost). This is standard BLF convention in the nesting literature. The feasible region boundary is walked to find the point with the smallest Y coordinate, breaking ties by smallest X.

Note: the existing `PackBottomLeft.FindPointVertical` uses leftmost-first priority. The new BLF uses lowest-first to match established nesting algorithms. Both remain available.

### Replaces

This replaces `PackBottomLeft` for mixed-part scenarios. The existing rectangle-based `PackBottomLeft` remains available for pure-rectangle use cases.

## Layer 4: Simulated Annealing Optimizer

### Interface

```csharp
interface INestOptimizer
{
    NestResult Optimize(List<NestItem> items, Plate plate, NfpCache cache,
                        CancellationToken cancellation = default);
}
```

This abstraction allows swapping SA for GA (or other meta-heuristics) in the future without touching the NFP or BLF layers. `CancellationToken` enables UI responsiveness and user-initiated cancellation for long-running optimizations.

### State Representation

The SA state is a sequence of `(DrawingId, RotationAngle)` tuples — one entry per physical part to place (respecting quantities from NestItem). The sequence determines placement order for BLF.

### Mutation Operators

Each iteration, one mutation is selected randomly:

| Operator | Description |
|----------|-------------|
| **Swap** | Exchange two parts' positions in the sequence |
| **Rotate** | Change one part's rotation to another candidate angle |
| **Segment reverse** | Reverse a contiguous subsequence |

### Cooling Schedule

- **Initial temperature:** Calibrated so ~80% of worse moves are accepted initially
- **Cooling rate:** Geometric (T = T * alpha, alpha ~0.995-0.999)
- **Termination:** Temperature below threshold OR no improvement for N consecutive iterations OR cancellation requested
- **Quality focus:** Since quality > speed, allow long runs (thousands of iterations)

### Candidate Rotations

Per drawing, candidate rotations are determined by existing logic:
- Hull-edge angles from `RotationAnalysis.FindHullEdgeAngles()` (needs a new overload accepting a `Drawing` or `Polygon` instead of `List<Part>`)
- 0 degrees baseline
- Fixed-increment sweep (e.g., 5 degrees) for small work areas

### Initial Solution

Generate the initial sequence by sorting parts largest-area-first (matches existing `PackBottomLeft` heuristic). This gives SA a reasonable starting point rather than random.

## Integration: NestEngine.AutoNest

New public entry point:

```csharp
public List<Part> AutoNest(List<NestItem> items, Plate plate,
                           CancellationToken cancellation = default)
```

### Flow

1. Extract perimeter polygons via `DefinedShape` for each unique drawing
2. Inflate perimeters by half-spacing at the `Shape` level via `OffsetEntity()`
3. Convert to polygons via `ToPolygonWithTolerance(circumscribe: true)`
4. Compute candidate rotations per drawing
5. Pre-compute `NfpCache` for all (drawing, rotation) pair combinations
6. Run `INestOptimizer.Optimize()` → best sequence
7. Final BLF placement with best solution → placed `Part` instances
8. Return parts

### Error Handling

- Drawing produces no valid perimeter polygon → skip drawing, log warning
- No parts fit on plate → return empty list
- NFP produces degenerate polygon (zero area) → treat as non-overlapping (safe fallback)

### Coexistence with Existing Methods

| Method | Use Case | Status |
|--------|----------|--------|
| `Fill(NestItem)` | Single-drawing plate fill (tiling) | Unchanged |
| `Pack(List<NestItem>)` | Rectangle bin packing | Unchanged |
| `AutoNest(List<NestItem>, Plate)` | Mixed-part geometry-aware nesting | **New** |

## Future: Simplifying FillLinear with NFP

Once NFP infrastructure is in place, `FillLinear.FindCopyDistance` can be replaced with NFP projections:
- Copy distance along any axis = extreme point of NFP projected onto that axis
- Eliminates directional edge filtering, line-to-line sliding, and special-case handling in `PartBoundary`
- `BestFitFinder` pair evaluation simplifies to walking NFP boundaries

This refactor is deferred to a future phase to keep scope focused.

## Future: Nesting Inside Cutouts

`DefinedShape.Cutouts` are preserved but unused in Phase 1. Future optimization: for parts with large interior cutouts, compute IFP of the cutout boundary and attempt to place small parts inside. This requires:
- Minimum cutout size threshold
- Modified BLF that considers cutout interiors as additional placement regions

## Future: Parallel Optimization (Threadripper)

The `INestOptimizer` interface naturally supports parallelism:
- **SA parallelism:** Run multiple independent SA chains with different random seeds, take the best result
- **GA upgrade:** Population-based evaluation is embarrassingly parallel — evaluate N candidate orderings simultaneously on N threads
- NFP cache is read-only during optimization, so it's inherently thread-safe

## Future: Orbiting NFP Algorithm

If convex decomposition proves too slow for vertex-heavy parts, the orbiting (edge-tracing) method computes NFPs directly on concave polygons without decomposition. Deferred unless profiling identifies decomposition as a bottleneck.

## Scoring

Results are scored using existing `FillScore` (lexicographic: count → usable remnant area → density). `FillScore.Compute` takes `(List<Part>, Box workArea)` — pass `plate.WorkArea` as the Box. No changes to FillScore needed.

## Project Location

All new types go in existing projects:
- `NoFitPolygon`, `InnerFitPolygon`, `ConvexDecomposition` → `OpenNest.Core/Geometry/`
- `NfpCache`, BLF engine, SA optimizer, `INestOptimizer` → `OpenNest.Engine/`
- `AutoNest` method → `NestEngine.cs`
- `Drawing.Id` property → `OpenNest.Core/Drawing.cs`
- Clipper2 NuGet → `OpenNest.Core.csproj`
