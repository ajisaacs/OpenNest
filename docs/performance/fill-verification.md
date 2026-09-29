# Fill performance verification

Opt-in synthetic measurements (`OpenNest.Tests/Fill/FillPerformanceTests.cs`):

```bash
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'Category=FillPerformance' --logger 'console;verbosity=detailed'
```

Only the exact value `1` enables these tests; otherwise they skip. In PowerShell, set `$env:OPENNEST_RUN_FILL_PERF = '1'` before the same `dotnet test` command, then run `Remove-Item Env:OPENNEST_RUN_FILL_PERF` afterward.

The category covers comparer, group-pattern, rotated-pattern, extents-column, feature-extraction, no-model angle, and FillLinear offset-geometry workloads; individual filters match benchmark method names in `FillPerformanceTests.cs`. Overlap checks are measured separately by `OverlapCheck_ReportsPolygonPairsAndGridChecks` in `OpenNest.Tests/Fill/OverlapCheckPerformanceTests.cs` (same category). Keep harness, inputs, warmups and batches identical before/after; exclude setup/assertions from timing. Comparer/extents allocations are synchronous and current-thread only; parallel group fills omit allocation totals. No timing CI gates or whole-job speedup claims. Keep raw results and run-specific reports outside source control; this guide documents the reusable verification procedure.

### FillLinear unchanged-row validation baseline

```bash
# Both configurations must characterize unchanged production; unset perf flag skips.
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~FillLinearValidation' --logger 'trx;LogFileName=linear-validation-debug.trx'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~FillLinearValidation' --logger 'trx;LogFileName=linear-validation-release.trx'
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~FillLinearValidation_ReportsStripeAndControls' \
  --logger 'console;verbosity=detailed' --logger 'trx;LogFileName=linear-validation-perf.trx'
```

`FillLinearValidationPerformanceTests.cs` is self-contained against base APIs: byte-copy and hash the same file in both trees. It times complete production `Fill` calls (not frozen code or overlap-only helpers): horizontal/vertical stripes, full grid, partial-only additions, and single-seed stripe. The closed concave fixture gives counts **8 / 19 / 36 / 29 / 8**; grid is row 8 + 28 additions, partial-only is row 19 + 10 (no full row). Two 100-call warmups and seven 200-call batches per mode alternate forward/reverse mode order. Retain every raw tick/time/current-thread allocation row, including warmups; setup/assertions/output are excluded, count/last-result consumption is identical. No timing or reduced-work assertions belong in this baseline harness.

`PreStep3FillLinear.cs` freezes production `FillLinear` at **1b23ad79f25d77fdd745bfea029de9dff2a91f6d**, separately from the older `LegacyFillLinear`. Reverse only the documented type/constructor rename, namespace/visibility/import additions, nullable directive and import formatting; retain a zero-diff comparison against `git show <base>:OpenNest.Engine/Fill/FillLinear.cs` and SHA-256 hashes. Do not refactor the oracle or use it for before timings. `FillLinearValidationReuseTests` compares ordered IEEE-754 poses/bounds, CNC values/program sharing, drawing references and immutable inputs through both public `Fill` overloads, including concurrent calls on one filler. Valid fixtures assert closed positive-area material and no overlap. The separate invalid overlapping-seed case intentionally remains invalid; in Debug its listener confirms Step1 and Step2 fallback pair `(0,1)` with six parts at each step (zero perpendicular additions). It makes no claim that bbox fallback repairs the seeds.

Keep future validation-work counter assertions Debug-only in `FillCacheCollection`, reset counters in `finally`, and retain the invalid fallback and single-seed/partial-only controls. This baseline does not assert a future skipped check or introduce production counters. Whole-job exact preservation needs a separately repeated serial oracle (fresh process, `DOTNET_PROCESSOR_COUNT=1`, verified one-worker thread-pool cap, solve on that worker); ordinary parallel layouts can differ on the same tree, and the serial gate does not replace concurrent differential tests.

Debug behavior/skipped-work checks:

```bash
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~DefaultFillComparerWorkTests|FullyQualifiedName~FillHelpersTests|FullyQualifiedName~FillExtentsTests|FullyQualifiedName~StrategyOverlapTests|FullyQualifiedName~FillLinearGeometryReuseTests|FullyQualifiedName~CollisionOverlapOnlyTests|FullyQualifiedName~PartOverlapCheckerTests'
```

`PerfCounters.FillScoreComputations`, `PartBoundaryPreparations`, `PartBoundsUpdates`, `OffsetPerimeterEntities`, `FeatureBitmaskCells`, `CrossingPointScans`, `OverlapPolygonPreparations`, and `PolygonTriangulations` increments compile away in Release: zero Release counters prove nothing. `OverlapPolygonPreparations` counts overlap-preparation starts (material extraction), not completed polygons: `Part.Intersects` counts both parts on every call, `PartOverlapChecker` counts once per distinct `Program`. `PolygonTriangulations` counts `Collision` triangulations; the checker triangulates a part at most once per check, and only after a bounding-box hit. Serialize counter assertions in `FillCacheCollection` and reset in `finally`. Keep `OpenNest.Tests/Fill/LegacyFillExtents.cs`, `OpenNest.Tests/Fill/LegacyFillLinear.cs`, `OpenNest.Tests/Geometry/LegacyCollision.cs`, and `OpenNest.Tests/Fill/LegacyPartOverlap.cs` frozen for differential tests (never route them through production helpers), not production or before timings; measure the actual baseline production code.

Predictor regression checks: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter "FullyQualifiedName~AngleCandidateBuilderTests|FullyQualifiedName~AnglePredictorTests|FullyQualifiedName~FeatureExtractorTests"` (repeat in Debug for bitmap counters). `IrregularAngles_ReportsWarmNoModelPath` measures the public builder with a missing model and skips when a model is installed; never remove real model files to benchmark. `FeatureExtraction_ReportsFullAndScalarOnly` measures extraction separately.

Predictor availability uses the same one-attempt session initialization as inference. Publish completion only after assignment or definitive failure; concurrent callers must wait for the outcome. The builder skips extraction when unavailable and requests scalar-only features when available. Tests use isolated loaders/prediction doubles, not evidence of real ONNX inference.

Whole-job before/after comparisons use `OpenNest.Benchmark` with a `*.manifest.json` corpus and `--parallel 1`. Retain input hashes, exact commands, raw results, validity, fulfillment and cost for both revisions outside source control. Record baseline failures rather than treating them as regressions or tuning the corpus around them; overlapping timing ranges are inconclusive, not proof of unchanged performance.
