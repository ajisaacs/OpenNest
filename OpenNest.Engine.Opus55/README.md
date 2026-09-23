# OpenNest.Engine.Opus55

An independent whole-job `INestingEngine`: **frontier-advance NFP packing with look-ahead
stock selection**. It does not call, wrap, or select over any built-in engine
(`StockLadderNestingEngine`, `FixedStrategyNestingEngine` strategies, `PlateNesterFactory`,
`NestingEngineRegistry`), nor the removed `OpenNest.Engine/Nfp` bottom-left-fill/annealing code.
Every placement decision (which part, which rotation, where, on which sheet) comes from the logic below.

## Algorithm

**1. Geometry (`PartCatalog`, `NoFitCache`)**
- Each part's outer perimeter is polygonized with a known chord tolerance (0.002 by default,
  coarsened for arc-heavy parts until the outline is ≤ ~64 vertices, capped at 0.1% of part size).
- Candidate rotations come from the part's `RotationPolicy`: for `Automatic`, the four right
  angles plus the two orientations that axis-align the minimum-area bounding rectangle
  (`RotatingCalipers`); for sweeps, up to 8 evenly spaced legal steps. Point-symmetric duplicates are dropped.
- Each orientation gets a **footprint**: outline inflated (miter joins, so it contains the exact
  round offset) by `(spacing + 0.022) / 2 + chordTolerance`. Two parts respect the spacing
  when their footprints don't overlap. The 0.022 covers validators that polygonize arcs
  circumscribed at 0.01 per side, plus Clipper's 1e-4 grid.
- **No-fit polygons** between footprints come from Clipper2 Minkowski sums: an O(n+m)
  edge merge for convex pairs, and for concave pairs the boundary sweep ∪ (A + p₀) ∪ (−B + a₀).
  The last two terms cover "B inside A" and "B swallows A". NFPs are cached per orientation pair.

**2. Sheet filling (`FrontierPacker`)**
- For every (part type, orientation) still in play, the packer keeps the exact **free region** of
  legal reference points: the inner-fit rectangle minus the NFPs of everything placed. Each
  placement subtracts one translated NFP from each region (in parallel, which stays deterministic).
  Regions only shrink, and an empty region is retired for the rest of the sheet.
- At every step all remaining types × orientations compete (there is no fixed placement sequence):
  1. **Gap fill:** if any part fits without pushing the packing front forward, place the
     *largest* such part at its lowest point.
  2. **Advance:** otherwise place the part with the least front advance per `area^β`, i.e. the
     most material coverage for the sheet length it consumes.
- The front sweeps along X or Y, which leaves one full-width offcut strip for salvage credit.

**3. Whole job (`Opus55NestingEngine`, `SheetEconomics`)**
- Sheet by sheet, every available stock size is trial-filled. The trial with the lowest
  *estimated whole-job cost* (its net area, plus the remaining demand priced at the best
  efficiency any trial achieved) is committed. This lets a sheet that finishes the job beat a
  denser partial one.
- Net area = sheet area − `SalvageRate` × the largest qualifying full-width/full-length edge
  offcut. This is the objective the benchmark scores.
- Six strategy variants (front axis X/Y × β ∈ {1, 0.5, 1.5}) each run whole-job, and the cheapest
  plan wins (fewest unplaced, then cost, then sheets). A **tail re-plan** then re-decodes the
  parts on the last 1–3 sheets with each stock forced first, and keeps any strictly cheaper result.
- **Deterministic:** no clock or randomness affects decisions. Effort is capped by a
  count-based work budget (free-region subtractions), not wall time.

## Layout

| File | Role |
|---|---|
| `Opus55NestingEngine.cs` | `Solve()`: demand filtering, variants, stock look-ahead, tail re-plan, result assembly |
| `FrontierPacker.cs` | One-sheet fill: free regions and the gap-fill/advance choice rule |
| `NoFitCache.cs` | Spacing footprints and cached NFPs (Clipper2 Minkowski) |
| `PartCatalog.cs` | Snapshot → perimeter polygon per allowed orientation |
| `SheetEconomics.cs` | Net-area objective with salvage credit |
| `tests/` | xUnit suite. Layouts are judged by `OpenNest.Benchmark.NestValidator` |

## Build / test

```bash
dotnet build OpenNest.Engine.Opus55/OpenNest.Engine.Opus55.csproj -c Release
dotnet test  OpenNest.Engine.Opus55/tests/OpenNest.Engine.Opus55.Tests.csproj
```

This project is intentionally **outside** `OpenNest.sln`, the same pattern as the
`OpenNest.Engine.Aurora` plugin. It's discovered at runtime as a plugin.

## Benchmark

```bash
dotnet build OpenNest.Benchmark/OpenNest.Benchmark.csproj -c Release
mkdir -p OpenNest.Benchmark/bin/Release/net8.0/Engines
cp OpenNest.Engine.Opus55/bin/Release/net8.0/OpenNest.Engine.Opus55.dll OpenNest.Benchmark/bin/Release/net8.0/Engines/
dotnet OpenNest.Benchmark/bin/Release/net8.0/OpenNest.Benchmark.dll <path-to-.nest-or-manifest-or-folder>
```

The engine reports as `Opus55NestingEngine`.

## Known limitations

- **No part-in-part:** holes are treated as solid, so small parts never nest inside cutouts.
- **Clearance padding:** gaps are ~0.022 (plus up to the chord tolerance) wider than the
  required spacing, to stay valid under circumscribed-polygon validators. That's negligible in mm
  and about 0.02" in inches. The constants are absolute and assume job units near inch/mm scale.
- **Rotation coverage:** `Automatic` parts try at most 8 orientations (fewer when a job has many
  distinct parts: `48 / partCount`, minimum 2). Free-angle rotations aren't explored beyond the MBR alignment.
- **Greedy core:** there is no order/permutation search. The variants and tail re-plan are the only
  search, and density on small mixed jobs trails what an interlocking-pair filler can reach.
- **`NestJobPart.Priority` is ignored**, and progress reports only `EvaluatingCandidate`
  per trial and `PlateCommitted` at the end, with no finer-grained progress.
- Parts whose geometry has no readable closed perimeter, or that fit no offered stock at any
  allowed rotation, are reported unplaced (`NoPlacementFound`) instead of failing the job.
