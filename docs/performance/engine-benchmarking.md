# Benchmarking whole-job engines

`OpenNest.Benchmark` runs each registered `INestingEngine` against `.nest` files or JSON manifests of DXFs and quantities. It ranks complete jobs first, then salvage-credited sheet cost with a penalty for each unplaced part. Invalid runs (bounds, spacing, quantity, rotation, or stock mismatch) place nothing and pay the penalty.

```bash
dotnet run --project OpenNest.Benchmark -- ./benchmark-jobs \
  --sheet-sizes 48x96,60x120,72x120 --engines Irregular,Rectangles --csv results.csv
```

Supply `--sheet-sizes` for an independent candidate pool; otherwise a saved nest offers only its original sheet sizes, which can bias comparisons. The default `--parallel 3` runs solves concurrently and confounds per-engine timing; use `--parallel 1` for timing comparisons. `--progress` logs each solve's start, engine progress, and finish. Retain input hashes, exact commands, validity, fulfillment, costs, and raw results outside source control when comparing revisions; see [fill performance verification](fill-verification.md).

Custom engines are DLLs implementing `INestingEngine` with a public parameterless constructor, loaded from `Engines/` beside the benchmark executable. See [nesting engines](../nesting-engines.md) for built-ins and engine-change rules. Run `dotnet run --project OpenNest.Benchmark -- --help` for all options.
