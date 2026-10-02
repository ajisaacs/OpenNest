# Nesting engines

Whole-job engines implement `INestingEngine.Solve(NestJob)` and are selected by name through
`NestingEngineRegistry`. Every automatic nesting front end validates engine output the same way;
see [automatic nesting and validation](automatic-nesting.md).

## Built-in engines

Engines are named for the jobs they suit, not for how or by whom they were built. Engine code lives
in `OpenNest.Engine/NestingEngines/<Name>/`, its tests in `OpenNest.Engine.Tests/NestingEngines/`.

| Engine | Best for | Method |
|---|---|---|
| Rectangles | Plain and near-rectangular plates | Each part packed as the box of its material at its minimum-area rotation, using a maximal-rectangles free list; stock chosen sheet by sheet by salvage-credited look-ahead cost |
| Irregular | Irregular profiles | No-fit-polygon frontier packing with gap filling and best-fit pairs, six whole-job strategy variants and a tail re-plan |
| StockLadder | Caller-supplied stock ladders | Constrained-first fill with equivalent-demand area repacking |
| Default, Strip, Vertical Remnant, Horizontal Remnant | Single-strategy fills | The fixed placement strategies behind interactive fill |

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

When remaining demand exceeds two, Irregular also offers Default Fill patterns as optional
multi-member candidates, not as solid bounding boxes or a whole-job Default fallback. It searches
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

## Renamed engines

Earlier releases shipped these as plug-ins under other names. The registry maps the old names so
saved desktop selections, scripts and API requests keep working:

| Old name | Now |
|---|---|
| `Opus55NestingEngine` | Irregular |
| `RectanglesNestingEngine` | Rectangles |

Gpt6Astra and Qwen38FlashNext are no longer shipped and have no alias. A saved selection of either
falls back to Default with the usual status-bar warning.

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
