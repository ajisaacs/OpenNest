# Automatic scrap cutoffs

Finish nesting, then choose **Nest > Automatic Scrap Cutoffs (All Plates)** to process the entire nest with one dialog. **Plate > Automatic Scrap Cutoffs** still processes only the active plate. The manual **Sheet Cut-Off** command remains available.

## Preview and apply

1. Finish or cancel any active nesting/fill operation first, and wait for it to finish stopping; closing its progress window alone does not finish the operation. Enter strip spacing along the sheet's length. The initial value is **35 in**, or **889 mm** for a metric nest. This is the distance between nominal cut lines, not the length of a cut.
2. Set **Minimum tail to keep**, initially **12 in** or **304.8 mm**. This measures the reusable tail's length along X, after separator clearance, not its area. A shorter tail does not get a new final separator; a tail at or above the minimum can be retained. Zero allows any positive tail.
3. Opening the modal dialog previews the initial proposal. After changing either setting, choose **Preview** to refresh it. Cuts run across the active sheet's actual width, advancing along X from the origin-side edge in the plate's quadrant. Review the used span, proposed separator, retained tail dimensions, and diagnostics; the sheet cannot be edited while the dialog is open.
4. Choose **Apply** (or **Apply to All Plates**) to recalculate against the current parts/settings and add ordinary cutoff definitions at the end of each plate's cutting sequence. Blocked or empty proposals add nothing. Existing parts and manual cutoff definitions are not moved or reordered. **Cancel**, Escape, and closing the dialog discard the detached preview.

For the Nest command, the on-sheet overlay and upper measurements show the active plate; the scrollable diagnostics list reports every plate's proposed cuts, occupied/used spans, retained tail and warnings. Each plate uses its own dimensions, quadrant and part spacing with the dialog's common spacing/minimum-tail settings and current cutoff clearance/direction settings. No plate navigation or per-plate confirmation is required. Empty or unchanged plates are left untouched. A blocking conflict or invalid plate prevents changes to **all** plates, with the plate number reported; resolve it before retrying. If application fails after changes begin, all touched plates' original cutoff programs and sequence are restored. An explicit incomplete-recovery warning requires manual review before saving or cutting.

An empty sheet is left untouched. Invalid input or out-of-sheet part geometry prevents automatic planning. Excessively small spacing is rejected rather than allocating an unbounded number of cuts. Ordinary floating-point roundoff between translated material and cached bounds is accepted without changing the part or its program; cutoff obstacle checks and fallback exclusions receive the same conservative numerical padding. Larger discrepancies (for example, an inconsistent arc center corrected during conversion) still prevent planning rather than risking a cut through material; repair the geometry before retrying.

## Preserving the unused tail

A proposed tail shorter than **Minimum tail to keep** is left attached to the scrap skeleton by omitting only the final separator. The nominal skeleton grid is not extended into that tail, and no retained tail is claimed. Existing cutoff definitions—including an end cutoff from an earlier run—are never removed by this setting.

Only real parts contribute to the occupied envelope; existing cutoff parts do not. When there is room, the final separator is beyond the furthest occupied extent by the larger of plate part spacing and cutoff part clearance, plus numerical tolerance. Repeated skeleton cuts stop before that separator. The larger margin affects only the separator: repeated cuts keep the existing manual-cutoff clearance behavior.

Cutoff trimming measures clearance from the part contour, excluding tagged lead-in and lead-out moves when building the closed outline. Leads must not turn a recessed outline into a convex hull and leave oversized gaps elsewhere. Genuinely open contours still use the conservative hull/bounds fallback. The conservative curve/offset approximation can leave a small extra gap (about 0.0003 in on a straight edge at the default 0.02-in clearance), not an extra quarter inch.

If no safe separator fits inside the sheet, there is no retained-tail claim and the nominal cuts may cover the full sheet length. A proposed tail is not reported as separated unless its generated program spans the full sheet width. Obstructed or minimum-length-filtered lines produce diagnostics; they are not proof of a completed partition.

For a 120-by-81-inch sheet whose parts extend through 80 inches, the nominal skeleton lines are at 35 and 70 inches, followed by a separator beyond 80 inches at the required margin. There is no line at 105 inches through the retained tail.

## Limits and operator review

**View > Draw Rapids** follows the complete cutting sequence: an incoming rapid ends at a cutoff's first pierce, gaps between its trimmed segments remain rapids, and the next part's rapid starts at the cutoff's final cutting endpoint—not its last pierce. This display does not add a return move or alter the cutoff program.

- Spacing is nominal. Clearance gaps and suppressed short segments can leave bridges between scrap regions. This command does **not** certify that every connected scrap piece is disconnected or fits a hopper.
- The active sheet's physical width is used, not a hard-coded 81 inches. Check the actual width against the hopper; a wider sheet is not automatically hopper-compatible.
- Internal-hole scrap is not processed. This command reuses the outside-skeleton behavior of manual cutoffs; it does not force cuts through parts or remove clearance to guarantee separation.
- Review the preview and posted NC using the normal machine-review process before production cutting.

## Part sequencing

After adding cutoffs, apply **Part Sequencing** to the current plate or all plates. Each cutoff is moved earlier as needed so it is cut before every part its nominal line passes through. Ordinary parts retain their relative order from the chosen sequencing route; a tail separator that crosses no parts keeps its normal route position rather than being forced to the front. Cutoff geometry, clearance, part programs, placements, and quantities are unchanged.

The dependency check uses the nominal horizontal/vertical line and its start/end limits, not the trimmed cutting segments, which intentionally skip the parts. It conservatively checks placed part bounds (including edge contacts), so a cutoff through a concave recess can also move ahead of that part. Definitions are matched by drawing identity, not their displayed names. A cutoff part with no matching definition is conservatively ordered before all ordinary parts.

This rule runs when applying automatic sequencing, after reversing the exit-first sequencer route into cutting order. Manual sequence edits are still manual; adding or moving cutoffs does not automatically reapply this rule. Reapply Part Sequencing after layout changes and review the resulting rapids before posting. Regeneration and saving/reloading retain the applied mixed sequence.

## Rerunning, editing, and saving

The command is one-shot; spacing and minimum-tail inputs start at their defaults each time the dialog opens. Generated lines are ordinary cutoffs and can subsequently be dragged, deleted, or sequenced with the existing tools. Moving parts regenerates their trimmed segments but does not reposition the cutoff grid or recalculate the retained tail automatically.

Rerunning unchanged suppresses equivalent existing full-span lines. A limited cutoff at a proposed line is a conflict requiring manual review, not permission to replace it or add overlapping NC moves. Existing definitions are never silently deleted or expanded.

Saving `.nest` retains ordinary cutoff positions, axes, limits, and sequence. Loading regenerates cutoff programs using the reader's default cutoff settings; exact toolpaths made with nondefault cutoff settings are not guaranteed to round-trip. Review regenerated paths before posting.

## Implementation boundary

`AutomaticCutOffPlanner` in Core builds detached definitions and diagnostics without changing the input plate. `AutomaticCutOffOptions.MinimumTailLength` is a finite, nonnegative distance in model units; its programmatic default of zero preserves the previous behavior, while the desktop explicitly supplies its unit-aware 12-in / 304.8-mm default. `AutomaticCutOffBatch.Create` plans an ordered plate list, and `Apply` replans the complete list before mutating any plate, refusing a blocked batch and recovering touched cutoff state on exceptions. Callers must prevent concurrent edits throughout planning/apply. The desktop dialog renders the active plate's detached preview through `PlateView.SetActiveParts`; both menu commands apply through the shared batch service, which adds definitions to `Plate.CutOffs` and uses `Plate.RegenerateCutOffs`. It must not accept preview parts directly, because doing so would bypass cutoff persistence and regeneration. There are no new nesting, post-processing, or `.nest` format rules.
