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

### Computation Method

Use the **Minkowski sum** approach: NFP(A, B) = A ⊕ (-B), where -B is B reflected through its reference point.

For convex polygons this is a simple merge-sort of edge vectors. For concave polygons, decompose into convex pieces first, compute partial NFPs, then take the union.

### Input

Polygons come from the existing conversion chain:

```
Drawing.Program
  → ConvertProgram.ToGeometry()           // recursively expands SubProgramCalls
  → Helper.GetShapes()                    // chains connected entities into Shapes
  → DefinedShape                          // separates perimeter from cutouts
  → .Perimeter                            // outer boundary
  → .ToPolygonWithTolerance(circumscribe) // polygon with arcs circumscribed
```

Only `DefinedShape.Perimeter` is used for NFP — cutouts do not affect part-to-part collision. Spacing is applied by inflating the perimeter polygon by half-spacing before NFP computation (consistent with existing `PartBoundary` approach).

### Concave Polygon Handling

Many real parts are concave. The approach:
1. Decompose concave polygon into convex sub-polygons (ear-clipping or Hertel-Mehlhorn)
2. Compute NFP for each convex pair combination
3. Union the partial NFPs into the final NFP

### NFP Cache

NFPs are keyed by `(DrawingA.Id, RotationA, DrawingB.Id, RotationB)` and computed once per unique combination. During SA optimization, the same NFPs are reused thousands of times.

**Type:** `NfpCache` — dictionary-based lookup with lazy computation.

### New Types

| Type | Namespace | Purpose |
|------|-----------|---------|
| `NoFitPolygon` | `OpenNest.Geometry` | Static methods for NFP computation between two polygons |
| `InnerFitPolygon` | `OpenNest.Geometry` | Static methods for IFP (part inside plate boundary) |
| `NfpCache` | `OpenNest.Engine` | Caches computed NFPs keyed by drawing/rotation pairs |

## Layer 2: Inner-Fit Polygon (IFP)

The IFP answers "where can this part be placed inside the plate?" It is the NFP of the plate boundary with the part, but inverted — the feasible region is the interior.

For a rectangular plate and a convex part, the IFP is a smaller rectangle (plate shrunk by part dimensions). For concave parts the IFP boundary follows the plate edges inset by the part's profile.

### Feasible Region

For placing part P(i) given already-placed parts P(1)...P(i-1):

```
FeasibleRegion = IFP(plate, P(i))  ∩  complement(NFP(P(1), P(i)))  ∩  ...  ∩  complement(NFP(P(i-1), P(i)))
```

The placement point is the bottom-left-most point on the feasible region boundary.

## Layer 3: NFP-Based Bottom-Left Fill (BLF)

### Algorithm

```
BLF(sequence, plate, nfpCache):
    placedParts = []
    for each (drawing, rotation) in sequence:
        polygon = getPerimeterPolygon(drawing, rotation)
        ifp = InnerFitPolygon.Compute(plate, polygon)
        nfps = [nfpCache.Get(placed, polygon) for placed in placedParts]
        feasible = ifp minus union(nfps)
        point = bottomLeftMost(feasible)
        if point exists:
            place part at point
            placedParts.append(part)
    return FillScore.Compute(placedParts, plate)
```

### Bottom-Left Point Selection

"Bottom-left" means: minimize X first, then minimize Y (leftmost, then lowest). This matches the existing `PackBottomLeft.FindPointVertical` priority but operates on polygon feasible regions instead of discrete candidate points.

### Replaces

This replaces `PackBottomLeft` for mixed-part scenarios. The existing rectangle-based `PackBottomLeft` remains available for pure-rectangle use cases.

## Layer 4: Simulated Annealing Optimizer

### Interface

```csharp
interface INestOptimizer
{
    NestResult Optimize(List<NestItem> items, Plate plate, NfpCache cache);
}
```

This abstraction allows swapping SA for GA (or other meta-heuristics) in the future without touching the NFP or BLF layers.

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
- **Termination:** Temperature below threshold OR no improvement for N consecutive iterations
- **Quality focus:** Since quality > speed, allow long runs (thousands of iterations)

### Candidate Rotations

Per drawing, candidate rotations are determined by existing logic:
- Hull-edge angles from `RotationAnalysis.FindHullEdgeAngles()`
- 0 degrees baseline
- Fixed-increment sweep (e.g., 5 degrees) for small work areas

### Initial Solution

Generate the initial sequence by sorting parts largest-area-first (matches existing `PackBottomLeft` heuristic). This gives SA a reasonable starting point rather than random.

## Integration: NestEngine.AutoNest

New public entry point:

```csharp
public List<Part> AutoNest(List<NestItem> items, Plate plate)
```

### Flow

1. Extract perimeter polygons via `DefinedShape` for each unique drawing
2. Inflate perimeters by half-spacing
3. Compute candidate rotations per drawing
4. Pre-compute `NfpCache` for all (drawing, rotation) pair combinations
5. Run `INestOptimizer.Optimize()` → best sequence
6. Final BLF placement with best solution → placed `Part` instances
7. Return parts

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

## Scoring

Results are scored using existing `FillScore` (lexicographic: count → usable remnant area → density). No changes needed.

## Project Location

All new types go in existing projects:
- `NoFitPolygon`, `InnerFitPolygon` → `OpenNest.Core/Geometry/`
- `NfpCache`, BLF engine, SA optimizer, `INestOptimizer` → `OpenNest.Engine/`
- `AutoNest` method → `NestEngine.cs`
