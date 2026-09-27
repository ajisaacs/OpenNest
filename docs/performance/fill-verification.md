# Fill performance verification

Opt-in synthetic measurements (`OpenNest.Tests/Fill/FillPerformanceTests.cs`):

```bash
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'Category=FillPerformance' --logger 'console;verbosity=detailed'
```

Only the exact value `1` enables these tests; otherwise they skip; [README](../../README.md) documents the PowerShell equivalent.

The category covers comparer, group-pattern, rotated-pattern, extents-column, feature-extraction, no-model angle, and FillLinear offset-geometry workloads; individual filters match benchmark method names in `FillPerformanceTests.cs`. Overlap checks are measured separately by `OverlapCheck_ReportsPolygonPairsAndGridChecks` in `OpenNest.Tests/Fill/OverlapCheckPerformanceTests.cs` (same category). Keep harness, inputs, warmups and batches identical before/after; exclude setup/assertions from timing. Comparer/extents allocations are synchronous and current-thread only; parallel group fills omit allocation totals. No timing CI gates or whole-job speedup claims. Preserve evidence in [the measured report](fill-performance.md).

Debug behavior/skipped-work checks:

```bash
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~DefaultFillComparerWorkTests|FullyQualifiedName~FillHelpersTests|FullyQualifiedName~FillExtentsTests|FullyQualifiedName~StrategyOverlapTests|FullyQualifiedName~FillLinearGeometryReuseTests|FullyQualifiedName~CollisionOverlapOnlyTests|FullyQualifiedName~PartOverlapCheckerTests'
```

`PerfCounters.FillScoreComputations`, `PartBoundaryPreparations`, `PartBoundsUpdates`, `OffsetPerimeterEntities`, `FeatureBitmaskCells`, `CrossingPointScans`, and `OverlapPolygonPreparations` increments compile away in Release: zero Release counters prove nothing. `OverlapPolygonPreparations` counts overlap-preparation starts (material extraction), not completed polygons: `Part.Intersects` counts both parts on every call, `PartOverlapChecker` counts once per distinct `Program`. Serialize counter assertions in `FillCacheCollection` and reset in `finally`. Keep `OpenNest.Tests/Fill/LegacyFillExtents.cs`, `OpenNest.Tests/Fill/LegacyFillLinear.cs`, `OpenNest.Tests/Geometry/LegacyCollision.cs`, and `OpenNest.Tests/Fill/LegacyPartOverlap.cs` frozen for differential tests (never route them through production helpers), not production or before timings; measure the actual baseline production code.

Task 4b checks: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter "FullyQualifiedName~AngleCandidateBuilderTests|FullyQualifiedName~AnglePredictorTests|FullyQualifiedName~FeatureExtractorTests"` (repeat in Debug for bitmap counters). `IrregularAngles_ReportsWarmNoModelPath` measures the public builder with a missing model and skips when a model is installed; never remove real model files to benchmark. `FeatureExtraction_ReportsFullAndScalarOnly` measures extraction separately.

Predictor availability uses the same one-attempt session initialization as inference. Publish completion only after assignment or definitive failure; concurrent callers must wait for the outcome. The builder skips extraction when unavailable and requests scalar-only features when available. Tests use isolated loaders/prediction doubles, not evidence of real ONNX inference.

Whole-job before/after comparisons use `OpenNest.Benchmark` with a `*.manifest.json` corpus and `--parallel 1` (see the report's Task 5 section for the delivered real-DXF manifest, hashes, outcome confirmation, and inconclusive whole-job timing). Circle-heavy archive drawings can validate INVALID at spacing even on the pre-batch baseline, and larger quantities can crash both trees identically; record such pre-existing behaviors instead of treating them as regressions or tuning around them.
