# Common-line cutting

Two parts placed with straight edges facing each other, **exactly one kerf apart**, can share
one cut: programs are nominal geometry and the controller offsets each contour by half the kerf
(`G41`/`G42`), so both parts' facing edges are cut on the same centerline. Placing parts this way
saves the part spacing along every shared edge, which matters on costly plate.

**Release gate.** Cutting a common line once is not yet confirmed on a machine. Before release it
needs a 3/16 stainless coupon pair on the CI FIBER 4020: measure both parts' shared edges against
nominal and check that compensation starts cleanly in the opened kerf and cancels where a
remainder ends in it (see [CI Fiber output](cincinnati-ci-fiber-post-output.md#common-lines)).
Until then, **Plate > Arrange > Cut Common Lines Twice** keeps the double cut.

## Groups

`Plate.CommonLines` holds `CommonLineGroup`s: the member parts (by reference) and the kerf they
were placed at.

- **Spans are derived, never stored.** `CommonLineGeometry` pairs straight edges of the members'
  clean perimeters (leads excluded, holes and arcs never share) that face each other, lie the
  group's kerf apart within `CommonLineGeometry.GapTolerance` (0.0005) and overlap by at least
  `MinSharedLength` (0.25). Only that overlap is shared; chamfers and other edges are cut as
  usual. A moved or rotated member therefore breaks its group instead of leaving a stale span.
  The tolerances are in model units and sized for inch nests.
- **Z and L shared lines.** Spans of one pair whose ends meet at a corner chain into one shared
  path (`CommonLineGeometry.Paths`), for example two stepped parts interlocked one kerf apart along
  three legs. Two spans meet when their centerlines cross beyond both ends, each end short of the
  crossing by what the kerf leaves there (`kerf/2 · tan(turn/2)`, half a kerf at a square corner),
  and the path turns between 10° and 135° (`MinCornerTurn`, `MaxCornerTurn`). The path runs from
  the first span's free end through each crossing to the last span's free end. Every other span
  is a straight path of its own.
- **Group rule:** every member shares at least one span with another member, and the members form
  one connected group. Rows, pairs and grids all qualify.
- **Removing a part** from the plate drops it from its group. A group left with one part is
  removed.

## Cutting each shared line once

By default Plan Cutting cuts every shared span once (`Plate.CutsCommonLinesOnce`: the plate has
groups and `Plate.CutCommonLinesTwice` is off). Material always comes from the drawings, so a
planned member's open program changes no utilization, quantity, overlap or spacing result.

- **Order.** `CommonLineCutting.Order` takes the members breadth-first from the group's first
  member along its spans. Each shared cut is owned by the member of its pair that comes first,
  so the owner precedes its partner (a captured dependency, enforced by search and replay; a kept
  order that cuts the partner first is a `ConstraintConflict` naming "the common-line partner").
  The tour treats each such pair like a nested part and its host, so a group is cut as one run.
  A rule the search learns later may still separate members; the dependency always holds.
- **The common line is the owner's own edge.** Each member cuts its outside compensated like any
  perimeter. Because the parts lie exactly one kerf apart, the owner's compensated shared edge
  runs on the kerf centerline: that cut is the common line, and it opens the partner's edge too.
  No cut is made without compensation. A Z or L path is cut through its corners as the owner's
  own edges; at each corner the kerf leaves the member on the outside of the turn a fillet of
  about half a kerf's radius where its nominal corner is sharp.
- **Each member is cut** as: its holes (and break cuts and tabbed cutouts, as for any part), then
  its outside, in the perimeter's own direction, never cutting a span an earlier member cut:
  - **Starting on its common line.** A member that owns a shared cut starts its outside there:
    pierced in scrap the external lead-in's reach back along the axis of the edge on which its
    travel enters the path, a straight lead-in to that edge's start vertex, then the common line
    and on round its perimeter with no lead-out. With no span cut before it (the group's first
    member), that is its whole perimeter, closing where it began: a normal closed contour. Otherwise
    it stops in the first earlier member's kerf it reaches, and its other stretches follow as
    remainder pieces, the stretch before its start last, ending at it, beside the partner it is
    cut before. Capture tries the start of each path it owns: the lead-in may touch no material and
    the pierce keeps the pierce zone. When none fits, planning refuses with `UnsupportedGeometry`
    naming both parts and the line. Leads are never shortened, and an external lead-in with no
    length refuses.
  - **Remainder:** a member that owns nothing cuts only its perimeter minus the spans earlier
    members cut, as one open piece per stretch between them. Each piece starts where such a span
    ends. It is pierced in that span's kerf, on the centerline, the external lead-in's reach back
    along the span, and enters along a straight lead-in to the piece's start, so compensation
    starts inside the opened kerf (for a Z or L path, the kerf of its last leg before the piece);
    the pierce meets nothing. It has no lead-out: it ends in the kerf at the next span. A span
    shorter than the lead-in's reach refuses with `UnsupportedGeometry`.
  - Every open piece (a remainder, or a starting stretch that stops in a kerf) is preceded by the
    comment `COMMON LINE REMAINDER CW` (or `CCW`), naming the perimeter's winding, so posts
    compensate it on the perimeter's side.
- **Accounting.** The planner marks the spans earlier members cut on each member's owned clean
  copy as `Tab` spans (`CommonLineCutting.MarkSharedSpans`): material the member never cuts. At a
  path's corner each leg's span runs on to the member's own vertex there, so a Z or L is one run
  of tabs. Replay's contour accounting proves each stretch between them is cut exactly once, as
  for [tabbed cutouts](tabbed-cutouts.md#verification): its pieces tile it with no gap or overlap
  (two pieces where a member starts on its common line inside a stretch), and it refuses a member
  that also cuts a span.
- **Rapids.** When a member's program ends, its pieces, joined across the spans earlier members
  cut, become one obstacle: the part is cut free then. Older programs' uncompensated `CommonLine`
  centerline cuts are never rapid obstacles.
- **Refusals.** A broken group, and a locked member or one a caller left out of the plan, cannot
  be cut once: rejoin, unlock or include it, or cut the plate's lines twice. A drawing pin on a
  member's outside is not applied (a warning makes the plan UNVERIFIED); pins on its holes are.
  Members are never support-tabbed. Remainder pierces in the kerf are held to the pierce zone like
  any other, so a pierce diameter wider than the kerf refuses them.
- **Apply** installs each member's program (holes, then its outside); nothing is added to the
  plate.

### Cut common lines twice

**Plate > Arrange > Cut Common Lines Twice** (`Plate.CutCommonLinesTwice`, saved with the nest,
undoable) keeps the first slice's behavior: each member cuts its whole perimeter, so every shared
edge is cut twice, the second pass running in the kerf the first opened. It needs no post support.
It costs one extra pass per shared edge, and a part freed by the first pass could tip under the
head on the second. Changing the setting after planning makes the plan stale.

## Desktop

- **Plate > Arrange > Join Common Line** slides the selected parts together one kerf apart
  (`CommonLineJoiner`). The unit nearest the origin stays put. Each other part moves along the
  normal of its best facing edge pair (the longest overlap, then the smallest move) to the part it
  is closest to joining. It then slides along that edge when a facing pair across it (the step of a
  Z or L) is within reach, so that pair lands one kerf apart too and the shared line runs through
  the corner. Facing edges up to four part spacings apart (at least 1) qualify. A part
  already in a group brings its whole group, which moves as one and merges into the new group.
  The kerf is the plate's cutting kerf for the nest's material and thickness (the machine's, see
  [machine cutting defaults](machine-cutting-defaults.md)). With no kerf, or any part left without
  a facing partner, nothing moves.
- **Plate > Arrange > Remove Common Line** removes the selected parts' groups. The parts stay put
  and are then held to the part spacing again.
- **Right-click a part** for the same Join Common Line and Remove Common Line commands on the
  selection. Right-clicking a part keeps the selection it is in, or selects it with its group;
  right-clicking empty plate still clears the selection. Join is offered when the selection holds
  at least two units (loose parts or groups), Remove when any selected part is in a group.
- The plate draws each shared path as a teal dashed centerline, through the corners of a Z or L. A member that no longer shares a
  span is outlined in red.
- Selecting a member, by click or window, selects its whole group, so dragging, nudging, pushing,
  rotating and aligning move the joined parts as one and keep their shared spans. Deleting the
  selection deletes the whole group. To move one member alone, remove the group first.
- Copying or cutting joined parts and pasting them keeps each pasted copy joined at the source
  group's kerf; members copied without their partners paste as loose parts.
- Join, Remove and Cut Common Lines Twice are undoable like any other edit.

## Automatic placement

Auto Nest, the server's nesting worker and Fill Area can place parts on common lines themselves.

- **Who may pair.** Only drawings with **Allow common line** set in Drawing Properties
  (`Drawing.AllowCommonLine`, off by default: a shared edge is cut from the centerline, which some
  customers' edge-quality requirements do not accept). A drawing pairs only with copies of itself.
- **The level** is the nest's `Nest.CommonLineUsage`, chosen in the Auto Nest dialog (saved with
  the nest) and seeded for new nests from the nest defaults (`defaults.json`, Tools > Nest
  Defaults):
  - **Off** (the default): engines and fills run exactly as without common lines; the pipeline
    asks the engine nothing more.
  - **When it adds parts:** a common-line layout is kept only when it places more parts than the
    best layout without common lines, or (whole jobs) the same parts on fewer sheets. A tie keeps
    the layout without common lines.
  - **Whenever available:** part count still ranks first and sheets second, so common lines never
    cost a part or a sheet. With both equal, the common-line layout is kept when it is better on
    one of material (a lower salvage-credited cost, then a larger compactness offcut: a shorter
    used length or a larger remnant) and cut length (perimeters less each shared span once), and
    no worse on the other.
- **The kerf** is the plate's cutting kerf for the nest's material and thickness (the machine's,
  see [machine cutting defaults](machine-cutting-defaults.md)). With no kerf, or a kerf not
  smaller than the part spacing, nothing pairs.
- **How.** `CommonLinePair` builds a compound of two copies sharing the part's longest straight
  edge (the second turned half a turn, so that edge faces its own copy one kerf apart; shorter
  edges are tried when the turned copy would come closer than the kerf elsewhere). The compound's
  outline is both perimeters (arcs circumscribed) and the kerf between them, without holes. The
  pair needs a part that may turn half a turn: automatic rotation, or a rotation limit that allows
  the 180° equivalent.
- **Whole jobs** (`NestPipeline`, every engine, built-in or plug-in): after the ordinary solve,
  `CommonLinePipelinePrepass` offers each eligible part as pairs plus its odd copy (all copies
  paired, then about half, so rows that fit an odd count are tried too) and solves again. Placed
  pairs expand into their two parts and one `NestJobCommonLine` group each on the result sheet
  (`NestJobPlateResult.CommonLines`: placement positions and kerf). The expanded layout is checked
  by `NestLayoutCheck` again, and `CommonLineRanking` chooses at the job's level
  (`NestJobOptions.CommonLineUsage`, `CommonLineKerf`, `NestJobPart.AllowCommonLine`). Committing
  adds the groups to `Plate.CommonLines`. Engines themselves are unchanged, and `FillScore` and
  every engine comparer still rank their own layouts.
- **Fill Area** (`PlateFillService.FillItem` with a level and kerf) also fills the area with
  pairs and keeps that fill when it places more parts, or, at "Whenever available", as many and
  the strategy's own ranking does not prefer the plain fill. Other interactive fills, the console
  and MCP fills do not pair.
- **Benchmark:** `--common-lines off|when-it-adds-parts|whenever --kerf <k>` runs each job through
  the pipeline with every part allowed to pair; `off` is the baseline to compare the levels with
  (see [engine benchmarking](performance/engine-benchmarking.md)).

## Checks

- **Part spacing** (the pre-post review and the desktop's live label): any two members of one
  group are held to the group's kerf instead of the plate's part spacing. Everything else,
  including spacing to non-members and the plate edge, is unchanged.
- **Engine results** (`NestLayoutCheck`, the benchmark's and the pipeline's validation): the
  exemption comes only from a result sheet's explicit group list, never from proximity. A group
  must be allowed by the job (a level on, the job's kerf, members whose parts allow common lines,
  each placement in one group at most), smaller than the part spacing, and intact
  (`CommonLineGeometry.Check`).
- **Common lines** (pre-post, a warning that needs consent like any other):
  - a member that no longer shares a span one kerf from another member, or a group split in two;
  - a group whose kerf differs from the plate's cutting parameters' kerf (when the plate has
    them) by more than `GapTolerance`. A group at the wrong kerf would leave a web or cut a sliver.
    Rejoin it at the current kerf;
  - a lead-in, lead-out or (older programs') centerline shared cut of a member that touches or enters another member's
    material. A lead on a shared edge would cut the partner one kerf away. An unreadable lead
    check is incomplete, never clear;
  - on a plate that cuts its lines once, a span no member's program cuts, a span cut more than
    once (both members cutting their edge along it, as a plate not planned since the parts were
    joined does), and a span cut after the partner whose remainder ends in it. A span counts as cut
    by a member whose compensated edge covers it half a kerf off the centerline, or by an older
    program's uncompensated centerline cut;
  - on a plate set to cut its lines twice, a member program that still cuts one once (a remainder).
- The rapid check treats members' remainders as planning does.

## Posting

A program that cuts a common line once (`CommonLineCuts.IsSinglePass`: `CommonLine` motions or
remainder comments) posts only through a post that implements `ICommonLinePostProcessor`. The
desktop's Post command and the console refuse any other post before verification
(`CommonLineCuts.EnsurePostable`), and the built-in posts refuse it themselves; no post falls
back to cutting the line twice. Set the plate to cut its lines twice and plan again instead.

- [Cincinnati CI Fiber](cincinnati-ci-fiber-post-output.md#common-lines): supported.
- [Cincinnati CL](cincinnati-post-output.md#common-lines) and Gravograph IS: refuse.

## File format

See [nest file format](nest-file-format.md#common-lines).

