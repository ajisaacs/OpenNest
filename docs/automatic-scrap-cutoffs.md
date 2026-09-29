# Automatic scrap cutoffs

Finish nesting the active plate, then choose **Plate > Automatic Scrap Cutoffs**. The manual **Sheet Cut-Off** command remains available.

## Preview and apply

1. Finish or cancel any active nesting/fill operation first, and wait for it to finish stopping; closing its progress window alone does not finish the operation. Enter strip spacing along the sheet's length. The initial value is **35 in**, or **889 mm** for a metric nest. This is the distance between nominal cut lines, not the length of a cut.
2. Opening the modal dialog previews the initial proposal. After changing spacing, choose **Preview** to refresh it. Cuts run across the active sheet's actual width, advancing along X from the origin-side edge in the plate's quadrant. Review the used span, proposed separator, retained tail dimensions, and diagnostics; the sheet cannot be edited while the dialog is open.
3. Choose **Apply** to recalculate against the current parts/settings and add ordinary cutoff definitions at the end of the cutting sequence. Blocked or empty proposals add nothing. Existing parts and manual cutoff definitions are not moved or reordered. **Cancel**, Escape, and closing the dialog discard the detached preview.

An empty sheet is left untouched. Invalid input or out-of-sheet part geometry prevents automatic planning. Excessively small spacing is rejected rather than allocating an unbounded number of cuts. Ordinary floating-point roundoff between translated material and cached bounds is accepted without changing the part or its program; cutoff obstacle checks and fallback exclusions receive the same conservative numerical padding. Larger discrepancies (for example, an inconsistent arc center corrected during conversion) still prevent planning rather than risking a cut through material; repair the geometry before retrying.

## Preserving the unused tail

Only real parts contribute to the occupied envelope; existing cutoff parts do not. When there is room, the final separator is beyond the furthest occupied extent by the larger of plate part spacing and cutoff part clearance, plus numerical tolerance. Repeated skeleton cuts stop before that separator. The larger margin affects only the separator: repeated cuts keep the existing manual-cutoff clearance behavior.

Cutoff trimming measures clearance from the part contour, excluding tagged lead-in and lead-out moves when building the closed outline. Leads must not turn a recessed outline into a convex hull and leave oversized gaps elsewhere. Genuinely open contours still use the conservative hull/bounds fallback. The conservative curve/offset approximation can leave a small extra gap (about 0.0003 in on a straight edge at the default 0.02-in clearance), not an extra quarter inch.

If no safe separator fits inside the sheet, there is no retained-tail claim and the nominal cuts may cover the full sheet length. A proposed tail is not reported as separated unless its generated program spans the full sheet width. Obstructed or minimum-length-filtered lines produce diagnostics; they are not proof of a completed partition.

For a 120-by-81-inch sheet whose parts extend through 80 inches, the nominal skeleton lines are at 35 and 70 inches, followed by a separator beyond 80 inches at the required margin. There is no line at 105 inches through the retained tail.

## Limits and operator review

- Spacing is nominal. Clearance gaps and suppressed short segments can leave bridges between scrap regions. This command does **not** certify that every connected scrap piece is disconnected or fits a hopper.
- The active sheet's physical width is used, not a hard-coded 81 inches. Check the actual width against the hopper; a wider sheet is not automatically hopper-compatible.
- Internal-hole scrap is not processed. This command reuses the outside-skeleton behavior of manual cutoffs; it does not force cuts through parts or remove clearance to guarantee separation.
- Review the preview and posted NC using the normal machine-review process before production cutting.

## Rerunning, editing, and saving

The command is one-shot. Generated lines are ordinary cutoffs and can subsequently be dragged, deleted, or sequenced with the existing tools. Moving parts regenerates their trimmed segments but does not reposition the cutoff grid or recalculate the retained tail automatically.

Rerunning unchanged suppresses equivalent existing full-span lines. A limited cutoff at a proposed line is a conflict requiring manual review, not permission to replace it or add overlapping NC moves. Existing definitions are never silently deleted or expanded.

Saving `.nest` retains ordinary cutoff positions, axes, limits, and sequence. Loading regenerates cutoff programs using the reader's default cutoff settings; exact toolpaths made with nondefault cutoff settings are not guaranteed to round-trip. Review regenerated paths before posting.

## Implementation boundary

`AutomaticCutOffPlanner` in Core builds detached definitions and diagnostics without changing the input plate. The desktop dialog renders the preview through `PlateView.SetActiveParts`; Apply adds definitions to `Plate.CutOffs` and uses `Plate.RegenerateCutOffs`. It must not accept preview parts directly, because doing so would bypass cutoff persistence and regeneration. There are no new nesting, post-processing, or `.nest` format rules.
