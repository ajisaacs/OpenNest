# OpenNest.Engine.Astra

An independent, deterministic .NET 8 CNC nesting plugin. Its public parameterless
`AstraNestingEngine` implements `INestingEngine`. The project remains outside `OpenNest.sln`.

## Placement algorithm

Astra searches configuration space: for each stationary/moving orientation pair, a no-fit
polygon describes the translations that would overlap. Subtracting these regions from the
sheet's usable translation rectangle exposes contact positions where another part can fit.
This permits overlapping bounding rectangles, complementary triangle pairs, staggered circles,
concave interlocking, and insertion into straight-edged and curved holes.

1. Validate immutable job input. Reconstruct owned analytic entities with `DrawingJobMapper`
   and `ConvertProgram`. Closed contours define material; internal open marks do not become
   holes. Preserve the snapshot's origin when converting normalized placements back to poses.
2. Prepare rotated outlines and material regions with holes, using conservative curve flattening.
   Automatic angles combine 15-degree samples over a full turn with orientations aligned to the
   longest straight edges. Symmetric duplicates are removed. Prefer up to 16 orientations whose
   envelope area is within 8% of the minimum; retain additional orientations when needed to fit
   a candidate stock. Fixed and bounded rotation policies remain enforced. Bounded sweeps use
   up to 721 integer step indices, including permitted half-turn equivalents.
3. Process high-priority parts first, then parts fitting fewer available stock types, then larger
   envelopes. Larger frames precede inserts. Search every retained orientation for each instance.
4. Build cached Minkowski/no-fit regions. Convex pairs use Core's linear convex NFP primitive;
   concave pairs use Clipper's integer Minkowski sum. Arc-heavy concave contact outlines use
   a coarser mesh with both approximation bounds added to clearance; fine material geometry
   still checks every candidate. Positive outer boundaries are filled conservatively.
   Axis-aligned rectangles have a four-vertex contact shortcut.
5. Maintain each orientation's available translation region incrementally as parts are added.
   Search its boundary vertices, exact-fit contacts and hole anchors. Reject points inside solid
   no-fit regions before expensive checks. Check surviving candidates against actual material
   regions with holes and spacing offsets. Zero-clearance contacts also pass the shared triangulated
   collision check, with tiny position adjustments when rounding makes an exact contact unsafe.
   Hole contacts use the same check in the final sheet coordinate frame, with bounded caching.
   Two directional objectives try bottom-up and left-to-right growth using the same contact algorithm.
6. Search stock plans with a beam of up to three states. Rank by observed delivery cost and
   remaining material, while preserving a state with high placed area. This avoids starving
   large-sheet plans in favor of cheap but inefficient small-sheet prefixes. A genuine material
   area lower bound prunes plans only once a complete cheaper plan exists. After 24 evaluated
   trials only one directional objective is used; after 64, beam width reduces to two.
   Work counts, not elapsed time or randomness, control search breadth.
7. Select a complete plan with lowest purchased area, breaking equal-cost ties by sheet count.
   If no complete plan is found, maximize fulfilled counts by priority, then minimize cost.
   Emit committed-sheet progress, contiguous per-part instance indices, inventory, fulfillment
   and the contract's job-level stop reason. Cancellation throws without returning a partial job.

The engine never invokes another engine, registry, job runner, whole-plate nester or filler.
All order, stock, orientation, placement, search and stopping decisions belong to Astra.
Core geometry and Clipper are primitives, not alternative nesters. A shared Core collision fix
corrects curved-hole validation; the placement algorithm remains entirely in Astra.

## Precision and safety

Analytic rotated bounds govern sheet containment. Material curves are conservatively flattened
at 0.001 job units. Positive configuration-space spacing includes 0.0003 extra units for non-rectangular
straight outlines; curved outlines reserve 0.003 extra units even at zero spacing, accounting for offset/chord error and the
benchmark validator's four-decimal grid. Axis-aligned rectangle contacts preserve exact requested
spacing. Actual material intersection checks backstop candidate construction. Both straight-edged
and curved holes are available for insertion. The shared collision routine now subtracts hole
triangles into disjoint fragments with consistent half-space clipping, resolving the reproduced
curved-hole false positive. See the benchmark report for regression results.

Concave contact outlines exceeding 64 vertices use a chord tolerance of the greater of 0.002
units or 0.2% of the smaller envelope dimension. The pair's two tolerances are added to the
NFP offset. This reduces Minkowski input size without coarsening the final material checks.

The broad phase is deliberately conservative. It can miss a valid close fit; output validation
is exercised separately through the benchmark's materialized geometry validator in tests.

## Structure

- `AstraNestingEngine.cs`: bounded stock-plan search, accounting, progress and result construction.
- `PreparedGeometry.cs`: snapshots, allowed orientations, symmetry reduction and material regions.
- `ContactGeometry.cs`: cached no-fit polygons and rectangle specialization.
- `ContactPlacer.cs`: incremental available regions, contact/inside-hole search and collision checks.
- `tests/`: xUnit tests plus a linked copy of the existing benchmark validator source.
- `benchmarks/`: standalone synthetic benchmark driver, reproducible repository-DXF manifests,
  baseline/current CSV results and comparison notes. It is not compiled into the plugin.

`OpenNest.Engine.Astra.csproj` references Core explicitly and inherits Engine/net8.0 settings from
`../Directory.Build.props`. Test and benchmark sources are excluded from the plugin assembly.

## Build, test and deploy

```bash
dotnet build Engines/OpenNest.Engine.Astra/OpenNest.Engine.Astra.csproj -c Release
dotnet test Engines/OpenNest.Engine.Astra/tests/OpenNest.Engine.Astra.Tests.csproj -c Release
dotnet build OpenNest.Benchmark/OpenNest.Benchmark.csproj -c Release
mkdir -p OpenNest.Benchmark/bin/Release/net8.0/Engines
cp Engines/OpenNest.Engine.Astra/bin/Release/net8.0/OpenNest.Engine.Astra.dll OpenNest.Benchmark/bin/Release/net8.0/Engines/
dotnet OpenNest.Benchmark/bin/Release/net8.0/OpenNest.Benchmark.dll Engines/OpenNest.Engine.Astra/benchmarks/dxf --engines AstraNestingEngine --parallel 1
```

The host supplies Core, Engine and their dependencies. Plugin discovery uses the CLR type name
`AstraNestingEngine`; no registry call exists in the plugin.
Rebuild the host with this checkout's `OpenNest.Core` as well: replacing only the plugin DLL
does not update the shared curved-hole collision fix.

## Limitations

This is bounded heuristic search, not a proof of minimum sheet cost or infeasibility. Early part
order is not backtracked within a sheet, already placed parts are not moved, and available
orientations are sampled/pruned. Hole search uses anchor positions, not a complete inner-fit
polygon solver. Small usable regions inside complex cutouts may be missed. The benchmark report
includes an isolated host-validator reproducer and its corrected outcomes.
Filling NFP interior voids can exclude unusual interlocking configurations.
Salvage-credit options and `PlacementStrategy` do not change Astra's objective; `MaxPlates` is
respected. Stock dimensions, all edge spacings, quadrants, priorities and rotation policies are
honored. No real `.nest` fixtures were available in this workspace.

Complex concave outlines, dense bounded sweeps, many part types or very large quantities can
be expensive. NFP and trial cache entry counts are bounded, but individual geometry can be large.
Cancellation is checked throughout search and between geometry operations; shared validation and
individual Clipper calls are not interruptible. The v2 search costs more CPU than the original
bounding-rectangle baseline. See `benchmarks/README.md` for measured tradeoffs.

## Current validation

Release build succeeded with .NET SDK 8.0.425 on Linux. All 27 xUnit cases passed, including
independent benchmark validation of materialized results, exact positive/zero clearance,
non-cardinal rotations, hole insertion, automatic diagonal-only stock fits, all quadrants,
curves, incremental geometry, determinism, inventory, cancellation and stock-plan regressions.
All 34 synthetic/generated benchmark cases and all four repository-DXF cases were valid and complete.
Existing nullable warnings originate from the benchmark validator linked into the test project.
