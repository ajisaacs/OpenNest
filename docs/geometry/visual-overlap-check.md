# Material-overlap polygon diagnostics

`OpenNest.Diagnostics.PlateOverlapAnalyzer` in OpenNest.Core checks a group of placed
parts and returns the shared polygon areas for each overlapping pair. This is the
cross-platform analysis foundation for a future PlateView overlay; it does not add
a menu command, painting, centroids, or freshness management yet. Existing
`Part.Intersects`, `PartOverlapChecker`, `Plate.HasOverlappingParts`, engine
validators, and CLI entry points are unchanged. A separate shared-triangulator fix
uses translation-stable winding, correcting missed clockwise outlines/holes far
from the origin without changing contact or fragment-area tolerance policies.

## Synchronous use

```csharp
using OpenNest.Diagnostics;

var report = PlateOverlapAnalyzer.Analyze(parts, cancellationToken);
var areas = report.Pairs.SelectMany(pair => pair.Regions).ToList();

foreach (var pair in report.Pairs)
{
    // IDs are zero-based positions in the original input list, including skipped cutoffs.
    Console.WriteLine($"{pair.PartAId} / {pair.PartBId}: {pair.Area}");
    foreach (var region in pair.Regions)
    {
        // region.Vertices: closed, read-only world-coordinate polygon
        // region.Area: positive shared material area, in model units squared
    }
}

// An empty list alone does not mean the whole group was checked successfully.
if (!report.IsComplete)
    foreach (var issue in report.Issues)
        Console.WriteLine($"Uncheckable input/pair {issue.PartAId} / {issue.PartBId}: {issue.Message}");
```

`Pairs` is ordered by `(PartAId, PartBId)`, with `PartAId < PartBId`. Drawing names
are captured as labels, not used as identity. Repeated instances and different
drawings with the same name remain distinct. Callers should pass each physical
instance once; duplicate input entries are distinct positions in the group.
`pair.Bounds` returns a fresh world-coordinate bounding box; collections and
vertices cannot mutate the snapshot, report, or live geometry.

## Capture once, analyze off-thread

```csharp
// UI thread, while parts/drawings are stable:
var snapshot = PlateOverlapAnalyzer.Capture(parts, cancellationToken);

// Worker thread, no access to live Part/Drawing/Program objects:
var report = await Task.Run(
    () => PlateOverlapAnalyzer.Analyze(snapshot, cancellationToken),
    cancellationToken);
```

Capture converts each distinct clean source program by reference identity once
into owned entities, including expanded shared hole-subprogram calls, and copies
input IDs, names, locations, and baseline-adjusted rotations. This is intentionally
not a `Program.Clone` dependency: conversion itself creates fresh geometry without
mutating the source or re-aligning shared subprograms. Capture has synchronous
conversion cost; callers must not mutate inputs while capture runs.

Analysis clones captured entities before chaining, prepares polygons once per
source, then prepares each pose. An X-sorted bounds sweep prunes separated pairs.
It invokes the existing hole-aware `Collision.Check` once per candidate pair,
without an earlier boolean collision pass. Pairs are rebased near the origin for
clipping/triangulation and restored to world coordinates; area calculation uses
translated-origin products to avoid cancellation far from the origin. Snapshots
can be reused and analyzed concurrently. Callers own freshness checks and must
not publish results after geometry changes or after a newer request supersedes them.

Cancellation throws `OperationCanceledException`; it never returns a partial
all-clear. Checks occur between source/pose preparation, validation loops, and
candidate pairs, and before return. An individual conversion, polygonization,
triangulation, or `Collision.Check` call is not internally interruptible.

## Material contract

- Material comes from `Part.BaseDrawing.Program`, transformed by
  `part.Rotation - drawing.Program.Rotation`, then `part.Location`. Applied or
  restored lead-ins, lead-outs, and tabs in `Part.Program` do not redefine material.
- Cutoff parts are skipped. Scribe, rapid, lead-in, and lead-out layers in the clean
  source do not define material; ordinary cut/default/display contours do.
- The caller chooses the group. Preview parts are not intrinsically distinguishable
  from committed parts here; a PlateView caller must supply committed parts only.
- Valid material has one simple closed outer contour and strictly internal,
  mutually disjoint holes. Open, empty, degenerate, self-intersecting, nonfinite,
  disconnected-outer, touching-hole, intersecting-hole, or nested-island geometry
  produces an issue. Native contour intersections are checked before polygonal
  containment, so sampling cannot hide a circular-hole crossing or tangency.
  A gap above `Tolerance.Epsilon` is not silently welded closed.
  Open cut marks are conservatively uncheckable; mark them as Scribe instead.
- Some valid curved geometry, such as sub-chord-width thin rings whose sampled
  contours cross, is uncheckable at this fixed tolerance and returns incomplete
  rather than clear. No automatic healing or adaptive refinement is performed.
  Poses that collapse edges or materially change area through floating-point
  rounding are also incomplete, even when all coordinates remain finite.
- Expected geometry failures produce issues with original input indices and
  preserve overlaps found among other valid parts. Unexpected failures propagate.
  A report with any issue has `IsComplete == false`, even if `Pairs` is empty.
- No part, drawing, quantity, pose, cutting state, or selection is modified.

## Interpretation and limits

Regions are the kernel's convex, hole-subtracted fragments, not merged connected
islands. Fill the fragments for a visual overlay; do not outline triangulation
seams as physical boundaries. Areas within one pair may be summed. Areas across
pairs are not a union: three coincident parts produce three overlapping pairs,
so summing all pair areas double-counts shared plate locations.

Full containment and coincident parts are detected without relying on crossing
points. Edge/corner contact with no positive shared material is not overlap.
There are no spacing offsets, plate-edge checks, cut-path crossing checks, automatic
repairs, export blocks, or machining-validity guarantees.

Arc/circle flattening uses a chord tolerance of `0.001` model units (also exposed
as `report.ChordTolerance`). Curved overlaps and topology are therefore polygonal
approximations. The unchanged collision kernel applies dimensional bounds and
fragment-area thresholds using `Tolerance.Epsilon` (`0.00001`); sufficiently small
slivers are below its reporting policy. Floating-point coordinates still have
finite resolution. Contact and fragment thresholds are unchanged; only the shared
triangulator's winding arithmetic was stabilized in the prerequisite fix.

Next integration/hardening: add PlateView request generations and stale-result
invalidation before rendering; measure real-plate capture/analysis cost and
cancellation latency before adding cached triangulations or background capture;
add area-weighted centroids separately if the UI needs them.

## Verification

`OpenNest.Tests/Diagnostics/PlateOverlapAnalyzerTests.cs` exercises analytical
rectangle regions/areas, containment and contact, both operands' holes, concave
and disconnected intersections, curves, baseline rotation, large translations,
deterministic pair ordering against an exhaustive rectangle oracle, invalid
inputs, snapshot isolation, read-only output, cutting-program independence, and
cancellation. Run:

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter FullyQualifiedName~PlateOverlapAnalyzerTests
```
