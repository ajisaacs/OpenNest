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
| Irregular | Irregular profiles | No-fit-polygon frontier packing with gap filling, six whole-job strategy variants and a tail re-plan |
| StockLadder | Caller-supplied stock ladders | Constrained-first fill with equivalent-demand area repacking |
| Default, Strip, Vertical Remnant, Horizontal Remnant | Single-strategy fills | The fixed placement strategies behind interactive fill |

Rectangles places irregular parts validly, but only as their bounding boxes; it never nests into a
notch or hole. Box sides account for how the layout check flattens arcs, so round-edged parts stay
valid at box contact.

Interactive/full-area box packing uses the same 90% work-area slack allowance as Rectangles.
A free box may absorb a slightly oversized side only at its right/top edge when that edge
coincides with the plate work-area boundary. Internal leftover edges keep the strict packing
tolerance, and actual part dimensions still determine spacing away from the plate boundary.

Irregular fills gaps and open notches using outer profiles; it does not yet place parts inside
enclosed cutouts. Concave no-fit polygons are prepared with a single boundary/containment union.
Any remaining numerical hole is filled only when its entire ring is certified to lie in forbidden
space, preserving genuine enclosed placement pockets without changing spacing tolerances.

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
