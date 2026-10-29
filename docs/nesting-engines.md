# Nesting engines

Whole-job engines implement `INestingEngine.Solve(NestJob)` and are selected by name through
`NestingEngineRegistry`. Every automatic nesting front end validates engine output the same way;
see [automatic nesting and validation](automatic-nesting.md).

## Built-in engines

Engines are named for the jobs they suit, not for how or by whom they were built. Engine code lives
in `OpenNest.Engine/NestingEngines/<Name>/`, its tests in `OpenNest.Engine.Tests/NestingEngines/`.

| Engine | Best for | Method |
|---|---|---|
| Default | Any job; used when no engine is named | Runs Irregular, Rectangles and Circles, checks each layout with the layout check and keeps the best: valid first, then fewest unplaced parts, then lowest salvage-credited cost, then the largest compactness offcut; remaining ties keep the earlier engine |
| Rectangles | Plain and near-rectangular plates | Each part packed as the box of its material at its minimum-area rotation, using a maximal-rectangles free list; stock chosen sheet by sheet by salvage-credited look-ahead cost |
| Circles | Round parts: blanks, washers, flanges, gears by their tip circle | Each part packed as its minimum enclosing circle at centers tangent to two of walls, placed discs and clamp keep-outs, with hexagonal and square lattice seeds; bids only when every part is round; stock chosen as Rectangles does, plus whole-job runs preferring each stock and a tail re-plan |
| Irregular | Irregular profiles | No-fit-polygon frontier packing with gap filling and best-fit pairs, six whole-job strategy variants and a tail re-plan |
| FlatBar | Flat bar: parts about as wide as the bar | Each stock read as a bar and cut in one row: the CutList cut-list packer and a best-fill search on part lengths less their self-nesting gain, then a slide-left pass that nests shaped ends and refills freed length; stock chosen bar by bar by whole-plan cost |
| Fill, Strip, Vertical Remnant, Horizontal Remnant | Single-strategy fills | The fixed placement strategies behind interactive fill; Fill is the multi-phase lattice fill (linear, pairs, rectangle best-fit, remainder). In whole jobs, all four place a plate with any fixed or bounded part rotation by the rotation-aware ordered placer instead |

Neither Irregular nor Rectangles wins every job, even within its own lane, so Default runs both
rather than choosing by part shape; Rectangles adds little time and is also the fallback when an
Irregular layout fails the check. A candidate that throws is skipped. Default routes the whole job:
engines cannot share a sheet, so a job mixing plain and irregular parts goes to both engines whole.
Circles is a third candidate that bids only on round jobs (see below), so a job mixing round and
other parts goes to Irregular and Rectangles.

Rectangles places irregular parts validly, but only as their bounding boxes; it never nests into a
notch or hole. Box sides account for how the layout check flattens arcs, so round-edged parts stay
valid at box contact.

### Best plan so far

Whole-job engines report each better whole-job plan as `NestJobStage.PlanImproved` with the plan
in `NestJobProgress.Plan`, built without plate commits; desktop Auto Nest shows it as its "Best
plan so far" sheet strip (see [automatic nesting](automatic-nesting.md)). Plans are unvalidated
proposals with no committed counts, all come before the plate commits, and the last one is the
returned layout. Reporting never changes the search: layouts are identical with or without a
progress sink.

- Irregular reports when a strategy variant or the tail re-plan beats its best plan so far.
- Rectangles makes one sheet-by-sheet run, so it reports the finished job as its one plan.
- Circles reports when a stock-preference run or a tail re-plan beats its best result so far.
- Default scores each candidate plan as it ranks results (valid, fewest unplaced, lowest cost,
  then candidate order) and reports it only when it beats every plan Default has reported, so a
  later candidate never shows a worse plan after a better one. A candidate result that reports no
  plans is offered the same way. When the chosen result is not the last plan reported (a
  candidate's plan can outscore every returned result), Default reports it last.

Plug-ins may report plans the same way; FlatBar and the fixed-strategy engines do not.

Interactive Fill shows its rectangle best-fit (box packing) layout before the best-fit pair
phase, whose first computation for a drawing can take a minute, so **Accept** is available at
once. The box layout is only a fallback: the strategies still run in their usual order and a
completed fill picks the same winner as without it. While the pairs are computed the progress
detail reads "Computing best-fit pairs (may take a minute)...". Accepting or stopping stops
waiting for the pair computation, which finishes in the background and is cached for the next
fill.

Fill's last strategy, TiltedRemnant, adds tilted parts beside the best layout when the room
left past it on the right or top is too narrow for the part at 0 or 90 degrees, such as a long
part beside a grid of the same part turned 90 degrees. It moves the layout flush to the low end
of that axis, so leftover room on both sides becomes one remnant, and also to either end of the
other axis, so the layout's slack forms one strip a tilted part's corner can tuck into. It tiles
the part at the most upright angles that fit the remnant's height and keeps only copies at least
the part spacing from the layout, tested on the outlines `NestLayoutCheck` uses. It is skipped
for circles, for an explicit rotation range, and once the layout holds the requested quantity.

On a plate with clamps, interactive Fill (Fill Area and filling from a part) fills as if the
clamps were absent, then drops every part that enters a clamp keep-out or sits too close to an
existing part, and a whole copy of a multi-part group when any member does. Packing around
the clamps usually gives a worse layout than losing the few parts on them. When every clamp
in the fill area lies on one edge, Fill also fills the area with that edge moved just past the
clamp keep-outs, as a larger edge spacing would, and keeps whichever layout the strategy ranks
better (a tie keeps the dropped-parts layout). Small parts usually win by dropping, large
parts by moving past the strip. The second fill reuses the cached best-fit pairs. Progress
previews are filtered by the same check and never fall below the first layout, so a preview
accepted early is clamp-clear. When a quantity is
requested and copies were dropped, the fill asks for that many more, up to two more times.
Pack and whole-plate Nest still pack only into the clamp-free areas.

Interactive/full-area box packing uses the same 90% work-area slack allowance as Rectangles.
A free box may absorb a slightly oversized side only at its right/top edge when that edge
coincides with the plate work-area boundary. Internal leftover edges keep the strict packing
tolerance, and actual part dimensions still determine spacing away from the plate boundary.

Irregular keeps nominal orientation bounds for line-only outlines, so an allowed rotation can
fit exactly between the configured plate-edge gaps. These bounds use original rotated line
endpoints, retaining material extents even when polygon cleanup discards short-edge chains.
Curved outlines retain conservative flattening-error padding. This does not relax part-spacing
footprints, no-fit polygons or layout validation tolerances; a part extending beyond the accepted
work-area bounds remains invalid.

Irregular fills gaps and open notches using outer profiles; as an engine it does not place parts
inside enclosed cutouts, but its packer also fills one cutout for the pre-pass described under
[Filling cutouts](#filling-cutouts). For a part with two or more copies it also offers its best-fit pairs (two copies
interlocked, as the Best Fit viewer shows them) alongside the single copies, and places a pair
where both members' free regions allow it. Each pair's internal spacing is re-checked with the
layout check before it is offered, and only rotations the part's policy allows are used. A pair
may introduce legal rotations beyond the sampled single poses; these remain eligible even when
none of the sampled singles fits the stock. Both members block space separately, leaving their
notches and intervening gaps available for later parts. A concave footprint is split once into
convex pieces (ear clipping, then merging across every diagonal whose removal keeps both ends
convex, all decided with exact integer cross products on the Clipper lattice); each concave
no-fit polygon is the union of the pairwise convex sums of the two footprints' pieces, which is
the same filled Minkowski sum the edge-pair sweep gives, at a fraction of the union's cost for
outlines with many vertices. A footprint the clipper cannot decompose (self-touching, zero-area
runs) keeps the single boundary/containment union.
Any remaining numerical hole is filled only when its entire ring is certified to lie in forbidden
space, preserving genuine enclosed placement pockets without changing spacing tolerances.

The pairs and the Fill blocks below read each part through one private drawing per part
geometry, owned by the engine instance: pairs and blocks share it, and so do a pipeline's cutout
trial and its ordinary solve. The best-fit search itself runs once per part geometry and spacing
in the whole process: every drawing reconstructed from job geometry
(`DrawingJobMapper.CreateDrawing`) takes its unfiltered candidates from a pool in `BestFitCache`
keyed by the snapshot's exact content (mode and every motion value, bit for bit) and the spacing,
so a repeated or re-tuned Auto Nest of the same drawings, a fresh engine instance, another engine
or the cutout pre-pass skips the search and offers the same pairs. Candidates depend on the program
geometry and spacing alone, so a pooled set is the set the search would produce and placements are
unchanged. The search runs in parallel but keeps rotation and slide-offset order through its stable
area sort, so equal-area candidates come out in the same order on every run. The pool keeps the 512 most recently used sets (`BestFitCache.SharedCapacity`), outlives
the private drawings (`Invalidate` releases a drawing's entry, not the pool) and is in-memory only:
the sets saved in a `.nest` file belong to the caller's drawings and are not consulted here.

When remaining demand exceeds two, Irregular also offers Fill patterns as optional
multi-member candidates, not as solid bounding boxes or a whole-job Fill fallback. It searches
the empty work area and physical leftover space for up to two high-area rectangles. Occupied
outlines are expanded by part spacing before rectangle search. Each sheet prepares blocks initially
and after its first placement, for up to four high-demand-area types; each type has at most eight
new Fill preparations per spacing per solve. Repeated rectangles reuse private drawing/candidate
caches. Quantity-one and quantity-two requests never run block Fill.

Block members are trimmed to remaining demand, mapped back to source-frame rotations, and checked
for legal rotations and internal material clearance before competing with singles and pairs.
Group-only rotations do not expand the single-part rotation choices. Placement intersects all
member free regions and subtracts each placed member separately, preserving usable gaps. A failed
or invalid Fill proposal leaves singles and pairs available. Large enclosed-pocket blocks remain
pending the hole-geometry integration; containment cutting order and shop-use safety acceptance
remain separate sequencer/verification work.

## Flat bar

FlatBar treats each stock's long work-area axis as the bar's length and lays parts in a single
row along it, so it is a one-dimensional cut list. Each part's length is its material extent at
the shortest rotation whose cross extent fits the bar's width; a part that fits no bar is
unplaced. Lengths carry one part spacing each, like a saw kerf.

Parts with shaped ends nest into their neighbours. FlatBar measures each part's self-nesting
gain once per stock by laying three copies the way the layout pass will and averaging the two
joints (so a part that only nests every other copy is not over-credited), and packs each copy at
its length less that gain; the first copy of each run pays the gain back.

Bar by bar, every available stock offers two candidate bars: the fullest bar of the CutList packer
(first-fit decreasing plus its swap optimization, or an exhaustive fewest-bars search for 25 items
or fewer) and the fullest single bar a bounded subset-sum search can fill from the remaining
demand. Lower priority numbers pack first in both. Each candidate is laid out, then the rest of
the demand is priced at the cheaper of each stock's cut-list plan and its repeated best fills. The
winner has the fewest unplaced parts of the most urgent tier, then the fewest unplaced parts, then
the lowest salvage-credited cost. Searches stop at counted node budgets, so results are
deterministic.

The layout pass starts each part box-to-box after the previous one and slides it back with the
Compactor push, trying each of its shortest rotations (so mitred ends nest by turning alternate
parts). Every slid pose is confirmed with `NestLayoutCheck.Clears` and backed off until it passes;
box-to-box always passes. Parts whose outline fills their box skip the slide. A packed part that
does not fit after all returns to the demand, and length freed at the end of a run is refilled
from it.

Limits: one part per position across the width, so narrow parts waste the rest of it; clamp zones
that reach the row block their whole clearance-expanded length; FlatBar is not a Default
candidate and must be selected by name.

## Irregular stock

A stock whose `NestPlateStock.Outline` is set (see [irregular stock](irregular-stock.md)) is
filled only by Irregular: its usable material (the outline less obstacles, eroded by the largest
edge spacing) becomes the `FrontierPacker`'s `PackBoundary`, built once per stock per solve on
the stock's spacing cache, with the rectangular work area as the packer's bounds. Rectangles,
Circles and FlatBar place nothing on it (`SheetByStock` skips it, their fit tests say no), the
single-plate fill strategies skip it in the runner, and Default keeps Irregular's layout when
it is the only valid one. The layout check rejects a part outside the outline or over an
obstacle, so a plug-in that ignores the outline is caught as any other invalid layout.

## Rectangular clamp zones

Whole-job Rectangles and Irregular exclude each stock's rectangular clamp zones,
expanded on all sides by its clamp clearance (`NestPlateStock.ClampClearance`: the rail's
own `Clearance`, else the bottom edge spacing). Part spacing is not added to clamp
clearance; interactive fills, pushes and the layout check use the same clearance. Default runs and validates both lanes as usual. Empty clamp collections
leave the existing packing path unchanged; clamps never become requested or placed parts.

Every `Plate`, including plates the engine builds from stock, stores clamp zones relative
to the sheet's lower-left corner; `NestPlateStock.ClampZones` carries them in the signed
placement frame of part coordinates. Code that compares a plate's zones with parts, cuts
or motions maps them through `ClampZoneFrame.ToPlacement`, which reproduces the stock's
zones exactly; reading `Plate.ClampZones` bounds directly is wrong in quadrants 2-4.

Irregular prepares static rectangle obstacles and their cached no-fit polygons once
per stock per solve, separately from the part-spacing cache. Every orientation's initial
free region excludes these obstacles, including extra orientations introduced by pairs
or Fill blocks. Blocks still size their proposals from work-area/placed-part rectangles;
member free regions determine whether the whole proposal can actually fit around clamps.
Open notches and material between clamps remain available. Inflated synthetic rectangles
are enclosed on the existing clipping grid before NFP preparation, retaining at least one
grid interval per axis so positive sub-grid clamps cannot disappear. Existing arc/grid
safety margins, covered-hole certification and cancellation apply without relaxed tolerances.

Rectangles blocks each clamp's keep-out out of its free list. When part spacing is so
large that the spacing-adjusted block would invert, it blocks a zero-width line at the
clamp edge facing the sheet interior instead: parts on that side keep the exact clearance
and the unavoidable extra exclusion falls toward the nearer sheet edge. Keep-outs narrower
than 0.001" on either axis are judged by a tolerance-free rectangle test
(`KeepOutOverlap`) in layout validation, interactive fill and the overlap check, so they
can neither crash validation nor be called clear.

Desktop clamp editing, the stock-size resolution of a nest's clamp rail, last-resort clamp
removal and the other clamp-aware tools are described in
[automatic nesting](automatic-nesting.md#machine-edge-clamps). Other engines and
plug-ins have not gained clamp-aware placement from this change.

## Repeating completed layouts

Rectangles and Irregular can skip repeated sheet searches after selecting one nonempty layout
whose normal sheet search has completed. The repeat count includes the first physical copy and
is limited by the scarcest requirement on that layout, remaining finite stock and the remaining
physical `MaxPlates` allowance. Parts absent from the layout keep their demand. A salvage-worthy
offcut does not prevent repetition: completion means the engine found no further supported
requested placement, not that no geometric packing exists.

Fast-forwarding applies only while exactly one stock entry remains available. Different stock
IDs count as alternatives even at identical dimensions. Irregular's forced-first-stock tail trial
still counts **all** remaining stock entries, not just that trial's restricted shortlist; it commits
the forced first sheet once when alternatives remain and resumes ordinary stock selection.
Candidates that cannot pass the existing candidate validator retain ordinary one-sheet allocation.
Cancellation never certifies a layout and is checked during physical-copy expansion.

Both engines continue automatically with residual demand in the same solve, including when a
layout's repeat count is one. There is no per-layout stop, confirmation or opt-in mode. Rectangles
retains both Global/Ordered packing modes and its priority guard. Irregular retains demand-dependent
singles, pairs and blocks, variant ordering and its existing work-budget semantics; saved work can
allow additional variants to run, so this is not a promise of historical pose equality.

Raw `NestJobResult` output remains one entry per physical sheet, with fresh placements and unique
instance indices, physical stock usage and physical progress counts. Irregular also retains each
physical copy in its working plan: its last-one-to-three-sheet improvement can return the removed
tail's finite stock, repack that demand, and replace just those copies while keeping the prefix.
Default inherits these fast paths by running the two engines; other engines and external plug-ins
keep their existing search behavior. Quantity-bearing presentation is a separate validated-pipeline
representation; repeating does not bypass whole-job validation or change cutting programs.

## Circles

Circles (`OpenNest.Engine/NestingEngines/Circles/`) packs every part as its minimum enclosing
circle (`CirclePacking/Item`): the circle covers the layout check's outward-flattened perimeter,
so packing is never more lenient than the check. When any readable part fills less than 0.8 of
that circle (a square fills 0.64, a gear about 0.85), it returns everything unplaced without
reporting progress, and Default discards it at almost no cost. Holes are not used here; rings in
rings come from the cutout pre-pass, which hands the engine each nested chain as its outer frame.

Each disc goes, lower priority number first and then largest first, to the best feasible center
tangent to two of: the work-area walls, placed discs, and clamp zones grown by the clamp
clearance (square-cornered, as the layout check grows them). Discs keep the part spacing plus
twice `NestTolerances.ValidationOutline` between them. Bottom-left fills rows and left-bottom
fills columns; each also runs seeded with a hexagonal lattice of the type with the most remaining
area, and bottom-left runs over a square lattice too, which wins when only a few discs span the
sheet. A disc with no feasible tangent center for one radius is skipped for that radius and larger
ones. Sheets and stock are chosen by the loop Rectangles uses (`NestingEngines/SheetByStock`):
salvage-credited look-ahead cost, priority first, and repeated completed layouts. That per-sheet
estimate can take a large sheet first and still need a second, so Circles runs the whole job once
with a free stock choice and once preferring each offered stock, and keeps the best result as
Default ranks candidates (valid, fewest unplaced, lowest cost). The last sheet, and the last two
sheets together, are then re-packed onto each single stock and replaced when that lowers the
whole job's cost, so a small remainder does not finish on a large sheet.

## Filling cutouts

Every whole-job `NestPipeline.Run` (desktop Auto Nest, Console, MCP and API) first places parts
inside other parts' enclosed cutouts, before the selected engine, so every engine benefits and
none needs to know about holes. The steps below prepare frame-plus-insert bundles; the engine
places each bundle's frame as an ordinary part and never sees its reserved inserts.

`CutoutLatticeFill` (`OpenNest.Engine/Jobs/Cutouts/`) fills one closed cutout with copies of one
part. It runs Fill over the cutout's bounds plus one part step on every side, then shifts
that lattice across a grid of offsets of up to half a step each way. At each offset it keeps the
copies whose spacing-grown outline lies inside the cutout, using the part's inner-fit region of
the inscribed, flattened cutout, and the offset keeping the most copies wins. Every returned pose
is then checked against the frame and the other copies with `NestLayoutCheck.Clears`, the test the
layout check uses. The method suits many small copies in a large cutout; a few large inserts are
meant for no-fit-polygon placement. Fill can return different, equally scored lattices on repeated
calls for some parts, so results are not yet guaranteed identical between runs.

The internal `CutoutRouter` can propose copies from the shifted lattice, then search bounded
inner-fit/NFP sample points for remaining copies and other insert requirements. It keeps
original requirement IDs, reindexes accepted copies and checks clearance against the frame
and every previously accepted insert. Lattice shifts account for occupied poses before
quantity trimming; a pre-fill work limit declines giant grids and lets bounded NFP sampling
try instead. A null NFP proposal is not proof of geometric impossibility. No material-area
ratio cutoff rejects a possible placement: the measured 0.10 and 0.35 ratios guide
search order only. Below 0.10 it starts with Fill for three or more copies; between
0.10 and 0.35 it compares Fill-plus-NFP with NFP-only counts; above 0.35 it tries
NFP first, then Fill if demand remains, taking the higher-count valid proposal.
An 0.20 NFP-only rule would lose a second 4-inch square in a 10-inch round hole.
Six geometry-only, anonymized real-job probes and neutral ring fixtures informed
the search-order hints; these limited cases are not a global density guarantee.

Every such hole is also packed by `CutoutPackerProposal`: the Irregular engine's
`FrontierPacker` run with the cutout as its boundary (`PackBoundary`) in place of a rectangular
work area. Each insert orientation's free region starts from its inner-fit polygon of the
inscribed, flattened cutout eroded by half the spacing (the orientation's clearance footprint
carries the other half, so the frame keeps the full spacing exactly as another part would), less
the no-fit polygons of inserts already seated in the hole; the inner fits are computed once per
orientation per boundary. The packer then places every offered requirement at once under its
usual gap-fill and least-advance rule, so copies settle against the wall and each other instead
of at the first sampled point that clears. All six strategy variants pack the hole; the one
seating the most copies wins, and ties go to the tighter layout (smaller bounding box of the
placed inserts, then lower, then further left). Every pose is certified with
`NestLayoutCheck.Clears` against the frame, the occupants and the other poses, as the lattice
and NFP poses are; no tolerance is loosened. The router keeps the packer's proposal when it
seats more copies than the lattice-and-NFP proposal, or the same number more compactly. Two
bars with quantity two, which the lattice never tries and the sampled NFP search scattered
across a tall window, now stand together against one wall. Without a boundary the packer is
unchanged: rectangular stock still starts from the work-area inner-fit rectangle, so engine
layouts are identical.

When the cutout is one circle and every insert fills at least 0.8 of its enclosing circle
(discs, washers, gears by their tip circle), `CutoutCirclePacker` first packs the whole insert
list as discs: largest first, at centers tangent to two of the hole wall and the placed discs,
or at the hole's center. It tries a center-out pass, so a single insert sits concentric, and a
wall-first pass, which reaches the optimal counts for up to nine equal discs. Each gap is the
spacing plus twice `NestTolerances.ValidationOutline`, and every pose passes `NestLayoutCheck`.
Its result is the router's proposal for that hole: the lattice and NFP searches never placed
more there and were slow on round inserts, most of all when demand exceeds what fits.
`CutoutPipelinePrepass` builds quantity-one proxy instances for eligible
closed-cutout frames. It considers legal frame rotations without outer-perimeter symmetry
reduction, shifts insert rotation policies into the local frame, and reserves certified router
poses by original requirement ID. It can fill multiple holes and repeated/different frame
requirements; inserts with a different priority stay independent, and a hole smaller in area than
an insert's outline (a bolt hole) is not searched for it. A part whose every cutout is smaller in
area than the job's smallest part outline (a strap with bolt holes) is an ordinary insert: no part
can land in its holes. Frames are bundled smallest
first, so a larger frame's round cutout can take, through `CutoutCirclePacker` only, a round part
with a cutout some part fits (a washer with an empty bore) or an earlier bundle as one insert: a flange
holds a washer that holds a smaller washer that holds a slug. Chains stop at four parts deep
(`CutoutPipelinePrepass.MaxLevels`). Lattice and NFP proposals never take such inserts, since they
could seat a part in a ring's bore or over a nested bundle's reserved inserts; non-round frames
with such cutouts stay independent. A frame's proposals are compared by physical parts placed, a nested
bundle counting its frame and contents, and ties keep the flat router's proposal. It uses the largest offered spacing for every bundle because the current
job contract cannot condition insert demand on the stock selected later. Remaining demand is
offered to the selected engine once. Only actually placed proxies expand, recursively, to original frame and
insert identities. An unplaced proxy's reservations, including every nested frame and insert,
return to the original unplaced demand;
they cannot be packed retroactively in that same solve. Engine indices and metadata are checked,
then every physical pose, stock, rotation, quantity and fulfillment is independently validated
before binding caller drawings. Forged or geometrically invalid composites return no bindable
plates even with invalid-result consent. Candidate evaluation progress is forwarded
with unverified proxy commitment counts zeroed; transformed proxy commits are hidden. Physical
commits are reported only after physical validation. Preparation is bounded to 32 bundles
and 16 base sweep-angle samples per frame (automatic quarter-turns are all retained);
remaining copies stay ordinary requirements. Within one preparation, geometry reads and
`NestLayoutCheck.Clears` answers are replayed (`CutoutMemo`: same geometry, bit-identical poses),
so repeated frame instances and frame angles do not repeat that check, its main cost; layouts are
the same as without the replay. A hole's proposal is also replayed whenever the same frame, hole
and offered inserts (ids, remaining quantities and frame-local rotation policies) come up again:
automatic-rotation inserts ask the same question at every frame angle, and a second instance of
the frame asks it again until the first instance's inserts change the remaining demand. The
lattice and NFP searches prepare the frame's and the occupants' validation outlines once per
search (`NestLayoutCheck.Prepare`) and test each candidate against the small occupants before
the frame, so a crowded window no longer re-outlines and re-triangulates the frame at every
candidate; the answers are those of the pairwise check. Fixed legal proxy orientations are a
conservative search choice, not full engine choice among all legal orientations; unsupported
bundles remain ordinary independent requirements.

`NestPipeline.Run` keeps a bundled trial that is valid and complete. Otherwise it also runs the
ordinary solve of the same job (with last-resort clamp removal, which the bundled trial does not
offer) and returns:

- the ordinary solve when the trial was refused, with the refusal on
  `NestPipelineResult.RefusedCutoutTrial` (never in `Violations`, since the refused trial has no
  bindable plates);
- the ordinary solve when the trial was incomplete and the valid ordinary solve places more
  physical parts;
- otherwise the incomplete trial, which is a keepable layout like any other incomplete result.

So a job with cutouts may take up to two engine solves; a job without eligible frames takes
one, exactly as before. Expanded sheets list each frame's inserts immediately before the frame,
at every level (innermost first), so the committed part order cuts every insert before the cutout
that would release its slug.
Leads are not changed: pre-post verification still warns when a host's hole lead or an insert's
lead meets the other part, or when a later reorder (by hand or Auto Sequence) puts the host
first (see [pre-post verification](post-verification.md)); Plan Cutting cuts each insert before
its frame and refuses a kept order that does not. Engine benchmarks call engines directly and do
not run this pre-pass; internal `NestPipeline.RunWithoutCutouts` is the ordinary-solve baseline
for tests.

## Renamed engines

The registry maps names used by earlier releases (the first two shipped as plug-ins) so saved
desktop selections, scripts and API requests keep working:

| Old name | Now |
|---|---|
| `Opus55NestingEngine` | Irregular |
| `RectanglesNestingEngine` | Rectangles |
| `StockLadder` | Default |

In v0.3.0 and earlier, `Default` was the multi-phase fill engine now named Fill. `Default` now names the
choosing engine above, so saved selections, scripts and API requests that name it get that
engine. Fill-strategy calls (interactive fill, `PlateFillService`, console fill without
`--autonest`, MCP fill tools) still read `Default` as Fill.

StockLadder, a greedy constrained-first engine kept out of the desktop list, was retired
because Default matched or beat it on every benchmark job, with and without salvage credit and
mixed sheet sizes. Every whole-job engine chooses among the offered stock sizes.

Gpt6Astra and Qwen38FlashNext are no longer shipped and have no alias. A saved selection of either
falls back to the default engine with the usual status-bar warning.

## Changing an engine

Use the [engine benchmarking guide](performance/engine-benchmarking.md) for the command, candidate-sheet controls, and comparison procedure.

- A change lands only when it beats the engine's current result on `OpenNest.Benchmark` for the
  jobs that engine targets, with every layout valid. Report cost, validity, unplaced parts and time.
- Placement must be deterministic: no clocks, unseeded randomness or environment variables. Budget
  work by counting it; wall time may stop work only through the cancellation token.
- Keep each engine's tests passing, including `EngineContractTests<TEngine>` (quadrants, overflow,
  priority, cancellation, stock and plate limits, determinism). Every layout in those tests is
  checked with `NestLayoutCheck`, the benchmark's validator.
- Engines may share code. Move a helper into shared Engine code when a second engine needs it,
  rather than copying it.

## Plug-ins

External engines still load from an `Engines/` folder beside the desktop, console, MCP or benchmark
executable. A plug-in implements `INestingEngine` with a public parameterless constructor and
registers under its CLR type name. A plug-in whose name matches a built-in engine, or a renamed
engine's old name, is skipped: a leftover `OpenNest.Engine.Opus55.dll` cannot shadow Irregular.
Leftover Gpt6Astra or Qwen38FlashNext DLLs still load as ordinary plug-ins until deleted.

## Cost basis and heuristic limits

`NestPlateStock.Cost` is nullable, immutable and per physical sheet. Every available
stock must have a finite positive cost, or all must omit it. Omitted costs preserve
area scoring. The original constructor and area-valued `NetSheetArea` retain their units. `NetSheetCost` applies proportional salvage
to explicit costs; geometric area still controls fit, utilization and material ordering.

Rectangles uses net cost in sheet trials and remaining-material estimates. Irregular
uses it in look-ahead, strategy comparison and equivalent-demand tail replacement.
Default compares valid equally fulfilled results by generic job score.
Fixed Fill/Strip/remnant strategies rank priority fulfillment first, then net cost
for priced stock (gross area for legacy unpriced stock), then existing stable ties.
None guarantees globally optimal purchasing or explores every combination of patterns.

Without salvage credit, which desktop Auto Nest applies only when offering several plate
sizes, every complete layout on one stock costs the same. Default, Rectangles, Circles and
Irregular then break exact cost ties by `NestJobCost.CompactnessOffcut`: the largest
full-width or full-length edge offcut, measured as salvage measures it but whatever the
salvage rate (it honors a positive minimum salvage dimension; with none, any offcut counts).
Larger wins, summed over a job's sheets. It is never part of the cost, so it cannot outrank a
cheaper layout or change a reported cost.
