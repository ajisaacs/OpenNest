# Verify a nest before posting

## Docked desktop validation

Choose **Nest > Validate Nest...** to inspect all plates without running the cut
planner or selecting a post processor. It opens the editor's read-only side panel
in **Validate Nest (live programs)** mode, distinct from the panel's unapplied
**Plan Cutting** proposal. The validation panel checks overlapping material,
missing lead-ins, rapid crossings in the existing placed programs and cutting
order, parts closer than the plate's part spacing, and parts off the plate or inside
its edge spacing. Findings identify plate and part
sequence numbers in a read-only grid;
incomplete or failed checks are never reported as clear. Select a row to switch
the main plate view to that plate and focus the finding. A colored headline gives
the warning count by kind (or says the check is incomplete or clear), a single line
explains the selected marker, and the grid retains every result. **Plan Cutting**
marks its overlap and route findings with the same overlay.
While a finding is selected, its named parts retain their normal colors; other
parts use muted gray fills and visible gray outlines, lead-ins, and marks. Selecting
another finding updates the emphasis. Clearing the finding or closing validation
restores normal rendering without changing drawing colors or selection.

Overlaps use the same magenta material shading as **View > Overlap Check**. A
DPI-scaled ring expands and fades continuously while the finding is selected;
both its color and contrasting halo fade as it grows. For overlaps the ring marks a point inside the largest
shared patch; for missing lead-ins it marks the contour start; for part spacing it marks
the middle of the closest gap; for edge spacing it marks the gap between the part and
the edge; for a part off the plate it marks where it leaves the plate. Rapid warnings
pulse the offending orange dashed segment continuously and mark its contacts with
the completed contour using fixed-screen-size magenta crosshairs with white halos.
Contacts use native lines/arcs and the rapid check's existing numerical tolerances;
shared vertices and tangencies get one marker, and travel along an edge marks the
contact span endpoints. Start-only departure is excluded. Travel entirely inside
a contour has no boundary contact markers. Incomplete findings use the part center when available;
findings without a usable location do not invent a marker. Closing the inspector
clears the highlight and restores the original plate, zoom, and overlap display mode.

Validation does not change placements, programs, lead-ins, or cutting order, save
the nest, or write CNC output. It includes material overlaps with active clamps and
cut-off or rapid crossings of their footprints, but does not check minimum spacing
to clamps.
It does not verify a post processor's final output. Posting still runs its own
fresh verification and consent flow.

Finish or cancel active nesting/fill operations before validating. The panel is
exclusive with editing and other side-panel actions while it owns the nest. The
main plate view remains available for wheel zoom, middle-button pan, and **F**
or middle-button double-click to fit. The plate header's zoom buttons and the
**Ctrl+Plus**/**Ctrl+Minus** shortcuts also stay live; its plate navigation
buttons stay locked. Panel X, Cancel or Escape requests
cancellation and waits for the worker to stop before restoring editing controls.
Switching to Plan mode also waits for any validation worker; it does not promote
a live validation report into a plan check. Closing the editor or main window
joins the worker before continuing the close. Posting while inspection is open
is refused; close the panel and use the fresh posting review. The former floating
validator window is retired; non-editor checks remain available through the
shared `PostVerificationAnalyzer` without an editor UI dependency.

## Posting workflow

The desktop's **Nest > Post > (post processor)** workflow checks every plate after
post settings are accepted and before asking where to save CNC output. Before any of it,
a nest whose plates carry MaterialLedger item codes has them
[re-checked](automatic-nesting.md#stock-from-materialledger) with the same per-attempt consent. The review
shows the first three check categories, even when there are no findings:

- **Overlapping parts and clamps:** shared material between placed parts, including containment,
  and physical material overlapping an active clamp. Holes are subtracted; placing a part inside
  a sufficiently large cutout is not itself an overlap. The same review also warns if a nominal
  manual cut-off line crosses an active clamp, even when its regenerated cut is currently trimmed;
  a line contact has no material-overlap area. Removed clamps on that sheet are not checked.
  The placed cut-off program is checked too, so a definition edited without regenerating its
  cut still warns where the posted cut would cross a clamp (one warning per cut-off and clamp).
  Clamp zones are compared in the plate's quadrant frame. A missing or malformed clamp zone
  makes the clamp checks incomplete (a warning), never a clear result.
  This uses clean drawing geometry, not added lead-in/out paths.
- **Missing lead-ins:** cutting contours without an actual, nonzero lead-in in the
  placed program. Applying a lead-in to only one contour does not clear the other
  contours. Scribe-only work and scrap cutoff lines do not require lead-ins here.
  The cutting planner supports direct contour piercing without lead-ins; that does
  not suppress this pre-post review warning or grant posting consent.
- **Rapid crossings:** direct XY moves crossing closed, already-cut, untabbed
  contours in cutting order, or an active clamp's physical footprint. This includes
  earlier holes within the same part and previously cut parts. A future cut is not
  an obstacle yet. Actual uncut gaps are respected; enabling tabs in settings alone
  is not evidence that a gap exists. A cut that runs on past its own start along its
  first cut (a converted program's seam overcut, matched within the 1e-6 seam
  tolerance) is closed at its seam; the rapid leaving its overcut end ignores contact
  within that tolerance of the departure. Clamp-rapid warnings are conservative: head
  height/retracts are not modeled, so inspect the emitted route and machine setup.
  A clamp whose footprint covers the program start point warns on every posting
  attempt by design; there is no exemption for the first rapid and no persisted
  bypass. A rapid Plan Cutting marked to raise the head (see
  [the safest-route fallback](cutting-planner.md#safest-route-fallback-when-the-search-runs-out))
  passes over cut contours with the head up, so its contour crossings are not warnings; its
  clamp crossings still are. The raised rapid out of a cutout's slug after its lead-out is
  accepted the same way; a head-down one still warns that it crosses the cutout.
- **Fixed leads:** Plan Cutting accepts fixed emissions as given and only warns (see
  [cutting planner](cutting-planner.md#fixed-emissions-warn)), so posting checks them
  again: every lead-in and lead-out of a locked program, and of each contour a part's
  active drawing pins fix, against the clean material of every part on the plate (its own
  included) and against active clamps. A pinned contour counts as applied only where the
  placed program cuts it exactly as the pin's own emission, motion for motion, with the
  part's cutting settings. A part with active drawing pins that its program does not apply
  (never planned, hand-edited, locked lead-ins overriding the pins, or a pin that no longer
  matches the geometry) warns "Drawing pins not applied"; pins switched off for that part
  are not reported. A part whose clean material cannot be captured makes the check
  incomplete for leads near it. This line appears in the summary only when there is such
  a finding.
- **Part support:** on a plate with a support bed, a part set for support tabs that the bed
  cannot hold level and whose outside cut leaves no tab that keeps it level; the marker is its
  center of gravity. See [support tabs](support-tabs.md#pre-post-review).
- **Part spacing:** two parts closer than the plate's part spacing without sharing material
  (overlapping pairs are reported only as overlaps). Candidate pairs come from each part's
  clean nesting material (leads excluded) tested exactly as Auto Nest's layout check tests
  it, with the shared `SpacingOutline` and `SpacingSlack`, so a layout Auto Nest accepted
  stays clear. That outline circumscribes arcs by more than the slack, so each candidate is
  confirmed on its native contours flattened 20 times finer than the slack (Core's
  `NativeContours`). The review warns only when that gap falls short of the spacing by more
  than `SpacingSlack`, which lets interactive fills that place arcs exactly at the spacing
  pass. Auto Nest's layout check and the whole-job runner's candidate validation confirm the
  same way, so all three agree. Zero spacing allows contact.
  Two members of one [common-line group](common-line-cutting.md) are held to the group's kerf
  instead (they are placed one kerf apart on purpose); the finding then names that kerf.
  A part inside another's cutout is held to the spacing from the hole edge. Cut-offs and
  clamps are not spacing-checked; the plate edge has its own check below. The finding gives the measured gap; this
  line appears in the summary only when there is such a finding.
- **Off the plate:** a part whose placed program cuts, leads or scribes past the sheet
  (native curve bounds, beyond `SpacingSlack`), or, on an [outlined sheet](irregular-stock.md),
  whose clean material lies outside the outline or over one of its obstacles. The finding
  gives how far it runs past which edge. This line appears in the summary only when there
  is such a finding.
- **Edge spacing:** a part on the sheet whose clean nesting material (leads excluded) lies
  outside the plate's work area, so closer to an edge than that side's edge spacing, or, on
  an outlined sheet, closer than the largest edge spacing to the outline or an obstacle.
  This is the bound Auto Nest's layout check applies, so a layout it accepted stays clear;
  shortfalls up to `SpacingSlack` pass. Leads may run into the edge spacing. The finding
  gives the gap and the side's spacing. Zero edge spacing allows contact with the edge.
  Cut-off parts are exempt from both checks. This line appears in the summary only when
  there is such a finding.
- **Nested parts:** a part whose perimeter lies strictly inside a cutout of another part (its
  host), proven on clean material exactly as Plan Cutting proves its inner-before-host
  prerequisite. **Nested part order** warns when the nested part comes after its host in the
  placed order, because cutting the host's cutout releases the slug it is cut from.
  **Nested part leads** warns when an actual lead-in or lead-out of the host (typically its
  enclosing hole's lead, including a hole cut through a subprogram call at its placed pose)
  contacts or enters the nested part's material, or a lead of the nested part enters the host's
  material. Leads already reported as fixed leads are not reported again. A pair whose
  containment cannot be established (touching or crossing boundaries, uncapturable material)
  is incomplete, never clear. Each line appears in the summary only when there is such a
  finding. Auto Nest commits cutout inserts before their frame, so an unedited plate has no
  order finding; leads keep their own checks. Plan Cutting's Ready result for the plate cuts
  nested parts first with leads that clear them; this review checks the live programs again
  before posting.
- **Common lines:** a [common-line group](common-line-cutting.md#checks) member that no longer
  shares an edge one kerf from another member, a group placed for a different kerf than the
  plate's cutting parameters now use, or a member's lead or shared cut that touches or enters a
  partner's material. On a plate that cuts its common lines once, also a span no member cuts once
  on its centerline (an unplanned plate would cut it twice), a span cut more than once, or one cut
  after the partner whose remainder ends in it; on a plate set to cut them twice, a program that
  still cuts one once. The rapid check treats a member's remainder pieces as cutting the part free
  when its program ends, and never treats a shared cut as an obstacle. This line appears in the
  summary only when there is such a finding. Posting a single-pass program through a post that
  cannot write it is refused before verification runs (see
  [common-line cutting](common-line-cutting.md#posting)).
- **Pierce zones:** with a `PierceDiameter` in the part's (or else the plate's) cutting
  parameters, every lead-in pierce of every placed program must keep half of it from all part
  material; see [pierce zones](geometry/pierce-zone.md). This line appears in the summary only
  when there is such a finding.

Findings identify the plate and part's position in the cutting sequence. Read the
scrollable report, cancel to fix the nest, then post again to rerun verification.
Uncheckable geometry is reported as incomplete, not passed. The overlap check reads
clean drawing programs made of the built-in instructions only; a missing instruction
list, any other instruction type, or a hole stored as an absolute-mode subprogram
(OpenNest writes them incremental) makes that part's check incomplete.

Open cuts, such as a cutout cut in several pieces around tabs (with or without leads, as
PEP programs often are), pass only when they provably release nothing. Within the part,
each open piece and its leads may touch another cut only at their own ends, and together
they may form no loop, counting each closed contour with its leads as one point. A lead at
an open end must also stay out of the gap beyond it: a lead-out never reaches ahead of the
end it leaves, and a lead-in never behind the start it enters. Anything else (pieces that
meet exactly, a lead that bridges or curls into a tab, cuts from edge to edge) is reported
as incomplete, needing manual review. Kerf, tab strength and other parts' cuts are not modeled.
Consecutive cuts of one piece meet at their shared vertex exactly, however their arcs were
rounded (see [cutting dependencies](cutting-planner.md#cutting-dependencies)): the proof
never mistakes a fillet's joint for a loop. Plan
Cutting runs the same proof, so its own [tabbed cutouts](tabbed-cutouts.md) (pieces without
lead-outs) pass, and so do its tabbed perimeters followed by a lead-out that stays out of the
tab. A program that still has a tabbed cutout's `Tab` motions (never planned) is
incomplete: a post may cut through them.

## Explicit override

With any warning, **Post Anyway** is disabled until the user selects:

> I understand the listed risks, including possible head crashes and machine or
> material damage. I choose to bypass these warnings and continue posting this nest.

Consent applies only to that posting attempt. Unchecking disables posting again;
opening another review always starts unchecked. Cancel, Escape, and the window's
close button do not write CNC files. Cancel is the default button. A clear report
still requires **Continue Posting**. An unexpected verification failure blocks
posting rather than allowing an override of an absent report.

The editor is modal during verification and posting dialogs. Posting is refused
while auto-nesting, interactive fill, or a busy plate action can still change the
nest. Cancellation keeps the modal dialog open until its worker has stopped.
No automatic repair, resequencing, lead-in insertion, or tab insertion is performed.

## Console

`--post` prints the same report. Warnings prevent CNC output (including overwriting
an existing file) and return exit code 1. After reviewing them, explicitly pass
`--acknowledge-post-risks` to accept the risks for that invocation. The switch does
not skip verification and is not persisted. When `--post` is requested, verification
must pass or be acknowledged before either nest saving or CNC writing. Nest-save
and CNC destinations must differ; collisions are refused even with acknowledgment.
Without `--post`, ordinary nest saving is unchanged; `--no-save` skips that save.

The raw `IPostProcessor` plugin interface remains a low-level writer. Third-party
callers must apply their own review/consent flow using `PostVerificationAnalyzer`;
the gate is installed at OpenNest's desktop and console posting entry points.

## Selected post and cut order

Cincinnati CI Fiber preserves the placed part/contour order and pierce positions
used by this check. Cincinnati CL keeps placed part order, including cut-offs, but
writes each part's (and hole's) etch features before its cut features, adds a rapid
ahead of a contour that opens without one, drops rapids with nothing to cut, and with
**Use part sub-programs** rapids to and from each part's corner. It declares the
placed order for a nest only when none of that changes a single motion with the
settings being posted: part sub-programs and smart rapids off, and every part's and
hole's motions, compared exactly, unchanged by its writer. Plan Cutting's programs
cut a part's scribes first, so a planned nest qualifies. Otherwise the warning names
the first part it changes, and the CNC output order is left as it was.
Gravograph builds and optimizes separate tool passes. It, a CL nest that does not
qualify, and unknown plugins show an additional **incomplete final rapid sequence**
warning requiring acknowledgment even if the nest-level checks have no findings.

## Limits and maintainer contract

These checks are warnings, not a machine safety certification. They inspect the
nest's placed programs and direct XY travel, not a simulation of final NC output.
A post processor or controller may change routing, cutting order, lead-ins,
retract height, or parking moves. They check planar clamp footprints but not clamp
height, actual head clearance, tipping, slats or tab strength. Review the generated
CNC program and machine setup before cutting, even when this report has no warnings.

`OpenNest.Core/Diagnostics/PostVerificationAnalyzer` owns the cross-platform logic;
its report exposes findings, display text, and `CanPost(risksAcknowledged)`. Use
`AnalyzeForPost` at posting entry points so unknown post ordering is not silently
certified. `IPostVerificationSupport.PreservesPlacedProgramOrder` is an opt-in
contract, not a machine-safety claim; only declare it after verifying the writer's
part order, contour order, and pierce positions. A post whose order depends on the
nest or its settings overrides `PlacedProgramOrderChange(nest)` instead, returning
null only when the posted motion sequence provably equals the placed one and
otherwise what it changes; the analyzer shows that reason in the warning. The WinForms dialog renders the
report and enforces per-attempt consent. New callers
must keep inputs stable until analysis completes, honor cancellation, and never
treat incomplete analysis as a clear result. Keep analysis out of paint handlers.
The overlap diagnostic's documented numeric/tessellation limits also apply.

Future hardening should inspect a shared post-specific emitted motion stream,
including controller retracts and final parking, rather than pretending direct XY
geometry proves physical head clearance. The [cutting planner](cutting-planner.md)
reuses these motion/completion checks and provides the desktop preview and Apply
workflow; applying a plan does not replace fresh posting review.

## Verification on Windows

The core/console tests run cross-platform. Dialog tests and actual desktop behavior
require Windows; Linux cross-compilation is not runtime verification.

1. Run `dotnet test OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj`.
2. Post a nest containing two overlapping parts and a part without lead-ins.
   Verify the report lists the plates/parts, shows all check categories, and disables
   **Post Anyway** until consent is checked. Uncheck it to verify disabling again.
3. Cancel with the button, Escape, and X on separate attempts; none should open the
   output save dialog. Reopen the review and confirm consent is reset.
4. Use a sequence that cuts a closed untabbed part, moves to its far side, then
   rapids back across it. Verify a crossing warning. Change the sequence so the
   perimeter has not yet been cut, then assign a real tab gap and recheck the
   appropriate control cases. Include a hole crossed later in the same part.
5. Acknowledge the warnings and post through both a single-file and multi-file post.
   Confirm output and normal overwrite prompts. Post a clear, leaded nest with Cincinnati CI Fiber and
   verify the report still appears without requiring a risk override.
6. During a large verification, cancel; the editor must stay disabled until the
   worker exits, with no output. A fill still finishing after its progress dialog
   closes must prevent posting until it has completed.

For failures, record the menu action, nest fixture, selected post, visible report,
and exact exception text.
