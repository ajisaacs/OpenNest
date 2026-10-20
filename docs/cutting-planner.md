# Unified cutting planner: direct-XY proposals

`OpenNest.Engine.CuttingPlanning.CuttingPlanService` plans contiguous whole-part
programs. It can retain fixed programs or jointly choose internal contour order,
entries and whole-part order using explicitly confirmed cutting parameters. It
returns an owned proposal; `Apply` installs Ready plate-scoped proposals atomically
after an exact freshness check. The desktop opens it from `Plate > Plan Cutting...` and
`Nest > Plan Cutting (All Plates)...` (see [Desktop workflow](#desktop-workflow)); the console
and MCP server expose the same batch (see [Console and MCP](#console-and-mcp)); the older
automatic sequencing and lead-in assignment commands remain until they are retired.

## Capture before worker planning

Create a `CuttingPlanRequest`, then call `Capture` while source placements,
programs and settings are stable. Pass that snapshot to `Plan` on a worker;
`Plan(request)` combines these steps synchronously. The modeled starting point
defaults to `Vector.Zero`, not a discovered controller position.

```csharp
var request = CuttingPlanRequest.ForPlate(plate, startPoint: start,
    confirmedParameters: parameters, expansionBudget: 20000,
    maxEntries: 16, preservePartOrder: false);
var snapshot = CuttingPlanService.Capture(request, cancellationToken);
var result = CuttingPlanService.Plan(snapshot, cancellationToken);
```

- A plate-scoped request (`CuttingPlanRequest.ForPlate`) plans the plate's current
  parts and records its exact state for `Apply`. A detached part list (`new CuttingPlanRequest(parts, ...)`)
  plans the same way but can never be applied. An empty plate is a Ready no-op.
- Omitting `confirmedParameters` preserves the original fixed-program contract:
  locked and unlocked programs stay fixed; only whole-part order may change.
- Supplying confirmed parameters enables regeneration for unlocked placements.
  `eligibleParts` can restrict it to an explicit reference-based subset; an empty
  subset retains all programs but still checks their leads against owned material.
  Locked placements never regenerate. Foreign or duplicate eligible identities
  are invalid; eligibility without confirmed parameters is invalid.
- `preservePartOrder` fixes whole-part order, not eligible internal contour choices.
- With `CuttingParameters.SupportTabs` and a support bed (`CuttingPlanRequest.SupportBed`,
  the plate's own under `ForPlate`), each regenerated part the bed cannot hold level gets a
  tabbed perimeter whose automatic entries keep to points where the tab steadies it. A tab
  that still cannot hold its part level, or sits in an inside corner, is noted on the plan
  without changing its status; see [support tabs](support-tabs.md).
- Parameters are caller-confirmed inputs. The service does not recover missing
  operator settings or silently change lead styles to find a solution.

Capture owns clean geometry, placed programs and required settings. Source `Part`
references are identity handles only: workers never read their mutable state.
Clean geometry accounts for the base program's existing rotation before applying
placement rotation; placement translation is applied once. Subprogram copying
must not rotate shared programs through their property setters. No live drawings
are attached to preview plates, so capture/search do not change quantity accounting.
Planning works from this historical snapshot; Apply compares it with live state.
Original
clean and executable graphs are type/mode-checked before cloning can erase unknown
semantics. Exact placed/proposed copies preserve authored motion feed/exact-stop
flags, symbolic bindings and shared subprogram identity; unsupported graphs are
refused. The geometry-only clean transform uses per-parent copies so legacy rotation
does not visit a globally shared descendant twice; it never changes the fixed payload.

## Cutting dependencies

Nominal material validation recognizes contacts at shared endpoints of adjacent
contour segments using `OpenNest.Math.Tolerance.Epsilon` (0.00001 model units).
This avoids false self-intersection warnings from rounding at arc/line joints,
including the contour seam. Overlapping segments and contacts away from those
joints or between separate contours still fail validation. For adjacent finite arcs
with a shared endpoint, material validation filters native contacts against both
actual arc spans: the native query's angular padding can otherwise report a
supporting-circle intersection beyond one arc's endpoint. This does not increase
the joint-distance tolerance, discard overlapping arcs, or waive a second intersection
inside both spans. Lead and rapid checks keep their conservative contact queries.
Source contour closure
in material capture and emitter preparation accepts endpoint gaps up to 0.000001
model units (inches for an inch nest), with a floating-point roundoff allowance
capped at 1e-12. It does not rewrite source endpoints. The one exception is rounding drift
in an incremental program (converted nests): each move is rounded at the source CAM's
precision, so a contour's end is a sum of rounded moves and can miss its start by more
than that tolerance. On the owned clean copy that planning and its lead reviews capture
(never the drawing), a contour that misses by more than
1e-8 but no more than its moves could accumulate (half of 0.000001 per axis per move, and
never more than `Tolerance.Epsilon`) ends its last cut on its start, and the next motion
absorbs the correction so nothing after it moves. Gaps inside the closure tolerance are
closed too, so material capture and preparation cannot disagree at that boundary. Absolute
programs and larger gaps are left alone and still refuse. A contour entered away from its
seam crosses that gap mid-cut; when an arc follows it, emission bridges the gap with a
straight move and cuts the arc as drawn, rather than starting the arc off its own circle
(which every check refuses as an inconsistent radius). An entry on the seam vertex has no
such bridge, since a lead-in must join the contour itself. Native intersections,
executed-motion checks and pre-post verification retain their existing tolerances.

Arcs of one circle whose centres were each derived from their own endpoints agree only
within the 1e-8 support tolerance, not bit for bit. Their spans are compared at the
1e-6 seam tolerance: arcs that share a span still refuse as overlap, while arcs that
only meet at a joint, or lie apart on the circle (a notched ring), do not.

A seam overcut baked into a clean contour is trimmed from the planner's owned copy
before any of this: the last cut runs on past the contour's own start along its first
cut (same line, or same circle and direction, within `Tolerance.Epsilon`) and a straight
jog returns to the start. Nests converted from another CAM system carry its overcut
this way. The retraced tail encloses no area, so material, regenerated programs and
replay all use the contour closed at its seam; the drawing itself is not edited. Nothing
else is repaired: a jog from beside the first cut, a last cut that stops short of the
seam, lead-in/lead-out layers and symbolic moves are left alone and still refuse.
The overlap analyzer trims the same shape from its own copy (see
[material-overlap diagnostics](geometry/visual-overlap-check.md)).

Capture builds whole-part prerequisites from owned values, and both the search and
the final replay enforce them:

- A cutoff precedes every part its nominal line crosses within its span, using the
  same rule as automatic sequencing: nominal position and limits against the part's
  placed bounds, matched by drawing reference, never by name or trimmed segments. A
  cutoff whose definition is missing precedes every part. Cutoffs need a
  plate-scoped request; they are always fixed programs, need no lead-in, are not
  material for lead validation and, being open cuts, never become rapid obstacles.
  Rapids into and out of them are still checked.
- A cutoff that splits the whole sheet is cut after every part and every other
  cutoff, so the head never travels back across the separated edge. It qualifies
  when its placed program is one continuous straight cut on its own line spanning
  the sheet from edge to edge (`CutOff.SeparatesSheet`). A line trimmed around parts,
  limited short of an edge, on the sheet edge, stale after a move or in an
  unrecognised program form does not qualify and keeps the crossing rule above.
  Being continuous, a separating line passes through no part. A kept order that cuts
  it earlier is a constraint conflict naming the separating cutoff.
- A drawing's break cuts (`LayerType.Break`, see [breakoffs](breakoffs.md)) are open
  cuts inside its material. Preparation keeps them apart from contours (no choices,
  no material); every full and prefix emission cuts them after scribes and before any
  contour, entered from the scrap with the internal lead-in. Their leads may run
  through the part's own material but not touch its boundaries; they are never rapid
  obstacles or unexplained open fragments.
- A drawing's tabbed cutouts (closed holes with `LayerType.Tab` spans, see
  [tabbed cutouts](tabbed-cutouts.md)) are material but not contour choices. Every full
  and prefix emission cuts their pieces after the break cuts and before any contour,
  entered from the slug without lead-outs; accounting requires each span between tabs cut
  whole, once.
- A part whose perimeter lies strictly inside a cutout of another part precedes
  that host. Material bounds (never rapids or scribe marks) only select candidate
  pairs; containment is proven on native clean material. Touching or crossing boundaries, or material that cannot be
  captured, refuse as `UnsupportedGeometry` naming both parts. A part in a concave
  pocket outside the host's material has no dependency.
  Auto Nest's cutout pre-pass commits each insert before its frame, so an unedited
  cutouts-first plate already satisfies this order; its leads are still checked
  ([nesting engines](nesting-engines.md#filling-cutouts)).
- A preserved manual order that violates a prerequisite, or a cycle, is a
  `ConstraintConflict`. Replay rechecks the captured prerequisites and refuses a
  violating order rather than trusting the search.

## Search and exact output

With regeneration, the bounded deterministic search plans internal contour order
and native entry candidates part by part along a whole-part order. Internal
contours precede their own perimeter; parts remain contiguous. Backtracking can
revisit an earlier entry when a later part cannot be reached safely. For multi-part
requests containing regenerated holes, it first tries one ranked hole chain for
each outside endpoint. This lets a later blocked approach change the previous
part's departure without first exhausting combinations of its earlier holes.
This preferred pass uses the hole centres to propose an order and checks only the
one previously lead-feasible entry selected for each hole; it does not prepare
alternative entries until a subsequent recovery/full pass needs them. It still
checks emitted motions under the shared expansion budget and stall limit, and
on failure the full entry/hole-order backtracking pass remains available. A failed
preferred tour first gets a directed-contour recovery pass for parts with at most
16 holes. It
tries native outside entries beyond the ranked shortlist, then builds a hole route
from actual emitted pierces, checking each transition against already released
contours. The part's scribes and other open cuts are checked once from the arrival,
as emitted, and each hole as a direct hop from the one before it. The nearest hole
is tried first, with its entries that leave toward the holes still to cut first, so
a hop does not run back across the hole just cut. Each outside entry gets a share of
the remaining recovery allowance, so one that no hole tour can reach does not starve
the entries after it. This reaches inner edges of interlocking concave parts which can be
missing from the facing-side shortlist. A failed released-hole-subset/departure
state is memoized only in recovery; it never prunes the retained full search.
Recovered whole programs are checked again and independently replayed. Single-part
and no-hole requests keep their existing search order.

A preserved order is followed as given. Otherwise the order is an open
travelling-salesman path over part centres from the start point: nearest neighbour,
then 2-opt reversals and Or-opt moves of one to three parts, never placing a part
before a cutoff or nested-part prerequisite. A part with other parts nested in its
cutouts (a host) is cut after all of them, so the tool reaches it from whichever was
cut last, and from the middle of a crowded cutout every straight approach crosses a
finished neighbour. The nested parts of each host are therefore toured as one run,
immediately before the host, ending beside the point of the host's contours nearest
the tool position at the start of the run; nested hosts apply the same rule inside
the run. Host groups follow the parts beside them, because a finished host is a large
obstacle that can only be left across its own outside edge. Learned rules win over
both: a rule between parts of two groups orders the groups, and rules that cannot
keep a group whole fall back to the plain tour. Plates without nested parts get the
plain tour unchanged, except a large grid: a plate of at least 200 parts with no
cutoffs or prerequisites, whose part centres bin into rows by Y and columns by X
within a quarter of the smallest part extent (every gap between neighbouring bins
wider than that, no two parts in one cell, at least a quarter of the cells filled,
as interleaved rows fill half), is toured as a serpentine: line by line, alternating
direction, along whichever axis and from whichever corner gives the shortest route
from the start point. Its 2-opt and Or-opt passes would grow with the square of the
part count. Replans after a learned rule use the plain tour. If a part on that order cannot be
reached without crossing parts already cut, the search learns "cut this part
before those", backs up to just before the earliest of them and re-plans the rest
from the tool position there; parts cut before that point are kept. The first pass
along an order that teaches such a rule ends the round; the directed-recovery and
full passes run only when the lighter pass taught nothing. An attempt
stops backtracking after a stall of 8 x entries x contours expansions without
getting further, so it learns instead of retrying every entry combination of the
parts before it. The contours are the larger count of the blocked part and the part
cut just before it, whose departure a retry changes, so one many-holed part elsewhere
on the plate does not lengthen every stall. A cutoff is the exception: it is a fixed program and cutting it earlier never
makes its own cut harder, so an approach into a non-separating cutoff that crosses parts already
cut teaches "cut the cutoff before them" at once, without that stall. A rule that would contradict an order already required is skipped.
"Cut before" rules are a heuristic (a part blocked straight after another may be
reachable via a third), so once nothing new can be learned the remaining budget
first tries sequence-free recovery, visiting dependency-ready parts lazily by
material-centre distance. When a branch stalls, it disposes the deeper search
frames and resumes alternatives at the root instead of spending the remainder
on that branch's completed prefix. Any budget left then goes to the retained full
search. Recovery shares the original total expansion budget; exhausting it still
returns a refusal, never a best-effort upgrade.

Candidates use native closest points, vertices, midpoints and circle angles in
stable order. `maxEntries` caps the normal ranked shortlist, not the recovery
catalogue: recovery may inspect the remaining finite native entries, with every
precheck and visited transition charged to the same expansion budget. Circle
rounding, clamping, corner resolution
and tab trimming happen during emission. Validation uses the actual emitted
motions, never the nominal entry point alone. Existing lead styles are not
shortened, disabled or substituted as a search fallback.

Whole-circle candidates rank by distance to their next-cut target; for a hole,
that is the next contour's actual pierce. They do not reward a diagonal point as
if it were a bounding-box corner. All eight
compass options remain available, including the four polar points at 0°, 90°,
180° and 270°, and every alternative still passes the emitted-lead and rapid checks.
Without a next-cut target, the arrival-based rule is unchanged. Polygon corner
preferences are unchanged.

`Round Lead-In Angles` remains an explicit cutting setting: a 90° increment snaps
circular-hole starts to the four polar directions; 45° also permits diagonals.
Snapping can increase reuse of identical hole subprograms and reduce output for
posts that support that reuse, at the cost of a less direct departure. It is not
a clearance exemption or a guaranteed file-size reduction. The planner does not
silently enable rounding, change the increment, or bypass checks on the rounded
motions.

Every candidate rapid is checked against contours already completed, including
earlier holes in the same part. Future contours are not yet obstacles. Rapid
checks skip completed contours whose extents are more than 0.001 clear of the
rapid, ten times the widest band any native contact query allows beyond an
extent; anything closer, touching included, gets the full native check. Native
line/circle queries can report rounding contacts for long leads far from small
circles, and leads keep those results: a lead check skips another part's material
only when their extents are clear by that 0.001 plus 1e-6 times the span of both
extents. The discriminant's rounding error is bounded by about 1e-7 times that span
(about 2e-9 is seen), so a skipped material has no native contact; a 900000-long lead
beside a small circle is still checked. A skipped scan is charged to the native query
budget as if it ran, and ray parity is still asked natively unless the lead's midpoint
lies outside the material's Y range. A pierce zone skips material farther than its
radius by the same gap. Every prefix of a part repeats its earlier contours' leads, so one search
remembers each other-material verdict by the lead curve's exact bits and the material's
reference, and charges the native query budget the scan ran; replay never uses it. An arc's
extent is its whole supporting circle widened to the distance at which the native
contact query still counts it as touched (for a very small arc up to 0.0001 beyond
its radius). Nothing is skipped when either extent has a nonfinite bound or reaches
beyond 1e6, where rounding of large supports can exceed any fixed margin. Actual
lead-in and lead-out line/arc paths must stay in target scrap and avoid other
placed material; holes in other parts remain scrap. Tangent/coincident contacts
outside the genuine target contour joint and numerically uncertain queries refuse.
Material capture supports a simple closed perimeter minus disjoint, non-nested
holes; unsupported topology is not a bounding-box approximation. A line meeting a
tangent arc at a shared vertex, such as a fillet, is an ordinary joint: an exact
contact that the native query rounds away is not uncertain at a line endpoint the
other curve already touches, while a contact anywhere else on the line still refuses.

Source parts rank by modeled travel (material-centre distance at a holed-part
boundary); within a part, preferred contour order and facing-entry rank precede
travel. Ties use stable source/contour/entry ordinals. Hash values and drawing names
are not tie breakers.

When the next whole part is fixed by the tour or a part is already active, the
search generates and checks contour alternatives on demand in contour/entry rank
order. It does not emit every sibling before trying the first. Backtracking still
visits the remaining alternatives when needed; none is pruned. The retained full whole-part
fallback still evaluates all ready sources before ranking their travel distances;
sequence-free recovery instead uses lazy centre-distance order.

The expansion budget counts evaluated candidates and frontier ranking, including
rejections, before emission; it is not a wall-clock timeout. An unvisited sibling
consumes no additional DFS-prefix emission work or per-sibling expansion charge;
entry-catalogue prechecks still emit isolated contours and consume budget.
Consequently a bounded search can
reach a different frontier and report different rejected approaches than eager
expansion did. All visited candidates retain their lead/rapid checks, and a selected
plan still receives fresh independent replay. Callers can cancel. Exhaustion returns
a refusal, not an unranked fallback or proof of geometric impossibility.

## Automatic outside entries and look-ahead

An unlocked part's outside entry is chosen automatically toward the NEXT cut: the ranker orders the native candidate
catalogue by the facing side(s) of the next part's placed-material centre, and
the shared lead validator certifies each emitted lead lazily until up to
`maxEntries` feasible candidates remain. At caps of four or more, a corrective
scan reconsiders memoized clear candidates for each missing side before evaluating
more of the catalogue; a later side cannot lose a usable point merely because
an earlier side's scan passed it. Replacements preserve other covered sides and
global rank, never exceed the cap, and stop evaluating the tail once coverage
settles. Smaller caps retain rank priority rather than promising all-side coverage.
An inset near a convex corner is measured along the selected native straight edge from
that edge's own endpoint, not a neighboring entity's endpoint. This keeps the
candidate on its selected entity when a joined contour has an accepted rounding gap;
no entry-membership or lead-clearance tolerance is widened.

With no lead-out the tool leaves a part from its outside entry. An entry inside a slot
or pocket can only be left back over the part just cut, so within the ranked catalogue
entries are ordered by how many straight sight lines to the next cut clear the part's
own perimeter: the look-ahead point and, for an ordinary next part, the four corners
of its cut extent. This is a stable reordering before the lead checks; no entry is
dropped, and every emitted rapid is still checked.

When the next cut is the host whose cutout the part sits in, the look-ahead point is
the nearest point of the host's own contours, not its centre (which lies in the middle
of the cutout, among the other nested parts).

The next cut is the next unfinished part in a supplied order — recomputed
after every learned-order replan — or, in the full fallback search, the nearest
dependency-ready remaining part, stable-ordinal ties; the last part has no
target and ranks by tier then distance to the tool's arrival. Between source
parts the tour stays nearest-first; the look-ahead rank only orders the entries
inside one part's contour stage, so distance sorting cannot undo the facing.
Uncertain (numerically incomplete) validator answers are not geometric refusals:
those candidates can fill remaining retained slots for emitted-prefix checking and
complete replay. The normal ranked entry count stays within `maxEntries`; directed recovery can
inspect additional native entries as described above. A
part/contour with no fitting lead in its fully evaluated catalogue is reported
as "No tested lead-in fits on part N, contour M"; budget exhaustion stays a
budget finding and incomplete checks are never presented as geometric
impossibility. Lead prechecks are reported separately and their requests also consume
the shared expansion budget before native work.

For a holed part, the search chooses an outside endpoint before cutting any hole.
Each endpoint branch builds an open hole-centre route from the original arrival to
that endpoint, using bounded nearest-neighbour, 2-opt and Or-opt improvement.
A cutout that contains the arrival point (the tool comes from a part nested in it) is
routed from the arrival point itself, not its centre, so it is cut first, before a
later hop to it would have to cross the other parts nested there.
Preferred hole entries are then resolved backward from the outside's actual emitted
pierce, ignoring scribe marks: each hole faces the following contour's actual pierce.
Convex corners lead the preference tiers, then straight midpoints/tangent joints,
then native fallbacks. Reflex/cusp corners remain manual-only.

A hole is left where it was entered too, so ahead of those tiers its entries are ordered
by whether the straight rapid from the entry to the following contour's pierce clears the
hole itself. Without this, the midpoint of a tiny corner arc (it lies on both facing sides
of the hole, so it ranks as an ideal corner) is left along its 45° tangent, and a shallow
rapid to a nearby next hole re-enters the corner and crosses the hole just cut; every
outside endpoint's preferred chain then fails the same way. The full search orders its
hole entries the same way toward its route's next pierce. This is a stable reordering
measured from the nominal entry, which is the departure when there is no lead-out; no
entry is dropped and every emitted rapid is still checked.

This preference orders the search; it never certifies a rapid or prunes alternate
retained entries or hole orders. A different outside endpoint recomputes its hole
preference. Every standalone emitted prefix is replayed from the original part
arrival and a copy of the checker from before that part, not from the previous
prefix (which would double-consume holes and scribes). Holes are cut once, the
outside last, and scribes once. Locked programs remain exact. A crossed preferred
route must recover through checked backtracking or return a refusal, never unsafe
`Ready`.

Selected programs are replayed from the beginning with a fresh checker and fresh
lead validation, without regenerating them or trusting cached search verdicts.
Before replay, expected-emission geometry is independently built from the owned
choices/settings, not from the candidate payload. Replay checks actual selected
code against it and independently accounts for directed native boundary coverage:
no partial, duplicated, retraced or reversed cuts, except the exact selected tab.
Equivalent subdivisions and merged collinear moves remain valid. Captured source
identity and pose binding use exact scalar bits, not geometric tolerance.
Arrival positions use actual departures, including lead-outs and subprograms.
`ProposedOrder` retains source identities/poses; `CopyProgram()` returns an
independent deep copy of each exact captured/generated program. `ContourChoices`
are nominal choice metadata, not a substitute for reading actual execution.
Neither obtaining a proposal nor copying its programs installs them on live parts.

## Results and refusal

`Ready` and `IndependentlyReplayed` describe the modeled proposal only. In the
no-parameter fixed route, replay checks rapid crossings and
incomplete retention; it does not add regeneration-mode material/lead checks.
In regeneration mode, replay also checks actual lead paths and contour accounting
against owned clean material. Neither mode certifies final NC, production cutting
readiness or physical machine safety.

Findings and source ordinals use the original zero-based source list, not proposed
sequence positions. Strict service refusals contain no proposed order. The desktop batch may
produce a separate `BestEffort` proposal as described below; it never labels it `Ready` or
sets `IndependentlyReplayed`.

- `ConstraintConflict`: fixed programs or explored fixed routing violate the
  modeled constraints. Locked internal crossings cannot be repaired by regeneration;
  with confirmed parameters they warn instead (see [fixed emissions](#fixed-emissions-warn)).
- `UnsupportedGeometry`: unsupported motion/material semantics or an incomplete
  check. Open nominal outlines, ambiguous release states or containment, cutoffs
  in a detached part list and scribe-only source drawings are not silently accepted.
- `InvalidInput`: malformed/missing/duplicate placements or settings, invalid
  geometry, empty input, invalid eligibility or nonpositive bounds.
- `NoSolutionWithinBudget`: the bounded/capped search found no complete proposal;
  it does not prove no possible geometric route exists.
- `Cancelled`: capture, search or replay cancelled without live mutation.

Malformed original executed graphs are refused, not salvaged. Valid but incomplete
old programs can regenerate from clean geometry. Regenerated programs retain genuine
configured tab gaps; stale tab settings do not establish retention. In confirmed-
parameters mode, an unlocked program a caller left ineligible must cover the complete
directed clean boundary: an open one has no certified selected tab metadata and is
refused, not repaired. A locked program that does not (a tab gap, a partial, reversed
or retraced cut) is kept as given with a fixed-lead warning instead; see below. The
no-parameter route retains its narrower compatibility contract and still refuses
fixed-program violations. A planned lead-out that may bridge a tab, or a malformed
emitted arc, is refused, not automatically repaired. Tabbed lead-outs leave from the
trimmed cut end, but a lead-out after an open contour still needs manual review of
its retention gap, so confirmed-parameters planning refuses it in a planned contour.

### Fixed emissions warn

With confirmed parameters, a locked program (Place Lead-in locks, PEP-imported locked
parts) is a fixed emission: everything it does after the planner's rapid into it is
accepted as given. So is each drawing-pinned contour of a
regenerated part (see [drawing pins](#drawing-pins)). Their violations are not refusals:

- Its lead-ins and lead-outs meeting another part's material or its own, a rapid inside
  it crossing a completed contour, a lead-out after its own open contour, and coverage
  that cannot be certified (a tab gap, a partial, reversed or retraced cut) each become a
  `PostVerificationKind.FixedLead` finding naming the part, the cutting contour where
  known and the other part hit (`CuttingPlanFinding.FixedLead`).
- Fixed emissions never constrain the search: they neither reject a candidate nor teach
  an order rule. Independent replay reports them. Every planner-chosen motion keeps
  every strict check, including the rapid into the locked program and every
  regenerated lead.
- A plate whose only findings are these warnings yields `BestEffort` with
  `IndependentlyReplayed` set (`CuttingPlanPlateResult.HasOnlyFixedLeadWarnings`): never
  `Ready`, so `IsReady`/`CanApply` stay false and `CanApplyWithWarnings` with the
  per-proposal acceptance checkbox is the only way to Apply. `CuttingPlanService.Apply`
  still rejects it. The locked program is never replaced.
- Known part-to-part material overlap, unreadable or unsupported programs, stale input
  and cancellation still block. Unlocked programs a caller left ineligible keep the
  strict rule above.
- The [pre-post review](post-verification.md) checks locked leads again before posting;
  accepting the plan is not posting consent.

### Drawing pins

A drawing's cut pins (`Drawing.CutPins`, from the drawing library) fix how named contours are
cut: the entry point, lead-in, lead-out and tab; the drawing's pins take precedence over the
plate's settings. Capture resolves each regenerated part's pins against its own rotated
clean contours with `CutPinResolver` and prepares them with
`PreparedContours.Capture(program, parameters, pins)`:

- A pinned contour has exactly one entry, the pin's; every candidate catalogue returns it
  alone and emission refuses any other entry for that contour, so the search can never move
  it. It is emitted through the same emitter with the pin's own lead-in and lead-out for
  every contour type, the pin's tab (or none) and angle rounding off; the emitter's own
  geometry rules (pierce clearance, small-circle clamping) still apply as in any emission.
  The certified tab gap is the pinned outside's own.
- Unpinned contours on the same part, and every other part, keep the plate's automatic
  selection; a plate without pins plans exactly as before.
- A pin is never guessed. One that lies on no contour, on several, shares a
  contour with another pin, or cannot be placed on the program leaves its contour automatic
  and adds a `FixedLead` warning naming the pin (`FixedLeadWarning.PinNumber`), for example
  "Drawing pin 2 no longer matches the geometry; planned with nest settings.", which makes the
  plate UNVERIFIED. A tab pinned on an internal contour is not honoured either: only the
  outside is tabbed.
- A pinned contour's lead-in, cut, lead-out and tab are a fixed emission like a locked program
  (see [fixed emissions](#fixed-emissions-warn)): a pinned lead that meets another part's
  material or its own part's (an already-cut hole included) is a `FixedLead` warning naming
  the part, cutting contour, pin and the part hit, with the advice "Increase spacing or move
  this part away from the part it meets, then replan". The entry is never moved
  and the lead never shortened to fit; the feasibility precheck accepts it as given. The
  rapids between contours, including the one into a pinned contour, stay strict. Plan
  Cutting does not model clamps, for pinned leads or any other.
- A placement with `Part.DrawingPinsOff` ignores its drawing's pins and plans entirely from
  the plate settings, with no warning. `Part.HasActiveDrawingPins`
  is the shared test for "pins apply here".
- A pinned part is regenerated like any unlocked part, whatever lead-ins or settings it
  carried before. A locked part keeps its exact locked program and warns "Locked lead-ins
  override drawing pins on part N; use Plate > Remove Lead-ins, then Plan Cutting, to apply the
  pins." It is not refused. An
  unlocked part a caller left ineligible warns that its pins are not applied.
- The plate state compared by `Apply` includes each drawing's pin list (reference and exact
  content, `StateFingerprint.Of(pins)`) and each part's pins-off flag, so changing either
  after capture makes the proposal `Stale`.
- Only Plan Cutting applies pins. The legacy Assign Lead-ins (`LeadInAssigner`) leaves every
  unlocked part with active pins unchanged and returns those parts; the desktop then says
  "N parts kept their drawing lead-ins (plate P: parts ...); use Plan Cutting to apply their
  drawing pins." It never overwrites a pin with the plate's settings.
- The [pre-post review](post-verification.md) warns "Drawing pins not applied" for a part
  with active pins whose placed program does not cut its pinned contours as pinned, and
  re-checks pinned leads like locked ones.

## Apply

```csharp
var commit = CuttingPlanService.Apply(results, cancellationToken); // one result per plate
```

Call it on the thread that owns the plates, with Ready, independently replayed
results from plate-scoped requests; anything else is `InvalidInput`. Apply never
replans. Each plate is compared exactly with the state captured with its request:
part list instance and order, plate quantity/size/quadrant and settings, cutoff
definitions, and for every part its program reference and exact content (an
in-place edit counts), drawing program, cutoff classification and cut pins, pose bits,
lead-in/lock/pins-off flags, settings (reference and exact content) and bounds. Any
difference on any plate returns `Stale` and changes nothing, so a proposal that
changes a plate can be applied once; an unchanged (no-op) proposal stays current.
A malformed live program is also `Stale`, not an exception. A part repeated on
two plates of one scope is `InvalidInput`. Caller-confirmed planning settings are
input, not plate state: editing a separate confirmed-settings object after
capture does not stale the result (confirmed settings that are also a part's or
the plate's live settings are live state, and editing them does). Settings are
compared member by member, and only the exact built-in settings types are
supported (the same set regeneration copies): a plate-scoped request whose part
or plate settings, or any lead-in, lead-out, tab, sequencing or assignment
object inside them, is another type (a subclass included) returns
`UnsupportedGeometry` without running that type's code. A settings object
replaced by such a type after capture makes `Apply` return `Stale`. Detached
part-list requests do not capture settings and are unaffected.

The whole scope is validated and its bounds staged first; cancellation is checked
immediately before the install. Order changes through `ObservableList.Reorder`
semantics: same references, no `PartAdded`/`PartRemoved`, so drawing quantities,
sentinel plates and plate lists are untouched. Regenerated parts receive a fresh
owned copy of the replayed program and of the settings captured with the request,
keep their pose and lock, and are marked as having lead-ins. Fixed programs are
not replaced. An exception during install restores every plate exactly and returns
`Failed`. The installer itself is internal: it trusts these owned payloads and
checks only root program references, so `CuttingPlanService.Apply` is the only
public path. After the whole scope is installed, each changed plate raises
`Plate.PartsReordered` once; an observer exception is reported in `RefreshErrors`
on an `Applied` result, not as a rollback.

## Desktop workflow

`Plate > Plan Cutting...` opens a cutting inspection panel beside the main plate canvas;
`Nest > Plan Cutting (All Plates)...` opens the same panel for every occupied plate.
It captures scope on the editor thread, plans on a worker, and shows only a fresh,
supported detached proposal in that canvas, including proposed part numbers and
rapids. The live `Plate`, part order, programs and quantities remain unchanged
until Apply. The outcome and complete structured findings remain available while
navigating plates; selecting a finding marks its recorded witness (overlap area, rapid,
contacts) like Validate Nest, or frames the affected part extent when it has none, never an
invented contact point. Unsupported or stale output has no proposal preview.

The panel shows the current cutting settings; editing them or switching Keep current
part order starts a new plan and clears the previous warning acceptance. Apply uses
the exact captured proposal, with an unchecked per-proposal acceptance required
for UNVERIFIED results; a blocked plate disables the whole-batch Apply. A successful
Apply persists the confirmed settings and refreshes the editor; a failed/stale Apply
changes nothing and requires replanning. Escape, the panel X or Cancel restores the
live view and prior viewport; Close during planning waits for the worker before
unlocking inspection. Planning is not live-nest validation or posting consent. The
panel's **Validate Nest (live programs)** mode separately checks current installed
programs across all plates and navigates typed findings on the live canvas; it
never certifies an unapplied proposal. Posting still initiates its own fresh review.
The former standalone planning dialog is retired; only the docked Plan/Validate
panel is wired in the editor. Non-editor callers still use the shared analyzer.

- The panel starts from the plate's cutting settings (or the last-used settings) and plans
  at once. `Cutting Settings...` edits them and `Keep the current part order` fixes the
  whole-part order; either change replans. The settings are confirmed parameters: every
  unlocked part's lead-ins are regenerated. Locked parts keep their exact programs; their
  own violations are fixed-lead warnings on an UNVERIFIED plate (see
  [fixed emissions](#fixed-emissions-warn)), and any warnings require explicit review below.
- When a plate in scope already carries applied lead-ins or cut-offs, a red banner
  above the settings says how many (and how many lead-ins are locked), and that the
  plan keeps locked lead-ins and existing cut-offs unchanged. `Clear Lead-ins &
  Cut-offs...` asks first, then removes every applied lead-in (locked and hand-placed
  ones too), every cut-off and the plates' recorded cutting settings, keeping part
  poses, order and quantities, and replans from scratch. There is no undo. The button stays
  enabled while a plan runs: confirming cancels that run, then clears once the cancellation
  lands (so the plates never change under the planner) and replans.
- `Add scrap cut-offs` runs the automatic cut-off planner (defaults 35 in /
  889 mm strips, half-strip minimum, 12 in / 304.8 mm tail) on every plate in scope
  *before* capture, so the cutting plan sequences the new lines like any
  other cut-off. Each added cut-off is moved in the part list to just before the first part
  its nominal line crosses (a sheet-separating one last, one that crosses nothing ahead of any
  existing separator), so a kept order, including the fallback after a free search runs out of
  budget, satisfies the cut-off rules; other parts and cut-offs keep their positions.
  These are the only live changes made before Apply: Apply keeps them,
  while unticking the box, Close or switching to Validate removes them and
  restores the earlier cut-offs and sequence. The proposal preview draws planned cut-offs
  with the same orange-red dashed line as the editor. A blocked cut-off plan adds nothing and
  plans nothing; the panel names the plate and reason.
  `Cut-off Settings...` opens the Automatic Scrap Cut-offs dialog in settings mode for
  the same scope: it previews on the active plate as usual, but `Use These Settings`
  only returns the values (it adds nothing), ticks the box and replans. The panel
  remembers the last settings per unit system until the application closes. Cut-off
  order follows the dependency rules above: crossing cut-offs before the parts they
  pass through, sheet-separating cut-offs last.
- Lead-ins are optional for planning. With `None` (or a style that emits no lead
  motion), the emitter places the pierce directly on the selected contour point.
  Unlocked contour starts can still move to improve travel; locked programs keep
  their existing starts. Search and independent replay still check rapid crossings,
  emitted lead paths when present, and contour accounting. The separate pre-post
  missing-lead warning remains available for operator review; a ready plan does not
  waive posting checks.
  When a lead hits another part, the finding suggests more spacing or a shorter lead;
  when no tested entry fits, it suggests reducing lead-in length and, if neighbours
  obstruct it, spacing the parts farther apart. These are suggestions, not guaranteed
  fixes: replanning runs the same strict checks; an unverified fallback requires
  separate warning acceptance and is not approval to post or cut.
- The desktop free-order batch first tries up to eight entries per contour for at most
  1,000 expansions. Only a Ready proposal that passes independent replay is used;
  otherwise it runs the original 16-entry search with its entire plate budget.
  Cancellation stops both attempts. This short pass can choose a different valid
  route, and a hard plate can take up to 1,000 extra expansions before the full
  attempt. The current-order retry still uses its original 16-entry budget.
  Returned expansion counts describe the retained attempt, not a discarded
  short pass plus its full-cap retry.
- Every plate is captured on the UI thread and checked and planned on a worker. Clean part
  material is checked for overlaps with the pre-post overlap analyzer; known overlapping parts
  still block that plate whatever its route. Incomplete checks remain visible warnings, never
  a clear result (see [pre-post verification](post-verification.md)). A free-order search that ends
  `NoSolutionWithinBudget` is retried once with the current part order, and the summary
  says the order was kept. Both are allowed 400 expansions per part (at least the
  default 20000), because both still plan contour order and entries for every part.
- **Tools > Options > Cut planning effort** (`Settings.CuttingPlanEffort`, default 1.0,
  0.5–10) multiplies both of those budgets. The panel reads it when planning starts and
  passes it to `CuttingPlanBatch.Capture(..., budgetMultiplier)`; the worker never reads
  settings, and out-of-range stored values are clamped. The budget counts expansions, not
  time, so a given effort gives the same plan on every computer; faster hardware only
  finishes it sooner, and raising the effort is how to spend that speed on hard plates.
  The eight-entry, 1,000-expansion first pass is not scaled: it is a cheap probe for easy
  plates, so a higher effort spends more only where the full search runs and leaves easy
  plates' plans unchanged. A plate refused because a search reached its limit (its own
  plan, or the free-order attempt before the current order was kept) says the effort can
  be raised; a longer search may still find no plan. The effort is a desktop setting: console
  `--plan-cutting` and MCP `plan_cutting` run the same planner at the default effort (1.0)
  and have no effort option, and the nesting API does not plan cuts. Direct `CuttingPlanRequest` callers set `expansionBudget` themselves.
- The docked panel keeps text short: one colored outcome line for the batch, one line for the
  shown plate, and one detail line (a remedy for a blocked plate, or what the selected marker
  shows). Below them is a read-only, unsorted issue grid with Plate, source Part, With, and full
  finding text. Every overlap issue/pair and every planner finding has its own selectable row.
  **Previous**/**Next** (shown for more than one plate) and issue selection navigate the editor
  to that plate's live source sheet, then display its detached proposal when available.
  Overlap pairs and checker findings carry their world-coordinate witness
  (`CuttingPlanDiagnosticRow.Witness`, from `CuttingPlanFinding.Witness`), so selection shades
  the overlap or draws the rapid and its contacts with the Validate Nest overlay, on the
  proposal or on a blocked plate's live sheet. Findings without a witness zoom to the affected
  part extent (or both parts for a pair), mapping source positions to proposed cutting order.
  If a plate cannot produce a current, supported proposal preview, its own live sheet remains
  visible; no unsupported geometry is cloned. Replanning, cancellation and stale Apply clear
  old warning targets.
- The plate line gives ready plates' part counts, order change and rapid travel, and other
  plates' blocking reason. Unverified proposals retain every overlap and route
  warning in the selectable grid before the acceptance checkbox is used; they do
  not truncate later parts' warnings. Finding part numbers are the plate's current order, as the
  editor numbers them. The panel shows the selected plate detached from the nest (quantity
  zero, so drawing quantities do not change) in the proposed order with the proposed
  programs. Ready and best-effort proposals can be previewed only while the plate still
  matches capture. Best-effort previews are labelled `UNVERIFIED`. Refused program graphs
  are never copied, and changed plates require replanning.
- Apply requires usable output for every plate and remains all or nothing. The outcome line
  counts best-effort proposals as usable when reporting how many plates block Apply;
  their warnings still require acceptance. Open nominal contour findings identify the
  contour number, endpoint gap in model units (the nest's linear units), and closure
  tolerance, so the source can be reviewed in the drawing editor. Reporting never
  closes gaps or changes the validation tolerance. A fully ready batch
  can apply immediately; an unverified batch requires the unchecked, per-proposal
  `Accept the unverified plan` checkbox (posting consent is still separate). Replanning clears it.
  Both use the same owned-program, freshness and rollback boundary. After it applies, each plate keeps its own copy of the
  confirmed settings, which also become the saved defaults. `Stale` keeps the panel open
  and asks for a replan; nothing changes.
- Closing or cancelling while planning cancels the worker and keeps the panel locked until
  it stops. Planning and Apply refuse to start while a nesting or plate operation runs.
  The panel plans with its own copy of the settings, and posts progress and results to
  the thread it was created on rather than to whichever context is current.
- `PlateView` follows `Plate.PartsReordered`: it redraws parts in the plate's order (the
  numbers it draws are the cutting order), rebuilds their graphics and marks the overlap
  check out of date. Both the editor and preview build outlines and lead paths in the
  drawing-local frame, then apply the part placement once. Absolute (G90) programs
  therefore follow moves and clones just like incremental (G91) programs; displaying a
  part does not rewrite its program or coordinate mode.

### Best-effort fallback for imperfect geometry

When strict planning reports unsupported or incomplete geometry, the desktop batch
automatically attempts a bounded, deterministic best-effort proposal. Touching, intersecting
or numerically uncertain material boundaries need not prevent lead-in generation when their
closed executable contours are readable. The fallback reuses the existing contour emitter:
internal contours first, the largest bounding perimeter last, nearest entry from the preceding
departure, with the confirmed lead styles. The internal contours follow the same open
nearest-neighbour + 2-opt/Or-opt tour over hole centres that the strict search proposes, not
source order, so their rapids do not cross each other. It retains source part order except to satisfy
proven cutoff and nested-part prerequisites. `Keep the current part order` still refuses an
order that contradicts a proven prerequisite. Uncertain containment is named as a warning.

No source contours are repaired, removed, simplified, or silently closed. Locked programs
and cutoffs remain exact. Unknown/null/recursive instruction graphs, suppressed or nonfinite
motions and contours the emitter cannot represent still refuse. This fallback is for
incomplete geometry checks, not every constraint conflict or exhausted search. Known
inter-part material overlap still blocks the entire batch.

The result retains strict refusal findings and available rapid-check findings, and is
explicitly not a certificate of lead clearance, rapid travel or material containment.
Inspect the preview and warnings before accepting it. A route with only an incomplete
overlap check may likewise be accepted with warnings. `CanApply` and parameterless batch
`Apply()` remain strict; `CanApplyWithWarnings` and `Apply(acceptWarnings: true)` are the
explicit review path. `CuttingPlanService.Apply` still rejects best-effort results directly.
Acceptance never skips the separate pre-post checks or grants posting consent.

## Console and MCP

Both front ends run `CuttingPlanBatch` exactly as the desktop does: capture, plan, then an
all-or-nothing Apply of the captured proposal, with the same summary text. Neither adds scrap
cut-offs; plan those in the desktop.

Settings are never invented. They come from an explicit cutting settings JSON file (the
format `CuttingParametersSerializer` writes, as stored in `.nest` plates), or else from the
plates' own saved settings, which must be identical across the plates in scope. A JSON
`null` is refused rather than read as the defaults. Without either source, planning is refused.

- Console: `--plan-cutting` plans the plates the run produced (the target plate, or every
  sheet `--autonest` added) after filling and before saving or posting; `--no-fill` plans a
  loaded `.nest` as it is, and `--all-plates` widens the scope to every occupied plate.
  `--cutting-params <path>` supplies settings and `--keep-part-order` fixes whole-part order.
  A blocked or cancelled plan exits 2 and saves or posts nothing. An unverified plan is
  applied only with `--accept-unverified-plan` for that invocation; posting still runs its own
  verification and needs `--acknowledge-post-risks` separately.
- MCP: `plan_cutting(plateIndex = -1, keepPartOrder, cuttingParametersPath)` reports the
  proposal without changing anything and keeps it in the session when it can be applied;
  `-1` plans every occupied plate. `apply_cutting_plan(acceptWarnings)` applies that exact
  proposal once. It is refused when a planned plate left the session (for example after
  `load_nest`) or changed since planning (Stale), and an unverified plan needs
  `acceptWarnings=true` on that call. Plate numbers in the report are session plate indices;
  part numbers are 1-based positions in each plate's part list. Save with `save_nest`.

## Remaining integration boundaries

The service does not establish clean-material non-overlap, scrap release by open
cutoff cuts or sheet edges, or physical retention strength. It does not write CNC
or set posting consent. A `Ready` proposal can still be unsuitable for cutting.

The lead-in side panel's Auto-Assign Lead-ins remains separate from Plan Cutting.
Review the desktop preview and the generated CNC output before cutting.
Fresh [pre-post verification](post-verification.md) is still required; it is not a
physical safety qualification.

## Portable regression gate

```sh
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter 'FullyQualifiedName~CuttingPlanning|FullyQualifiedName~PostVerificationAnalyzerTests'
```

Synthetic fixtures exercise whole-part routing and internal-hole crossing repair,
ineligible refusal and locked fixed-lead warnings, actual native lead paths, shared subprograms, ownership,
entry/whole-part backtracking, deterministic budgets, cancellation and fresh replay
rejection. Retained emission characterizations cover styles, winding, corner rules,
circle rounding/clamping and tabs; unsupported cases remain explicit refusals.
`PlanningCharacterizationTests` freeze the batch output (proposals, expansions, emitted
motions) for the six synthetic nests and focused fixtures; a change that is meant to
leave plates without fixed-lead warnings untouched must keep those digests.
