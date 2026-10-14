# Visual material-overlap check

## Desktop use

Choose **View > Overlap Check > Check Active Plate** to check committed parts on
this plate. Shared material is shaded red/magenta without changing the nest,
selection, cutting paths, or export behavior. Cutoffs and temporary preview parts
are excluded. **Cancel Check** discards the running request. **Display > Off /
Areas / Markers / Both** changes visibility without rerunning analysis. Areas
is the default; rechecking preserves a visible mode, while checking from Off
shows Areas. Clamp overlaps are shown as part/clamp findings with the clamp ID
in hover details; clamps are not selectable parts and are not added to quantity
or machining. A clamp edit invalidates the diagnostic and schedules a fresh
check. Display preferences belong to the current document and are not saved.

Markers and Both show fixed-screen-size, DPI-scaled pair crosshairs with a
contrasting halo. Labels such as `1/3` identify the two plate sequence positions
at capture time, including gaps occupied by cutoffs. Hover near a marker to see
captured names, approximate shared area in in² or mm², and marker coordinates
in the same linear units. Units are captured with the request; hover does not
relabel an old report from live settings. Nearby/coincident markers show all
matching pairs in sequence order. If details exceed the view, the tooltip wraps
and pages them with a `Page x/y` hint: keep the pointer near the marker and use
plain **PageUp / PageDown** while PlateView has focus. Long individual details
continue across pages without truncation; an impossibly small viewport asks you
to enlarge it. These keys act only while a multipage diagnostic tooltip is
visible; wheel zoom, modified keys, selection clicks, and dragging are unchanged.
Small positive areas use significant-figure formatting rather than rounding to
zero. Selection and dragging remain ordinary plate operations, not overlay
interactions.

A persistent label distinguishes unchecked, checking, current, incomplete, stale,
canceled, and failed checks. Only a completed, current, fully checked report can
say **No material overlaps detected**. An incomplete check retains known overlaps
and states how many distinct parts could not be checked. Pair counts are not
fragment counts. This diagnostic checks shared material, not minimum spacing,
plate edges, or cutting-path crossings.

In a nest window the active plate is checked automatically. Any layout edit
(add, remove, reorder, move, rotate, fill) invalidates the full report and shows
**Overlaps: check pending…**, but highlights for unchanged pairs remain visible.
Only highlights involving changed ordered input slots are removed; once the
layout has been unchanged for 0.5 s and no
mouse button, modal dialog, or fill progress window is active, the check reruns.
Automatic results appear only in the canvas label, so the status bar keeps the
last command's message, and an automatic check keeps Display > Off rather than
switching to Areas. Rechecks are incremental: drawing material is prepared once
and reused, and pairs whose two parts have not moved reuse the previous result,
so only the moved parts' neighbors are clipped again. Canceling a check is
respected until the layout changes again, and a failed layout is not retried
until it changes. Check Active Plate remains available and always runs at once.

Plate changes reset the check. Pan, zoom, selection, and display changes do not
rerun geometry. Drawing-editor loading invalidates before loading (dropping cached
material), even if the dialog is later canceled. There is no check during export.

## Analysis API

`OpenNest.Diagnostics.PlateOverlapAnalyzer` in OpenNest.Core checks a group of placed
parts and returns the shared material for each overlapping pair as connected
islands. It clips with Clipper2. Existing `Part.Intersects`, `PartOverlapChecker`,
`Plate.HasOverlappingParts`, engine validators, and CLI entry points are unchanged
and still use the hand-written `Collision` kernel (its GPU port is on hold).

## Synchronous use

```csharp
using OpenNest.Diagnostics;

var report = PlateOverlapAnalyzer.Analyze(parts, cancellationToken);
var areas = report.Pairs.SelectMany(pair => pair.Regions).ToList();

foreach (var pair in report.Pairs)
{
    // IDs are zero-based positions in the original input list, including skipped cutoffs.
    Console.WriteLine($"{pair.PartAId} / {pair.PartBId}: {pair.Area}, marker {pair.Marker}");
    foreach (var region in pair.Regions)
    {
        // region.Vertices: closed, read-only, counterclockwise world-coordinate outline
        // region.Holes: closed, read-only, clockwise hole rings inside that outline
        // region.Area: positive net shared material area, in model units squared
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
// UI thread, while the plate and drawings are stable:
var snapshot = PlateOverlapAnalyzer.CapturePlate(plate, cancellationToken: cancellationToken);

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
Each candidate pair is intersected once with Clipper2: each operand is its outline
minus the union of its holes (outline positive, holes negative, positive fill rule).
Pairs are rebased near the origin and rounded to a 1e-8 model-unit grid for
clipping, then restored to world coordinates. A coordinate more than 1e9 units from
the pair's origin, or a nonfinite one, makes that pair incomplete rather than clear.
Area calculation uses translated-origin products to avoid cancellation far from the
origin. Snapshots can be reused and analyzed concurrently. Callers own freshness
checks and must not publish results after geometry changes or after a newer request
supersedes them.

Cancellation throws `OperationCanceledException`; it never returns a partial
all-clear. Checks occur between source/pose preparation, validation loops, and
candidate pairs, and before return. An individual conversion, polygonization,
or Clipper2 intersection is not internally interruptible.

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
  One self-contact is recognized: a seam overcut, where the last cut runs past the
  contour's own start along its first cut and a straight jog returns to the start
  (converted nests carry another CAM system's overcut this way). It encloses no area
  and is trimmed from the analyzer's owned copy, never from the drawing; see
  [cutting dependencies](../cutting-planner.md#cutting-dependencies).
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

For a full plate, `PlateOverlapAnalyzer.CapturePlate(plate)` also captures active
clamps and nominal manual cut-off definitions. `CutOffClampFindings` is a separate
list of zero-area line/rectangle contacts, not `Pairs` or material overlap. The
view marks these with an X and a cut-off number/clamp ID, including contacts whose
current regenerated program has been trimmed. Removed clamps produce no contact.
Zones are mapped from the sheet's lower-left frame into the parts' quadrant frame; a
malformed zone fails the check rather than being skipped. Editing definitions, sheet
size/quadrant or zones stales the result (including part/clamp findings, because a size or
quadrant change moves the zones); posting runs its own fresh check with per-attempt consent.

## Interpretation and limits

Regions are connected islands of shared material. Each has a counterclockwise
outline and clockwise `Holes`; an island can also sit inside a hole that both parts'
holes enclose together. Fill outlines and holes in one nonzero-winding path so holes
stay empty. Areas within one pair may be summed. Areas across
pairs are not a union: three coincident parts produce three overlapping pairs,
so summing all pair areas double-counts shared plate locations.

`pair.Centroid` is the true area-weighted center of every shared-material island
for that pair, after hole subtraction. It is not an average of crossings or
vertices. For a disconnected, holed or concave overlap it can lie outside the red
material (between two patches, in a hole, or inside a curve).

`pair.Marker` is where markers, hover hit testing and cutting-inspection rings go:
a point on the shared material, inside the pair's largest island. It is that
island's pole of inaccessibility (`PolyLabel`), searched to a tenth of the island's
area/perimeter with a budget of 4096 distance evaluations. If that bounded search
does not land inside (a long thin sliver), the marker is the midpoint of the widest
interior run of a horizontal scan line through the island, placed between vertex
heights. There is one marker per pair, not per island: a second patch has shading
but no crosshair.

`PolygonAreaMoments` evaluates signed moments about local origins. The analyzer
subtracts each island's hole moments from its outline's (`TrySubtract`), then
combines the islands with positive area weights (`TryCombine`), on rebased
coordinates before adding the world origin back. Invalid,
degenerate, or nonfinite moments produce an incomplete pair issue, not a marker
at zero. Curved-outline centroids inherit the polygonization approximation.

Full containment and coincident parts are detected without relying on crossing
points. Edge/corner contact with no positive shared material is not overlap.
There are no spacing offsets, plate-edge checks, cut-path crossing checks, automatic
repairs, export blocks, or machining-validity guarantees.

Arc/circle flattening uses a chord tolerance of `0.001` model units (also exposed
as `report.ChordTolerance`). Curved overlaps and topology are therefore polygonal
approximations. A pair is reported when its total shared area exceeds
`Tolerance.Epsilon` (`0.00001` square units). Contact and rounding dust below that
are not overlap. The engine's `Collision` kernel instead drops each convex fragment
at or below that area, so a sliver it splits into small fragments can read clear
there while this diagnostic reports it. Floating-point coordinates still have finite
resolution.

## Desktop lifecycle and rendering

`OverlapReportState` and `OverlapGeometryStamp` in Core hold the testable request
policy. The stamp compares ordered part identities, exact pose scalars, drawing
and program references, cutoff status, and plate identity. It is not a geometry
hash: any new editor that mutates a clean program in place must call
`PlateView.InvalidateOverlapCheck()` before loading/mutation. Current live clean
program editing goes through `EditNestForm.EditDrawingsInConverter_Click`;
metadata-only edits do not change material. In-place hole-program edits require
the same explicit invalidation.

`OverlapOverlayController` owns UI-thread captures, background analysis, request
generations, cancellation, and the GDI display cache. `EditNestForm` enables its
automatic recheck with `PlateView.SetOverlapAutoCheck(() => Nest.Units)`; other
PlateView hosts (fill previews, pattern tiles) stay manual. The debounce decision
is the Linux-testable `OverlapAutoCheckScheduler`: collection events and every
paint's stamp comparison restart a WinForms timer, and the tick starts a check only
if the stamp is unchanged since the last observation and no interaction is active.

The controller passes one `OverlapMaterialCache` to
`PlateOverlapAnalyzer.Capture(parts, cache)` and its last published report to
`Analyze(snapshot, previous)`. The cache keeps each clean `Program`'s converted
entities and prepared, validated material (weakly keyed by program reference; a
changed code count or rotation is detected). Preparing material dominates
first-check time for drawings with many holes. Incremental analysis reuses a pair
only when both parts have the same cached source, bit-identical pose, and the same
relative input order (clipping is operand-order sensitive), then renumbers it.
`InvalidateOverlapCheck()` clears the cache, baseline and every highlight, so
in-place program editors must keep calling it before loading.

Moving one part hides only highlights involving changed parts. Unchanged pairs
remain visible throughout the quiet period and background recheck, sharing their
existing immutable regions rather than recalculating them. `OverlapReportState`
keeps these `DisplayPairs` separate from its full `Report`: the full report is
unavailable and the label stays pending/out-of-date/checking until a fresh result
is published. Retained highlights are known overlaps, never an all-clear for the
edited layout. Each paint checks exact poses and references again, including
further edits while an earlier check is pending. Hover details remain disabled
until the full report is current. Collection edits conservatively discard pairs
whose ordered input slots changed; this display-only path does not renumber them.
Explicit geometry invalidation, plate switch, handle loss, cancellation and
failure clear all retained highlights.

Measured on 389 real PEP-converted plates (252 nests), a from-scratch check takes
median 0.8 ms, p99 97 ms and max 3.2 s. Nearly all of the slow cases are first-time
material preparation for a hole-heavy drawing. With material prepared, clipping every
candidate pair and placing markers takes median 0.05 ms, p99 34 ms and max 76 ms
(markers added about 0.4 ms per overlapping pair). The triangle-pair kernel
took up to 2.6 s on the same plates. On those plates Clipper2 and the previous kernel
found identical pair, clamp and issue sets. Areas differed by at most 0.0002 square
units, always smaller from the kernel, consistent with its dropping fragments below
its area threshold. With the previous kernel, an
incremental recheck after moving one part took median 0.1 ms, p99 20 ms and max
35 ms. A comparison against uncached full analysis over 2505 edits (nudge, drag onto
another part, move three, delete first, swap order) matched exactly. It checks freshness before
publication and painting. Handle destruction/disposal cancels work and releases
paths; old completions cannot replace a newer report. Snapshot conversion and
clipping never run in paint or mouse-move handlers.

PlateView draws the controller overlay after work-area/debug-remnant drawing and
before action paint subscribers and hover tooltips. Island outlines and reversed
holes go into one consistently wound path that is filled once, so holes stay empty
and triple coverage does not darken. World-to-graph conversion excludes pan,
because PlateView already applies origin translation. Paths are rebuilt for
displayed-pair/scale changes, not ordinary repaints or panning. The state label
saves/restores graphics state.
Marker hit tests use only cached report coordinates and DPI-scaled screen
radii. Hover clears on edits, mode/request/view changes, leave, and teardown.
Diagnostic details draw above action adorners and take precedence over the normal
part-name tooltip only while visible.

Next hardening: the first check of a plate with a hole-heavy drawing can still
take seconds, and cancellation cannot interrupt the interior of material
preparation or one Clipper2 intersection.

## Verification

`OpenNest.Tests/Diagnostics/PlateOverlapAnalyzerTests.cs` exercises analytical
rectangle regions/areas, containment and contact, both operands' holes, concave
and disconnected intersections, connected islands with holes (including an island
enclosed by both parts' holes), curves, baseline rotation, large translations,
deterministic pair ordering against an exhaustive rectangle oracle, invalid
inputs, snapshot isolation, read-only output, cutting-program independence, and
cancellation. Run:

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter 'FullyQualifiedName~PlateOverlapAnalyzerTests|FullyQualifiedName~OverlapReportStateTests|FullyQualifiedName~PolygonAreaMomentsTests|FullyQualifiedName~OverlapPairPresentationTests|FullyQualifiedName~OverlapHoverPagesTests|FullyQualifiedName~IncrementalOverlapAnalysisTests|FullyQualifiedName~OverlapAutoCheckSchedulerTests|FullyQualifiedName~OverlapMarkerTests|FullyQualifiedName~PolyLabel'
```

`IncrementalOverlapAnalysisTests` compares incremental rechecks with uncached full
analysis across moves, rotation, deletion, insertion, order swaps, renames and
coincident duplicates, and covers pair reuse, issue renumbering, cache clearing and
cancellation. `OverlapAutoCheckSchedulerTests` covers the quiet period, interaction
waits, and the no-retry rule for canceled or failed layouts.
`OverlapReportStateTests` verifies request supersession, exact pose/reference
freshness, per-pair display retention through repeated edits and pending checks,
hard invalidation, cancellation, and incomplete-versus-clear messaging.
`PolygonAreaMomentsTests` covers analytic
centers, unequal/disconnected fragments, hole subtraction, winding, closure, large
translations, and invalid/overflow cases. `OverlapPairPresentationTests` checks adaptive unit
formatting, sequence labels, coincident ordering, and zoom-independent DPI hit
radii. `OverlapHoverPagesTests` proves bounded continuation pages retain every
pair and long/Unicode name, with navigation bounds and explicit tiny-view failure.
`OverlapMarkerTests` covers the pole, the scan-line fallback after an exhausted search
budget, and the last-resort fallback; `PolyLabelDifferentialTests` proves the heap
queue returns bit-identical poles to the former sorted list, so label positions
elsewhere do not move.
`OpenNest.WinForms.Tests/PlateOverlapOverlayTests.cs` adds STA worker/publication,
menu/MDI, path-cache, uniform-fill pixel, control-lifetime, and automatic-recheck
(debounce, drag detection, busy wait, supersession, display/status) checks. Run those
on Windows:

```sh
dotnet test OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj
```

Linux can cross-build with `-p:EnableWindowsTargeting=true`, but that does not
execute Windows tests or verify appearance, DPI, or interaction. On Windows,
check partial overlap, containment, inside-hole placement, markers on concave,
holed and two-patch overlaps, pan/zoom and quadrant
alignment, changed-pair clearing and unchanged-pair retention during edits and
pending rechecks, full clearing on plate switches and converter cancellation,
the pending label and automatic recheck after dragging a part onto another,
crowded-marker PageUp/PageDown access to the last pair, and repeated
check/toggle/close cycles without GDI/disposed-control errors.
