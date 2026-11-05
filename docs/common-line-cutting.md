# Common-line cutting

Two parts placed with straight edges facing each other, **exactly one kerf apart**, can share
one cut: programs are nominal geometry and the controller offsets each contour by half the kerf
(`G41`/`G42`), so both parts' facing edges are cut on the same centerline. Placing parts this way
saves the part spacing along every shared edge, which matters on costly plate.

## What ships today: kerf-gap groups, cut twice

`Plate.CommonLines` holds `CommonLineGroup`s: the member parts (by reference) and the kerf they
were placed at. Each member still cuts its whole perimeter, so every shared edge is cut twice: the
second pass runs along the kerf the first one opened. That needs no planner or post change. It
costs one extra pass per shared edge, and a part freed by the first pass could tip under the head
on the second. **Single-pass cutting (each shared edge cut once) is not implemented yet.**

- **Spans are derived, never stored.** `CommonLineGeometry` pairs straight edges of the members'
  clean perimeters (leads excluded, holes and arcs never share) that face each other, lie the
  group's kerf apart within `CommonLineGeometry.GapTolerance` (0.0005) and overlap by at least
  `MinSharedLength` (0.25). Only that overlap is shared; chamfers and other edges are cut as
  usual. A moved or rotated member therefore breaks its group instead of leaving a stale span.
  The tolerances are in model units and sized for inch nests.
- **Group rule:** every member shares at least one span with another member, and the members form
  one connected group. Rows, pairs and grids all qualify.
- **Removing a part** from the plate drops it from its group. A group left with one part is
  removed.

## Desktop

- **Plate > Arrange > Join Common Line** slides the selected parts together one kerf apart
  (`CommonLineJoiner`). The unit nearest the origin stays put. Each other part moves along the
  normal of its best facing edge pair (the longest overlap, then the smallest move) to the part it
  is closest to joining. Facing edges up to four part spacings apart (at least 1) qualify. A part
  already in a group brings its whole group, which moves as one and merges into the new group.
  The kerf is the plate's cutting kerf for the nest's material and thickness (the machine's, see
  [machine cutting defaults](machine-cutting-defaults.md)). With no kerf, or any part left without
  a facing partner, nothing moves.
- **Plate > Arrange > Remove Common Line** removes the selected parts' groups. The parts stay put
  and are then held to the part spacing again.
- The plate draws each shared span as a teal dashed centerline. A member that no longer shares a
  span is outlined in red.
- Moving one member moves only that part and breaks the group. Rejoin, or remove the group.
- Join and Remove are undoable like any other edit.

## Checks

- **Part spacing** (the pre-post review and the desktop's live label): any two members of one
  group are held to the group's kerf instead of the plate's part spacing. Everything else,
  including spacing to non-members and the plate edge, is unchanged. The engines' layout
  certification (`NestLayoutCheck`) knows nothing of groups. Engines never make them, and a plate
  with groups is not an engine result.
- **Common lines** (pre-post, a warning that needs consent like any other):
  - a member that no longer shares a span one kerf from another member, or a group split in two;
  - a group whose kerf differs from the plate's cutting parameters' kerf (when the plate has
    them) by more than `GapTolerance`. A group at the wrong kerf would leave a web or cut a sliver.
    Rejoin it at the current kerf;
  - a lead-in or lead-out of a member that touches or enters another member's material. A lead on
    a shared edge would cut the partner one kerf away. An unreadable lead check is incomplete,
    never clear.

## File format

See [nest file format](nest-file-format.md#common-lines).

## Planned next

Single-pass cutting through Plan Cutting and the posts (cut each shared span once on the
centerline, then each part's remaining edges with compensation), then automatic common-line
placement in fills. Both need machine trials before release.
