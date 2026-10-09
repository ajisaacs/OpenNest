# Nesting engines

Whole-job engines implement `INestingEngine.Solve(NestJob)` and are selected by name through
`NestingEngineRegistry`. Every automatic nesting front end validates engine output the same way;
see [automatic nesting and validation](automatic-nesting.md).

## Built-in engines

Engines are named for the jobs they suit, not for how or by whom they were built. Engine code lives
in `OpenNest.Engine/NestingEngines/<Name>/`, its tests in `OpenNest.Engine.Tests/NestingEngines/`.

| Engine | Best for | Method |
|---|---|---|
| Default | Any job; used when no engine is named | Runs Irregular, then Rectangles, checks both layouts with the layout check and keeps the best: valid first, then fewest unplaced parts, then lowest salvage-credited cost; ties keep Irregular |
| Rectangles | Plain and near-rectangular plates | Each part packed as the box of its material at its minimum-area rotation, using a maximal-rectangles free list; stock chosen sheet by sheet by salvage-credited look-ahead cost |
| Irregular | Irregular profiles | No-fit-polygon frontier packing with gap filling and best-fit pairs, six whole-job strategy variants and a tail re-plan |
| StockLadder | Caller-supplied stock ladders | Constrained-first fill with equivalent-demand area repacking |
| Fill, Strip, Vertical Remnant, Horizontal Remnant | Single-strategy fills | The fixed placement strategies behind interactive fill; Fill is the multi-phase lattice fill (linear, pairs, rectangle best-fit, remainder) |

Neither Irregular nor Rectangles wins every job, even within its own lane, so Default runs both
rather than choosing by part shape; Rectangles adds little time and is also the fallback when an
Irregular layout fails the check. A candidate that throws is skipped. Default routes the whole job:
engines cannot share a sheet, so a job mixing plain and irregular parts goes to both engines whole.
A future circle/ring engine joins Default as another candidate.

Rectangles places irregular parts validly, but only as their bounding boxes; it never nests into a
notch or hole. Box sides account for how the layout check flattens arcs, so round-edged parts stay
valid at box contact.

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

Irregular fills gaps and open notches using outer profiles; it does not yet place parts inside
enclosed cutouts. For a part with two or more copies it also offers its best-fit pairs (two copies
interlocked, as the Best Fit viewer shows them) alongside the single copies, and places a pair
where both members' free regions allow it. Each pair's internal spacing is re-checked with the
layout check before it is offered, and only rotations the part's policy allows are used. A pair
may introduce legal rotations beyond the sampled single poses; these remain eligible even when
none of the sampled singles fits the stock. Both members block space separately, leaving their
notches and intervening gaps available for later parts. Concave no-fit polygons are prepared
with a single boundary/containment union.
Any remaining numerical hole is filled only when its entire ring is certified to lie in forbidden
space, preserving genuine enclosed placement pockets without changing spacing tolerances.

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

## Filling cutouts (not yet in production)

Placing parts inside another part's enclosed cutout is being built as a step that runs before any
engine, so every engine benefits. Nothing calls it yet: a part inside a cutout must be cut before
the cutout's contour, and the sequencer does not enforce that order.

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
The router has no pipeline caller and does not change
stock, live demand, cutting order or posting safety; in-hole production use remains held.

## Renamed engines

The registry maps names used by earlier releases (the first two shipped as plug-ins) so saved
desktop selections, scripts and API requests keep working:

| Old name | Now |
|---|---|
| `Opus55NestingEngine` | Irregular |
| `RectanglesNestingEngine` | Rectangles |

In v0.3.0 and earlier, `Default` was the multi-phase fill engine now named Fill. `Default` now names the
choosing engine above, so saved selections, scripts and API requests that name it get that
engine. Fill-strategy calls (interactive fill, `PlateFillService`, console fill without
`--autonest`, MCP fill tools) still read `Default` as Fill.

Gpt6Astra and Qwen38FlashNext are no longer shipped and have no alias. A saved selection of either
falls back to the default engine with the usual status-bar warning.

## Changing an engine

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
