# OpenNest.Engine.Terra

An independent `INestingEngine` implementation. It must not be a wrapper, ensemble, or
selector over OpenNest's built-in engines. `Solve()` must not call, instantiate, or
delegate to any existing `INestingEngine` (`StockLadderNestingEngine`,
`FixedStrategyNestingEngine`), `NestingEngineRegistry`, `NestJobRunner`, or the whole-plate
nesters/fillers behind `PlateNesterFactory` (`DefaultPlateNester`, `StripPlateNester`,
`RemnantPlateNester`, `PlateFillService`, `DefaultPlateFiller`, ...). It must also never run
several of them and keep the best result.

The decisions that make it an engine must be yours: which sheet(s) to use, which parts go
where and in what order, which pattern/strategy to apply to which region, and when to stop.

## Allowed building blocks

Reuse is encouraged. These are tools you drive, composed by your own decision logic:

- `OpenNest.Core` geometry: `Polygon`, `Shape`, `BoundingBox`, `Vector`, `Box`, `ConvexHull`,
  `ConvexDecomposition`, `RotatingCalipers`, `Collision`, `NoFitPolygon`, `ShapeProfile`,
  `SpatialQuery`.
- Fill and pattern components in `OpenNest.Engine.Fill`: `FillLinear`, `FillExtents`,
  `PairFiller`, `ShrinkFiller`, `RemnantFiller`/`RemnantFinder`, `Compactor`, `FillScore`,
  `Pattern`/`PatternTiler`, `PartBoundary`, `RotationAnalysis`, `AngleCandidateBuilder`,
  `BestCombination`.
- `OpenNest.Engine.BestFit` (`BestFitFinder`, `PairEvaluator`, ...), `RectanglePacking`,
  `CirclePacking`.

If you find a faster or better way to do something a shared component already does (for
example linear patterning), implement it inside this engine's own project and leave the
shared code untouched. Do not edit `OpenNest.Core` or `OpenNest.Engine`. Call it out in your
report (what it replaces, why it is better, measured numbers) so it can be generalized and
upstreamed for every engine later.

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
