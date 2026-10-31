# Benchmarking whole-job engines

`OpenNest.Benchmark` runs each registered `INestingEngine` against `.nest` files or JSON manifests of DXFs and quantities. It ranks complete jobs first, then salvage-credited sheet cost with a penalty for each unplaced part. Invalid runs (bounds, spacing, quantity, rotation, or stock mismatch) place nothing and pay the penalty.

```bash
dotnet run --project OpenNest.Benchmark -- ./benchmark-jobs \
  --sheet-sizes 48x96,60x120,72x120 --engines Irregular,Rectangles --csv results.csv
```

Supply `--sheet-sizes` for an independent candidate pool; otherwise a saved nest offers only its original sheet sizes, which can bias comparisons. A saved nest's first plate also supplies spacing, quadrant and clamps: its machine clamp rail is resolved for every candidate size, as desktop Auto Nest does, and its baseline layout is checked against the clamps it was made with. Manifests have no clamps. A manifest part allows quarter turns unless `"allowRotation": false` locks it at 0°, or `"rotations"` lists its angles in degrees: `[0]`, `[0, 180]` or `[0, 90, 180, 270]`. A saved nest records its salvage rate, but salvage credit applies only with a positive `--min-salvage-dimension`. Without it, every complete one-sheet layout costs the same, as in desktop Auto Nest on a single plate size. The default `--parallel 3` runs solves concurrently and confounds per-engine timing; use `--parallel 1` for timing comparisons. `--progress` logs each solve's start, engine progress, and finish. `--time-limit <seconds>` stops each solve at that wall-clock limit and scores the best valid plan the engine had reported (see [time limits](../nesting-engines.md#time-limits)); a solve with none counts as timed out. Without a limit the harness abandons a solve after five minutes; with one it waits for the limit plus a minute, so the engine's limit always fires first. Retain input hashes, exact commands, validity, fulfillment, costs, and raw results outside source control when comparing revisions; see [fill performance verification](fill-verification.md).

Custom engines are DLLs implementing `INestingEngine` with a public parameterless constructor, loaded from `Engines/` beside the benchmark executable. See [nesting engines](../nesting-engines.md) for built-ins and engine-change rules. Run `dotnet run --project OpenNest.Benchmark -- --help` for all options.
