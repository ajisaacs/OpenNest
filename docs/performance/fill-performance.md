# Fill performance measurements

## Count-first comparer slice — 2026-09-25

This report covers only the count-first `DefaultFillComparer` change. Geometry, custom-comparer score elimination, ML work, and whole-job optimization have not been implemented or measured here.

### Source and environment

Final staged review corrected two delegate declarations to the repository's `var` style. The complete original/optimized measurement pair was then rerun with that identical final harness; all timing tables below are from those final runs, not mixed with earlier samples. The original production comparer was temporarily restored only for the before measurement and reinstated in `finally`.

- Baseline: `1b862dc1a896df502935b900094c15c3300999c5` on `master`.
- Optimized source: the count-first implementation delivered with this report; use `git log -1 --format=%H -- OpenNest.Engine/Fill/DefaultFillComparer.cs` to resolve its implementation commit.
- The original comparer was measured before adding the fast path. Tests and the Debug-only diagnostic were already present; the diagnostic call is compiled out in Release.
- Original comparer SHA-256: `8d436b88831d3187b319d52084131773f998c94ff4a227262cf15e4d27013b8f`.
- Optimized comparer SHA-256: `2444d30ddc92fa662f5ad50ac2cca0a7c50da3aee15f0fc932d58cf64023333d`.
- Identical before/after measurement harness SHA-256: `6a3b911748e31027bab6922d9d54f89b6fd7257884e1eecc5461db032089c7bf` (`OpenNest.Tests/Fill/FillPerformanceTests.cs`).
- Machine: `hermes`, KVM guest, 4 vCPUs presented as AMD Ryzen 9 5900X; Ubuntu 24.04.5 LTS x64; Linux 6.8.0-142-generic. No CPU pinning.
- Build SDK: 10.0.112. Measured runtime, emitted by the test: .NET 8.0.31; Release; stopwatch frequency 1,000,000,000 ticks/second.

### Workload and timing scope

All inputs are fixture-independent, finite, nonoverlapping 2×1 rectangles inside a `(0,0,256,256)` work area, arranged in 64 columns. The larger layout has 2,048 parts at pitch 4; the smaller layout has 2,047 at pitch 3 (higher density despite lower count). The equal-count control compares 2,048 parts at pitch 3 against the larger layout.

Each implementation gets two warmup batches of 50,000 calls per case, then seven measured batches. Unequal-count batches contain 500,000 calls; equal-count batches contain 10,000. Argument order alternates each call, with half forward and half reverse. Actual/reference batch order alternates between repetitions. The reference is the prior scoring expression, `FillScore.Compute(a, area) > FillScore.Compute(b, area)`, not a duplicate comparer class.

Inputs, cached drawing areas/bounds, and JIT paths are warm. There is no fill-cache operation or cache reset in this workload. Construction, layout validation, correctness assertions, and output are outside timing. Delegate/loop/result-consumption overhead and JIT optimization are included. Returned true counts are checked after each batch.

Allocated bytes are measured around synchronous work with `GC.GetAllocatedBytesForCurrentThread`, not process-wide memory or RSS. Every batch of both implementations, before and after, reported zero allocated managed bytes on the measured thread.

### Results

Nanoseconds per call, minimum / median / maximum over seven batches:

| Case | Implementation | Before | After |
| --- | --- | --- | --- |
| unequal-counts | Comparer | 11901.864 / 11979.646 / 12194.228 | 1.800 / 1.878 / 3.221 |
| unequal-counts | Reference expression | 11654.151 / 12085.460 / 12269.212 | 12093.576 / 12247.735 / 12419.104 |
| equal-count-control | Comparer | 11617.457 / 12455.571 / 12814.497 | 11874.469 / 12372.208 / 12666.589 |
| equal-count-control | Reference expression | 11576.379 / 12344.544 / 12983.099 | 11736.897 / 12565.047 / 12864.353 |

The unequal-count median batch decreased from 5,989.823190 ms to 0.939182 ms. Their ratio is approximately 6,378× for this warm, repeated local-operation workload only. The optimized path is close to loop/JIT overhead; its nanosecond estimate is not an isolated method-latency guarantee. The unchanged reference varied slightly between processes, illustrating host/runtime variability. Do not extrapolate the ratio to nesting jobs.

Equal-count medians remained in the same range as the reference; these runs show no material regression, not evidence of an equal-count optimization. No elapsed-time thresholds are enforced in CI.

### Behavioral and work-removal evidence

- Characterization covers null/empty guard order, lower-count rejection, count winning against density, both density directions for equal counts, and exact ties retaining the current result.
- A deterministic matrix of 33 valid layouts checks all 1,089 ordered pairs against the reference expression, including empty, self, translated, and reordered cases. It snapshots caller part/drawing/program identities, positions, rotations, bounds, and motion endpoints.
- A Debug-only `PerfCounters.FillScoreComputations` diagnostic follows the existing conditional counter pattern. Concrete lists and nonvirtual getters do not offer a practical direct scan spy without broader production changes.
- Before the fast path, both unequal-count work assertions failed with **expected 0, actual 2** score computations; the other three work cases passed. Afterward, all five passed: unequal/empty cases compute no scores, while nonempty equal-count cases compute both scores. Counter tests run in the existing nonparallel `FillCacheCollection` and reset counters in `finally`.
- Debug instrumentation does not establish Release behavior by reading zero counters; Release evidence is the compiled-out call convention, source fast path, equivalent outputs, and timings above.

### Test results

| Run | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Original full main suite, Release | 1,140 | 12 | 0 |
| Original full engine suite, Release | 300 | 0 | 0 |
| Final full main suite, Release | 1,154 | 13 | 0 |
| Final full main suite, Debug | 1,159 | 13 | 0 |
| Final full engine suite, Release | 300 | 0 | 0 |

The 12 original skips are optional `ChrFontTests`: `ChrFontPath not configured in test-config.json or file not found`. The additional final skip is the opt-in performance test. The five extra Debug cases are the score-work tests. The performance test passed with the opt-in value `1`, and was separately verified to skip when unset and when set to `0`.

The implementer's broader comparer/score selection passed 28 Release tests before and after, and 33 Debug tests afterward (it also selected `NestProgressTests.BestDensity_MatchesFillScoreFormula`). Parent re-verification using the plan's narrower `FillComparerTests|FillScoreTests` filter passed 27 Release tests, and the five Debug work tests passed separately. Existing unrelated compiler warnings remain; no failures are hidden as baseline bugs. Source formatting and `git diff --check` passed.

### Reproduction

```bash
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'Category=FillPerformance' --logger 'console;verbosity=detailed'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~DefaultFillComparerWorkTests'
```

Normal suite runs skip the measurement. The installed Xunit.SkippableFact 1.4.13 names its inverse skip predicate `Skip.IfNot`, not `Skip.Unless`; no package change was needed. README documents both shell and PowerShell invocation.

The corresponding `CLAUDE.md` workflow edit was denied by the protected-file permission gate. That file was left unchanged; this documentation sync remains blocked pending explicit approval. No retry or alternate write was attempted.

No Windows UI runtime tests, actual ONNX inference, representative production corpus, or whole-job benchmark was run for this slice. These results do not complete the broader initial optimization batch.

### Raw measured batches

Times in milliseconds; allocations were 0 bytes for every actual/reference batch below. Preserved from matching raw TRX and console outputs before removing temporary test artifacts.

| State | Case | Batch | Comparer ms | Reference ms |
| --- | --- | ---: | ---: | ---: |
| pre | unequal-counts | 1 | 6097.114079 | 6093.454749 |
| pre | unequal-counts | 2 | 5950.931964 | 6031.795978 |
| pre | unequal-counts | 3 | 6010.884370 | 5827.075456 |
| pre | unequal-counts | 4 | 5989.823190 | 6019.619401 |
| pre | unequal-counts | 5 | 5953.063588 | 6134.606110 |
| pre | unequal-counts | 6 | 6051.466272 | 6042.729937 |
| pre | unequal-counts | 7 | 5973.554301 | 6083.705108 |
| pre | equal-count-control | 1 | 126.868005 | 129.830987 |
| pre | equal-count-control | 2 | 124.555712 | 128.105601 |
| pre | equal-count-control | 3 | 125.615121 | 124.509854 |
| pre | equal-count-control | 4 | 128.144975 | 120.288678 |
| pre | equal-count-control | 5 | 121.787519 | 123.445436 |
| pre | equal-count-control | 6 | 116.562527 | 115.763791 |
| pre | equal-count-control | 7 | 116.174565 | 118.153899 |
| post | unequal-counts | 1 | 0.931488 | 6123.867689 |
| post | unequal-counts | 2 | 0.933221 | 6046.787840 |
| post | unequal-counts | 3 | 0.939182 | 6209.551894 |
| post | unequal-counts | 4 | 1.610389 | 6120.239321 |
| post | unequal-counts | 5 | 1.210914 | 6145.847134 |
| post | unequal-counts | 6 | 0.945585 | 6160.595776 |
| post | unequal-counts | 7 | 0.899788 | 6097.613482 |
| post | equal-count-control | 1 | 122.282550 | 121.856075 |
| post | equal-count-control | 2 | 124.576228 | 123.526717 |
| post | equal-count-control | 3 | 123.722076 | 125.650465 |
| post | equal-count-control | 4 | 126.342491 | 128.643534 |
| post | equal-count-control | 5 | 126.665891 | 125.683547 |
| post | equal-count-control | 6 | 118.822360 | 126.622891 |
| post | equal-count-control | 7 | 118.744694 | 117.368968 |
