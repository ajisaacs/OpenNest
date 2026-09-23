# OpenNest.Engine.Terra

An independent `INestingEngine` implementation — **not** a wrapper, ensemble, or
selector over OpenNest's built-in engines (`StockLadderNestingEngine`,
`FixedStrategyNestingEngine` "Default"/"Strip"/"Vertical Remnant"/"Horizontal Remnant`,
or anything reachable through `PlateNesterFactory`/`NestingEngineRegistry`).
`Solve()` must never call, instantiate, or otherwise delegate a placement decision
to one of those.

## Allowed building blocks

Low-level geometry/data-structure primitives are fair game — they are not nesting
strategies:

- `OpenNest.Core` geometry: `Polygon`, `Shape`, `BoundingBox`, `Vector`, `Box`,
  `ConvexHull`, `ConvexDecomposition`, `RotatingCalipers`, `Collision` (overlap/spacing
  checks), `NoFitPolygon`, `ShapeProfile`, `SpatialQuery`.
- `OpenNest.Engine` support types if useful: `PartBoundary`, `RotationAnalysis`,
  `AngleCandidateBuilder` — the *decision logic* using them must be your own (don't just
  call `BestFitFinder`/`PairEvaluator`/`RotationSlideStrategy`, which are the existing
  best-fit engine's internals).

## What to fill in

`TerraNestingEngine.cs` — implement `Solve()`. Pick and document an actual
placement strategy (NFP-based sliding placement, skyline/shelf packer,
simulated-annealing/genetic layout search, guillotine-cut packer,
physics/gravity-settling, etc). It's fine to be simpler or worse than the built-in
engines to start; it must not be the same algorithm re-derived through indirection.

## Build

```bash
dotnet build Engines/OpenNest.Engine.Terra/OpenNest.Engine.Terra.csproj
```

This project is intentionally **outside** `OpenNest.sln` (same pattern as the
`OpenNest.Engine.Aurora` plugin) — it's discovered at runtime as a plugin, not built
as part of the main solution.

## Try it out with the benchmark

`OpenNest.Benchmark` auto-loads plugin engines from an `Engines/` folder next to its
own build output:

```bash
dotnet build Engines/OpenNest.Engine.Terra/OpenNest.Engine.Terra.csproj -c Release
dotnet build OpenNest.Benchmark/OpenNest.Benchmark.csproj -c Release

mkdir -p OpenNest.Benchmark/bin/Release/net8.0/Engines
cp Engines/OpenNest.Engine.Terra/bin/Release/net8.0/OpenNest.Engine.Terra.dll    OpenNest.Benchmark/bin/Release/net8.0/Engines/

dotnet OpenNest.Benchmark/bin/Release/net8.0/OpenNest.Benchmark.dll <path-to-.nest-or-folder>
```

Or build and deploy in one step with `./Engines/Build-Engines.ps1 -Engines Terra`.

Your engine will show up in the report under its CLR type name (`TerraNestingEngine`),
competing on equal footing against the built-in engines.
