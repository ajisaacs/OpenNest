# Fix Spacing

`Plate > Fix Spacing` clears the pairs the overlap check flags (parts sharing material, and
parts closer than the plate's `PartSpacing`, or than their kerf for common-line partners) with
the smallest moves it can find. It moves parts by translation only and never changes
`PartSpacing` or `EdgeSpacing`. Unlike [Expand Spacing](expand-spacing.md) it needs no
selection and does not open up the rest of the layout: parts nothing crowds stay where they
are. With parts selected, only flagged pairs that include a selected part are fixed. The
engine entry point is `SpacingFixer.Fix`.

## How a pair is opened

Pairs are fixed in turn, overlaps first, then the tightest gaps. Gaps are measured on the
same native contours the overlap check confirms with (`NativeContours`), flattened a little
coarser for speed; that understates gaps, so a push errs slightly long.

1. **Straight chains.** One part of the pair is pushed straight away from the other, in each
   of 16 directions, by just enough to reach the spacing plus the check's slack. Any part that
   push brings closer than its floor is pushed the same way by its own shortfall, and so on
   down the chain, so a fix can ripple through rows already locked in at the spacing. A chain
   fails when it would push the pair's other part, a common-line member, or a part past the
   work-area edge or into a clamp. The chain with the least total travel wins.
2. **Relaxation.** When no straight chain has room (a plate packed edge to edge), each crowded
   pair is pushed apart along the normal between its closest points, the shortfall shared
   between its two parts; a part held by the work-area edge or locked passes its share to the
   other. Passes repeat until every gap holds, so the moves can differ in direction (one part
   right and up, its partner down and left). Relaxation cannot separate parts that overlap or
   touch, since they have no normal; straight chains handle those. A move under 0.0001 that the
   layout does not need is undone.

A pair's **floor** is its required gap, or its gap before the fix when that is already
smaller (another flagged pair never gets worse), less 0.00001 of rounding allowance.

## Validation

The plan for every pair is checked once with the overlap check. The result is applied only
when it has fewer findings than before and nothing new: no new pair, edge, clamp or
unreadable finding. Otherwise the pairs are redone one at a time, each fix kept only when the
overlap check rates the plate better. A pair with no room is left as it was and counted in the
status bar ("Fixed 3 of 4 flagged pairs ... - 1 flagged pair left").

Cut-offs are not obstacles: they are regenerated around the moved parts. Irregular sheet
outlines are honored through the final overlap check, which reports parts outside them.

## Desktop

The command runs on a worker behind the same Cancel window as Expand Spacing, and refuses
while Auto Nest or a fill is still running on the nest. A canceled run moves nothing. It records
one undo step, "Fix Spacing", and does not save.
