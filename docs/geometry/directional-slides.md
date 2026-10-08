# Directional slides and repeated pushes

## Behavior contract

For initially non-overlapping closed material boundaries, a directional slide stops at the first contact that blocks forward motion. Leaving an existing contact or sliding along a straight shared edge is legal. Skipping a contact must not skip the rest of that obstacle: a later hook or the opposite wall of a hole still stops the slide. Distances retain the existing `Tolerance.Epsilon` snapping and `double.MaxValue` no-hit convention; supplied vector directions are unit vectors.

`SpatialQuery.DirectionalDistance` overloads and CPU best-fit batches use the same event sources and contact resolver. Events carry both contact points in their initial world frames, rather than only a snapped distance. The classifier uses closed-loop material sectors, native-curve containment for hole depth, and the curvature of the supporting boundary at a tangential contact. A full-circle arc has no physical corner at its seam. Raw ray helpers remain first-touch primitives, not material-aware slide queries.

Open/incomplete chains and ambiguous contacts conservatively block. This is not an overlap-repair operation or a general replacement for layout validation. Caller-provided contact topology must represent the same boundaries and offsets as the query. Prepared geometry must not be mutated; prepare a classifier before sharing it between parallel queries. Edge-array queries still sort their arrays, and recover loop order from private copies before classifying contacts.

## Callers

- PlateView uses `SelectionManager.PushSelected` → `Compactor.Push`. The zero-spacing nudge-and-discard workaround is removed. Cutout contours on stationary obstacles are retained, and the plate entry's existing-overlap filter accounts for holes rather than relying solely on `Part.Intersects` (which compares outer perimeters).
- Linear fill inherits the shared native-entity query unchanged. Extents fill passes complete boundary loops instead of direction-filtered fragments.
- CPU best-fit batches prepare contact topology once and use all vertices plus curve/line interior and curve/curve tangency events. The old leading-half vertex filter cannot establish the next blocker after a skipped touch.
- GPU kernels retain nearest-hit reduction and return unsnapped contact witnesses. The shared CPU classifier accepts a blocking witness or replays the full query after a nonblocking witness, preserving tied/later blockers. Both batch APIs honor active buffer lengths and refresh mutated/reused segment arrays. The GPU distance adapter sends only exact cardinal directions to the axis-only slide interface; arbitrary directions and native curves use the shared CPU path.

- Shift-click while cloning parts settles the copied group toward the plate quadrant. If its starting bounds overlap any placed part's bounds, it skips the coarse bounding-box pass and uses geometry directly; otherwise it tries coarse horizontal/vertical and vertical/horizontal orders. Each candidate then alternates geometry pushes until movement is negligible (at most 20 iterations), and the group nearest the quadrant's work-area corner wins. The coarse pass remains useful for avoiding sawtooth/rung traps when the starting boxes do not overlap.

## Regression coverage
`SlideContactTests` exercises cardinal line, translated line, reused edge-array, arbitrary-vector, native-entity, and both CPU batch paths. Cases include winding reversal, nonzero origins, rotated hooks, holes, separating circles, positive-distance grazing followed by a blocker, full-circle arc seams, concave/straight junctions, thin rings, and circle/line interior contact.

`CompactorTests` covers the reported sequence (push left with spacing, then right/up/down), genuine zero-distance blocking, zero/nonzero-spacing later hooks, and inside-hole pushes through both direct and plate entry points. Physical spacing is measured from raw outlines rather than the inflated contours used by the solver.

Verification commands:

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter 'FullyQualifiedName~SlideContactTests|FullyQualifiedName~CurveContactDistanceTests|FullyQualifiedName~CompactorTests'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj
dotnet test OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj
dotnet test OpenNest.IO.Tests/OpenNest.IO.Tests.csproj
```

The isolated repair tree (excluding other sessions' fill-performance and spacing-expander work) passed 130 targeted cases and the full Debug suites: main 1758 passed / 21 skipped, engine 300 passed, IO 41 passed. The main Release suite passed 1728 / 21 skipped. Skips are not counted as passes. The 142 `GpuSlideContactTests` also pass in a net8 harness linking the production GPU class and test source, using ILGPU 1.5.1's CPU accelerator (not a mocked distance solver). The Windows desktop/test project cross-build passes in Release. Neither physical GPU execution nor Windows UI interaction was runtime-verified on Linux.

## Remaining hardening

This repair does not change saved best-fit cache versioning, add a clearance acceptance gate to every fill entry, repair existing overlapping layouts, or claim the earlier real-DXF/grid-validator discrepancies in [pair-spacing checks](pair-spacing.md) are resolved. That document's measured candidate counts describe its earlier tree; removing CPU projection filtering and adding interior curve/line contacts does not substitute for rerunning its corpus. General `Part.Intersects` hole semantics remain unchanged outside Compactor. Profile the new classification path before attempting optimizations; retain the first-blocking-contact regressions.
