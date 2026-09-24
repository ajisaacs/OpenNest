# Astra development benchmark report

Measured locally on Linux with .NET SDK 8.0.425, Release builds, 2026-09-23.
The shared-validator fix was verified on 2026-09-24; its results are recorded separately below.
The baseline is Astra's original independent guillotine/bounding-rectangle implementation,
archived before the contact-search rewrite. These are not measurements against Opus or a
claim of performance on an unseen competition dataset.

## Synthetic cases

Both versions were run on exactly the same programmatically generated geometry and stock.
The driver validates materialized output with `OpenNest.Benchmark.NestValidator`, including
quantity, stock settings, rotation, material overlap and spacing. Timings cover `Solve` only,
exclude external validation, and are single-run observations rather than stable distributions.
Every contact result is valid and complete. The baseline is valid but incomplete on `plate-cap`.

| Case | Baseline area | Contact area | Change | Contact time (ms) |
|---|---:|---:|---:|---:|
| triangles | 8064 | 4608 | -42.9% | 3354 |
| circles | 3456 | 3456 | 0.0% | 1021 |
| circles-dense | 3456 | 2304 | -33.3% | 742 |
| concave-L | 5760 | 3456 | -40.0% | 1055 |
| mixed | 4608 | 3456 | -25.0% | 1990 |
| holes | 2304 | 1152 | -50.0% | 306 |
| rectangles | 3456 | 3456 | 0.0% | 261 |
| grain | 4608 | 2304 | -50.0% | 527 |
| scarce-stock | 228 | 228 | 0.0% | 1 |
| tail | 600 | 600 | 0.0% | 4 |
| plate-cap | 100 | 400 | 4/10 → 10/10 placed | 4 |

Excluding `plate-cap`, where baseline completion differs, purchased area fell from 36540
to 25020: **31.5% less area** across these ten cases. The original solver
usually took 0–30 ms; contact search takes approximately 1 ms to 3.4 s on this set. Packing
quality improved at a substantial CPU cost. No speedup over the original baseline is claimed.

## Extended generated cases

`GeneratedCases.cs` adds 23 jobs: rounded rectangles, T-shapes, trapezoids, pentagons,
hexagons, rings, pipe flanges, ring/insert mixtures, curved C-shapes, narrow U-shapes,
stars, and 12 seeded mixed jobs. These exercise all four quadrants, asymmetric edge margins,
finite small-sheet inventory, translated source origins, zero/positive spacing, fixed
non-cardinal rotations and bounded sweeps. The library supplies most shapes; the C, U and
star contours are generated directly. Seeded cases use seeds 19073 through 19084.

Both versions place every requested part in all 23 jobs with valid output. Seven cases use
less purchased area; the other sixteen match the baseline. Aggregate area drops from
58259 to 44353, **23.9% less area**. The changed cases are:

| Generated case | Baseline area | Contact area | Reduction |
|---|---:|---:|---:|
| T-shapes | 5917 | 2016 | 65.9% |
| Hexagons | 2160 | 1080 | 50.0% |
| Pipe flanges | 1932 | 966 | 50.0% |
| Curved C-shapes | 6051 | 2150 | 64.5% |
| Narrow U-shapes | 5925 | 3901 | 34.2% |
| Seed 19083 | 1968 | 984 | 50.0% |
| Seed 19084 | 2100 | 1050 | 50.0% |

The initial fine-mesh curved-C search exceeded the driver's 90-second cancellation budget
(an in-flight Minkowski operation delayed cancellation to 110 seconds). Separately coarsening
its contact outline, padding both approximation errors, and retaining fine safety geometry
reduced that case to approximately 2.3–2.5 seconds. No wall-clock cutoff was added to the engine.
The final regression pass validates all 34 generated/synthetic jobs and all four DXF jobs;
27 independent xUnit cases also pass. Raw results are in `results/`.

## Curved-hole validator fix (2026-09-24)

The generated ring/insert job exposed a shared-validator false positive. A ring with inner
radius 3.5 containing a concentric radius-3 disk has 0.5 units of clearance, yet the host's
triangulated collision check can report a violation with required spacing 0.175. The outcome
also changes with translations. Checking every candidate against that routine made a small
ring job take approximately 80–90 seconds.

Astra initially reserved curved cutouts as solid during placement, preserving the original
drawing in output. This workaround finished the ring/insert job in about 50 ms at the baseline
sheet cost. It has now been removed following a fix in `OpenNest.Core/Geometry/Collision.cs`.

Hole subtraction previously clipped each fragment independently against every triangle edge,
duplicating surviving area. It also classified points with an epsilon-shifted boundary but
intersected against the unshifted line, which could extrapolate outside the source segment.
The corrected routine emits disjoint outside fragments and carries the inside remainder to
the next edge. Classification and interpolation use the same signed cross products. Exact
closing vertices and local-coordinate area checks avoid additional small-fragment errors.
This remains the shared hand-written collision algorithm; Clipper is only an independent
oracle in the new tests, not a replacement per-pair validator.

The eight original translated reproductions all pass. Regression coverage also rejects real
spacing violations, checks both operand orders and windings, exercises all four quadrants,
and compares overlap areas against Clipper on 80 seeded pairs with multiple/concave holes.
The main suite passes 1,096 tests (12 font-fixture skips), Engine passes 170, and Astra passes
27. Astra's curved-hole tests now require a ring and insert to share stock whose usable area
fits only the ring, proving that insertion is enabled.

All 34 synthetic/generated jobs remain valid and complete with unchanged sheet-area costs.
The ring/insert job with hole search enabled takes about 4.8 seconds in this run and still
uses two small sheets. This fix improves validity and enables insertion; it does not improve
that job's stock plan. Results are in `results/validator-fixed-synthetic-generated.csv`.
All four repository-DXF jobs also remain valid and complete at unchanged sheet-area costs;
their rerun is recorded in `results/validator-fixed-dxf.csv`.

The isolated reproduction does not invoke any nesting engine:

```bash
dotnet run --project Engines/OpenNest.Engine.Astra/benchmarks -c Release -- --diagnose-ring
```

`results/ring-validator-reproducer.txt` retains the original failures;
`results/ring-validator-fixed.txt` records the corrected outcomes. Rebuild the host's Core
dependency when deploying. The benchmark validator's spacing rules and source are unchanged.

## Repository DXFs

The four manifests under `dxf/` use PT45, PT23 and PT11 repository drawings. Every result from
both versions was valid and complete. Runs used `--parallel 1`.

| Manifest | Baseline area | Contact area | Change |
|---|---:|---:|---:|
| locked.manifest | 115200 | 115200 | 0.0% |
| mixed.manifest | 144000 | 115200 | -20.0% |
| original.manifest | 115200 | 115200 | 0.0% |
| volume.manifest | 374400 | 374400 | 0.0% |

The mixed three-drawing job improves 20%; the other three retain baseline sheet-area cost.
The initial contact version regressed on `volume`; preserving a high-progress beam state and
ranking with observed per-part delivery cost removed that regression. Final results purchase
720000 area units versus 748800, a 3.8% reduction across the four manifests. Fewer physical
sheets sometimes have the same purchased area; those are not counted as area savings.

An exploratory run of the original manifest against StockLadder and Default found StockLadder
valid/complete at the same 115200 area cost; Default's result was flagged for spacing. That
single case does not establish general superiority. There is no Opus result available here.

## Local geometry optimizations

Apart from the shared Core collision fix described above, these optimizations are in Astra.
Astra caches NFPs and incrementally
subtracts each newly placed part from available translation regions, avoiding repeated unions
of all previous obstacles. A four-vertex rectangle configuration-space specialization avoids
round-offset polygons and polygon collision work for exact axis-aligned rectangle contacts.
During development, the 96-rectangle case dropped from roughly 1.5 s to 0.24 s after this
specialization. This is an end-to-end observation, not an isolated component microbenchmark.

Precision regression tests cover exact clearances and rotated zero-spacing contacts. In the
latter case, the host's four-decimal polygon rounding and triangulated collision test can
reject a contact accepted by Clipper at six decimals. Astra now retains the original rotated
frame for that validation, checks zero-clearance contacts with Core's collision primitive,
and tries tiny nearby translations when exact contact is unsafe.

## Reproduce

Run from the repository root:

```bash
dotnet run --project Engines/OpenNest.Engine.Astra/benchmarks -c Release
```

Run only the extended generated suite with `-- current generated`; the first positional
argument is either a previous plugin DLL or a label for the current build. Invalid layouts
and crashes make the driver exit with a nonzero status. Incompleteness is reported separately.

The standalone driver also accepts a prior plugin DLL and optional case-name filter:

```bash
dotnet run --project Engines/OpenNest.Engine.Astra/benchmarks -c Release -- /path/to/previous/OpenNest.Engine.Astra.dll triangles
```

An isolated assembly load context prevents .NET from silently substituting the currently
built plugin when comparing another version with the same assembly name. Historical baseline
CSV files are included; the old binary is not committed. The driver's 90-second cancellation
budget is a benchmark safeguard and is not an elapsed-time stopping rule inside the engine.

For real DXFs, build and deploy the plugin as described in the parent README, then:

```bash
dotnet OpenNest.Benchmark/bin/Release/net8.0/OpenNest.Benchmark.dll Engines/OpenNest.Engine.Astra/benchmarks/dxf --engines AstraNestingEngine --parallel 1 --csv /tmp/astra-dxf.csv
```

The CSV files under `results/` retain the measured results. Tests run independently:

```bash
dotnet test Engines/OpenNest.Engine.Astra/tests/OpenNest.Engine.Astra.Tests.csproj -c Release
```
