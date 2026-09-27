# Fill performance measurements

Documentation follow-up — 2026-09-25: the user explicitly approved the previously blocked instruction update, then chose `AGENTS.md` as the shared source of truth for multiple coding agents. `CLAUDE.md` imports it with `@AGENTS.md` for compatibility. The shared instructions document the delivered comparer/group/extents workflow, Debug work counters, measurement limitations, and safeguards already reflected in README. The instruction-document sync blocker is resolved; references to it in the historical slice records below describe their delivery-time status. No production code or measured results changed.

## Count-first comparer slice — 2026-09-25

This historical Task 1 section covers only the count-first `DefaultFillComparer` change. Geometry, custom-comparer score elimination, ML work, and whole-job optimization had not been implemented or measured in that slice. Task 1b is reported separately below.

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

## Task 1b — skip unused group-pattern scores — 2026-09-25

### Scope and provenance

Only the two candidate additions in `FillHelpers.FillPattern` changed: horizontal and vertical results compute `FillScore` when `comparer == null`, otherwise store `default`. The `ConcurrentBag`, angle scheduling, fill calls, result iteration, strict default-score comparison, and custom-comparer calls are unchanged. No geometry, bounds, scoring formula, ML, or Tasks 2+ optimization is included.

- Before source: `master` HEAD `b318950a543d2a6c4fd0b305e5e3ae2bbee3c6d9`, already containing Task 1. `FillHelpers.cs` was confirmed unchanged against HEAD before measurement. The actual unchanged production method was measured, not a reference approximation.
- Before `OpenNest.Engine/Strategies/FillHelpers.cs` SHA-256: `3f01f15b673d33626e49e06f4a381dc7331b33ec0b8850acf56dff4aa71e9198`.
- After `OpenNest.Engine/Strategies/FillHelpers.cs` SHA-256: `a1c8a1bc155dbe62439f8345a3a54e06147a5404a6a46835a30b66d52cceccfa`.
- Identical before/after `OpenNest.Tests/Fill/FillPerformanceTests.cs` SHA-256: `a5594148a2b11e37f5d47925c96774855eb622befb54b6736e98112ae8662873`. This extends the historical Task 1 harness; it does not replace its measurements above.
- Identical red/green `OpenNest.Tests/Strategies/FillHelpersTests.cs` SHA-256: `35fee6661bd23091708041b0dc7e3b0517ad859bbf05e3ead95082fe7025fed9`.
- Tracked C# source/project manifests before and after differ only in `FillHelpers.cs`; the new, untracked test file is covered separately by the hash above. There was no temporary production restoration or alternate implementation during these measurements.
- Host `hermes`: Ubuntu 24.04.5 LTS x64, Linux 6.8.0-142-generic, KVM guest, four vCPUs presented as AMD Ryzen 9 5900X. No CPU pinning or host isolation. SDK 10.0.112; test-emitted runtime .NET 8.0.31, Release, stopwatch frequency 1,000,000,000 ticks/second.

### Workload and method

The opt-in `GroupPattern_ReportsDefaultAndCustomComparer` test uses two finite, nonoverlapping synthetic rectangles: 2×1 at `(11,13)` and 1×2 at `(13.5,14.5)`. Work area is `(3,5,5,9)`, part spacing 0.25, and the only candidate angle is 0 radians. Horizontal fill returns eight parts; vertical returns seven. The default path selects horizontal. A custom comparer deliberately prefers fewer parts, selecting vertical against `FillScore`.

Each mode gets two warmup batches of 2,000 calls and seven measured batches of 20,000 calls. Default/custom order alternates in both warmup and measured repetitions. Before and after run in separate test processes using the same harness and inputs, with the two-line production change between them. Both run the real `Parallel.ForEach`/fill/bag/selection path. One angle avoids cross-worker bag tie-order ambiguity; it does not represent multi-angle parallel scaling.

Setup, correctness/layout assertions, and output are outside timing. Fill geometry, candidate construction, scheduling, garbage collection, scoring/selection, delegate/loop overhead, and result-count consumption are included. Each batch consumes every result count (160,000 default or 140,000 custom parts); its last result is checked for exact drawing identity, part order, locations, and rotations against direct horizontal/vertical fills. Preflight and batch checks establish finite, positive-area, in-bounds, nonoverlapping layouts. Behavioral tests separately check input non-mutation. No cache reset or forced collection is inserted between batches; JIT paths and source drawing data are warm, but each call still constructs its candidates.

**Allocation measurement is omitted for this parallel workload.** The current-thread allocation probe in the older synchronous comparer benchmark above would not measure all fill workers and must not be interpreted as total group-fill allocations.

### Results and limitations

Minimum / median / maximum over seven batches, calculated from raw TRX rows with Python `statistics.median`:

| Mode | Before batch ms | After batch ms | Before µs/call | After µs/call |
| --- | --- | --- | --- | --- |
| Default scoring | 12349.821045 / 12588.985384 / 12798.229355 | 12373.077829 / 12613.732653 / 12696.177150 | 617.491 / 629.449 / 639.911 | 618.654 / 630.687 / 634.809 |
| Custom fewer-parts comparer | 12430.689835 / 12733.580886 / 12944.521123 | 12537.190933 / 12624.207441 / 12905.258236 | 621.534 / 636.679 / 647.226 | 626.860 / 631.210 / 645.263 |

The custom-comparer median decreased **0.86%**, while the default control increased **0.20%**. Before/after ranges overlap. These sequential process measurements on a shared VM do **not** establish a reliable fill-time speedup. The benchmark includes geometry and scheduling, not isolated score latency, and its small candidate lists limit the amount of removed work. The concrete result is removal of two unused score computations per successful single-angle custom-comparer call, established by Debug diagnostics below. A custom comparer may still compute any scores it requires.

This is a local group-fill microbenchmark, not a whole-job speedup estimate, production-corpus result, multi-angle throughput test, Windows UI runtime test, or ONNX inference test. There are no timing gates. The full main/engine-suite counts in the historical section above belong to Task 1; the independent Task 1b full-suite results are recorded below.

### Behavior and real red/green evidence

`FillHelpersTests` adds 17 behavior cases in both configurations and four Debug-only work cases:

- Valid staggered rectangle groups exercise higher-count wins in both directions and equal-count density wins in both directions. The deliberately reversed-score comparer must select the opposite layout, even against count or density.
- Recording comparers assert exactly one call, horizontal candidate versus vertical current, the original work-area object, and the exact selected list reference. One angle keeps both writes on a single bag queue. Tie cases verify default selection retains the first bag result and custom comparison still executes, with both accept and reject outcomes.
- Layout comparisons preserve count, drawing reference identity, order, positions, rotations, and bounds. Input snapshots cover part/drawing/program/code identities, poses, bounds values, drawing area, and motion endpoints; a nonzero rotation case checks cloning without changing caller programs. Input angle lists and work areas are also checked where applicable.
- Empty angle lists, empty groups, and valid groups too large for the work area return null, make no comparer calls, and compute no scores. No invalid or null parts are used.
- Existing process-wide `PerfCounters.FillScoreComputations` is reused, with the existing nonparallel `FillCacheCollection` and reset in `finally`. No new production diagnostic or testing seam was added.

Before changing production, all characterization passed. Three new work assertions genuinely failed:

| Debug case | Expected | Before actual | After actual |
| --- | ---: | ---: | ---: |
| Non-scoring custom comparer, area 5×9 | 0 | 2 | 0 |
| Non-scoring custom comparer, area 10×4 | 0 | 2 | 0 |
| Custom comparer explicitly computes two scores | 2 | 4 | 2 |
| No comparer, default-score control | 2 | 2 | 2 |

The scoring-comparer control proves the helper does not bypass the supplied comparer or its score work. Release counter increments compile away; zero Release counters are not used as evidence of work removal.

| Run | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Before targeted Release | 47 | 0 | 0 |
| Before targeted Debug (intentional RED) | 48 | 0 | 3 |
| After targeted Release | 47 | 0 | 0 |
| After targeted Debug (GREEN) | 51 | 0 | 0 |
| Existing `DefaultFillComparerWorkTests`, Debug, after | 5 | 0 | 0 |
| Group-pattern measurement, Release, before | 1 | 0 | 0 |
| Group-pattern measurement, Release, after | 1 | 0 | 0 |
| Group-pattern gate, environment unset | 0 | 1 | 0 |
| Group-pattern gate, environment `0` | 0 | 1 | 0 |
| Parent full main suite, Release | 1,171 | 14 | 0 |
| Parent full main suite, Debug | 1,180 | 14 | 0 |
| Parent full engine suite, Release | 300 | 0 | 0 |

The targeted filter is `FillHelpersTests|FillPipelineTests|FillComparerTests|FillScoreTests` using `FullyQualifiedName~` for each term. The five older Debug comparer-work cases have a different class-name suffix, so they were run separately. Existing compiler warnings remain outside the changed code; no warnings were emitted for the new test/harness or changed helper. `git diff --check` passed. The parent independently reconciled all 14 raw timing rows against TRX and console output, verified source snapshots/hashes and the sole production-source difference, and reran the full suites. The 14 skips are the 12 existing optional CHR-font tests plus both opt-in benchmarks. README now documents the group-pattern filter, parallel-allocation limitation, and helper checks. The previously denied `CLAUDE.md` workflow sync remains blocked pending explicit approval; no retry or alternate write was attempted.

### Raw measured batches

Times in milliseconds for 20,000 actual production calls per mode per batch. Odd batches run default first; even batches run custom first.

| State | Batch | Default ms | Custom ms |
| --- | ---: | ---: | ---: |
| before | 1 | 12798.229355 | 12733.580886 |
| before | 2 | 12446.484280 | 12838.273225 |
| before | 3 | 12349.821045 | 12543.308696 |
| before | 4 | 12638.532022 | 12944.521123 |
| before | 5 | 12588.985384 | 12547.909410 |
| before | 6 | 12475.834989 | 12430.689835 |
| before | 7 | 12603.474398 | 12883.844997 |
| after | 1 | 12613.732653 | 12795.834020 |
| after | 2 | 12659.052180 | 12624.207441 |
| after | 3 | 12696.177150 | 12537.190933 |
| after | 4 | 12476.409124 | 12611.963585 |
| after | 5 | 12683.978429 | 12905.258236 |
| after | 6 | 12373.077829 | 12553.051166 |
| after | 7 | 12462.758852 | 12731.211633 |

### Evidence files and reproduction

Raw test/measurement logs, TRX, source snapshots/manifests, and exploratory artifacts were retained under `/home/aj/.hermes/cache/scratch/opennest-fill-1b-20260925/` through independent spec and subsequent quality/integration review. Both reviews passed with no findings and reconciled the source hashes, genuine red/green failures, raw timings, and full-suite outcomes. The parent verified the same evidence and formatting checks. Temporary artifacts were then removed before commit; all measured batch rows, source hashes, environment details, and test summaries needed for this report are preserved above.

Run the current group-pattern benchmark alone (the category filter also runs the historical comparer benchmark):

```bash
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~GroupPattern_ReportsDefaultAndCustomComparer' \
  --logger 'console;verbosity=detailed'

dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~FillHelpersTests|FullyQualifiedName~FillPipelineTests|FullyQualifiedName~FillComparerTests|FullyQualifiedName~FillScoreTests'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~FillHelpersTests|FullyQualifiedName~FillPipelineTests|FullyQualifiedName~FillComparerTests|FullyQualifiedName~FillScoreTests'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~DefaultFillComparerWorkTests'
```

To reproduce before/after independently, use separate disposable worktrees at the baseline revision and the delivered revision, copy the identical measured harness/tests into both, and verify their hashes before rebuilding and running. Do not substitute a test-only approximation for the before production path or mix measurements from changed harnesses.

## Task 2 — eliminate discarded extents pitch geometry — 2026-09-25

### Scope, proof, and compatibility

`FillExtents.BuildColumn` now uses `pair.Bbox.Width + partSpacing` directly for finite pair height and finite, nonnegative spacing. The source comment records the equivalence: the old helper returns that pitch for negative/no-hit slide distances; otherwise `minSlide >= 0`, so `Max(pairHeight - minSlide, pairHeight + partSpacing)` selects the same pitch. Boundary creation, two temporary clones, and vertical slide queries were discarded work in that domain.

The caller audit found that `NestJobValidator` rejects negative/nonfinite spacing, but the public constructor, `Plate.PartSpacing`, `PlateFillService`, and `ExtentsFillStrategy` do not establish that invariant for all interactive/public callers. The legacy calculation and its helpers therefore remain behind a negative/nonfinite-spacing or nonfinite-height guard. No validation or exception policy was added. Pair construction still prepares both boundaries. Tiling, adjustment, overlap fallbacks, progress, cancellation, bounds recomputation, and horizontal repetition are untouched. Task 3 and ML work are not included.

### Source and measurement provenance

- Resumed base: `cec7396da6d62386ef4817a6eb0e1b573ddd58de` on `master`, with unfinished Task 2 edits. The final implementation is delivered with this report; resolve its commit with `git log -1 --format=%H -- OpenNest.Engine/Fill/FillExtents.cs`.
- Original `FillExtents.cs` SHA-256: `e4110022f142ec4f954a00b8569dea5d451dbd5216756b0b6bf6a9595f2a83d0`.
- Optimized `FillExtents.cs` SHA-256: `efaacfca83a6d94bb7fdc65ba4da4be09c97c14c9c4e161491d48d64a69d1be3`.
- Identical final before/after harness `FillPerformanceTests.cs` SHA-256: `bb2adf31fa3348d1b84c239b01cdffa50fffc92193af3d1a4122461a93181cda`.
- Identical behavior/work tests `FillExtentsTests.cs` SHA-256: `b43508e30ab9683a123c4e00a0478e92cc1467fcf1afc5c6778f8fb2d548f02b`.
- Frozen test reference `LegacyFillExtents.cs` SHA-256: `b50b3b64014446d9688facc0b711b59cf126b64037fdf667603ce3d85859a324`. Reversing only the type/constructor rename and public-to-internal visibility change reproduced the baseline file byte-for-byte. The full reference is retained because the tests compare intermediate progress and private column-adjustment/fallback behavior as well as final layouts.
- Before tests/measurements temporarily restored the actual original production file, with restoration of the optimized file in `finally`. Source/project manifests matched across measurements except for `FillExtents.cs`; diagnostics and all tests were identical. No reference implementation or reflection is inside timing.
- Host: `hermes`, Ubuntu 24.04.5 LTS x64, Linux 6.8.0-142-generic, KVM guest with four vCPUs presented as AMD Ryzen 9 5900X. SDK 10.0.112. All measured tests emitted .NET 8.0.31, Release, and a 1,000,000,000 Hz stopwatch. Shared VM, no CPU pinning or host isolation.
- The resumed exploratory logs were not used for final numerical estimates: the full measurement pair was rerun after test formatting, then repeated in reverse process order. All final raw rows are preserved below; no best-run selection or mixing of harness versions.

### Behavior and deterministic work evidence

35 new Release behavior cases cover a 16-case shape/spacing/rotation matrix (rectangle, right triangle, concave L, native quarter-arc profile; spacing 0 and 0.5), nonzero work-area origins, no-fit parts/pairs, pre-cancellation, adjusted columns, overlap fallback at a 30-degree triangle angle, negative spacing, nonfinite no-fit calls, and isolated unsupported negative-spacing column calculations. Exact ordered comparisons include drawing reference identity, location, rotation, bounds, program geometry, and progress messages/layouts. Caller program/code identities and values, cached area, work-area values, and clone program-sharing/rotation ownership are checked. Main supported fixtures are finite, positive-area, in-bounds, and nonoverlapping; exact legacy equivalence preserves their spacing behavior rather than introducing a new spacing algorithm.

Two dedicated differential cases explicitly retain a pre-existing limitation: at zero spacing in a `(3,5,40,27)` area, rectangle/native-arc fills overrun the right edge by approximately `1e-5`. Those cases are not counted as valid-layout evidence and are not silently repaired by this optimization. The normal validity matrix and benchmark instead use `(3,5,45,27)`. Nonfinite spacing is tested only through bounded no-fit or isolated negative-infinity column paths, not unrestricted legacy tiling that could fail to terminate.

`PerfCounters.PartBoundaryPreparations` follows the existing Debug-only conditional increment pattern. Four new work assertions run in the existing nonparallel `FillCacheCollection`, resetting counters in `finally`:

| Case (each at spacing 0 and 0.5) | Required | Original production | Optimized production |
| --- | ---: | ---: | ---: |
| Four repeated `BuildColumn` calls | 0 preparations | 8 (genuine RED) | 0 |
| Full triangle fill, initial plus rebuilt column | 2 preparations | 6 (genuine RED) | 2 |

The two remaining full-fill preparations are the necessary `BuildPair` work, also asserted independently. Every full-fill work case returns the same 24-part layout. Six column-adjustment cases establish both accepted adjustment and fallback: four retain nonoverlapping adjusted columns, and two reject overlapping adjusted columns in favor of the unchanged initial column. The selected column and final layout remain in bounds. Release counter calls compile away; zero Release counters are not evidence of skipping work.

### Workload and results

The opt-in benchmark uses a closed right triangle `(0,0)-(10,0)-(0,8)-(0,0)`, work area `(3,5,45,27)`, rotation 0, and spacing 0 or 0.5. Both modes return 24 parts and rebuild their column once. Each spacing gets two 50-call warmups and seven 200-call measured batches per process. Spacing order alternates per batch. The first pair runs original then optimized; the repeat runs optimized then original. These are separate rebuilt test processes, not an interleaved in-process implementation comparison.

Timing includes real synchronous production fill, geometry preparation, tiling, adjustment, overlap checking/fallback, GC, delegates/loops, and count consumption. Setup, reference fills, assertions, and output are outside timing. Every batch consumes 4,800 parts and checks its last layout against the frozen reference. Source drawing data/JIT paths are warm; no forced collection, cache reset, file fixture, or network dependency is involved.

Microseconds per fill, minimum / median / maximum over seven batches:

| Process pair | Spacing | Before µs/fill | After µs/fill | Median change |
| --- | ---: | --- | --- | ---: |
| Forward | 0 | 2205.240 / 2281.382 / 2332.728 | 2158.270 / 2186.747 / 2207.422 | -4.15% |
| Forward | 0.5 | 2244.825 / 2310.086 / 2330.661 | 2155.409 / 2190.618 / 2215.091 | -5.17% |
| Reverse | 0 | 2165.221 / 2229.736 / 2403.664 | 2165.604 / 2210.005 / 2316.437 | -0.88% |
| Reverse | 0.5 | 2224.011 / 2302.593 / 2330.944 | 2187.911 / 2203.634 / 2266.720 | -4.30% |

The measured medians decrease in both process orders, but the ranges overlap (especially zero spacing) and the gain varies with run order. These shared-VM samples support a modest local improvement, not a stable percentage guarantee or whole-job speedup. No reproducible material regression was observed and there is no elapsed-time test gate.

Synchronous current-thread allocations are identical in every measured batch for a given implementation/spacing:

| Spacing | Before bytes/batch | After bytes/batch | Before bytes/fill | After bytes/fill | Saved bytes/fill | Reduction |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 0 | 174939200 | 167924800 | 874696 | 839624 | 35072 | 4.01% |
| 0.5 | 226099200 | 205708800 | 1130496 | 1028544 | 101952 | 9.02% |

These are `GC.GetAllocatedBytesForCurrentThread` managed-allocation differences around synchronous fills, not retained memory, RSS, or parallel-job totals. The deterministic result is removal of discarded boundary preparation; the allocation savings reproduce in both process orders.

### Tests and serial reviews

| Run | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Original targeted Release | 43 | 0 | 0 |
| Original targeted Debug (intentional RED) | 43 | 0 | 4 |
| Optimized targeted Release | 43 | 0 | 0 |
| Optimized targeted Debug | 47 | 0 | 0 |
| Each of four final opt-in Release measurement runs | 1 | 0 | 0 |
| Extents opt-in gate unset | 0 | 1 | 0 |
| Extents opt-in gate `0` | 0 | 1 | 0 |
| Full main Release | 1206 | 15 | 0 |
| Full main Debug | 1219 | 15 | 0 |
| Full engine Release | 300 | 0 | 0 |

The targeted filter is `FillExtentsTests|StrategyOverlapTests` with `FullyQualifiedName~` on each term. Full-suite TRX outcomes were parsed rather than inferred from console totals: main Release has 1,221 total results, Debug 1,234, engine 300. All skips are the 12 optional CHR-font fixtures and three opt-in performance tests. The adapter confirms .NET 8.0.31 in all full runs. Rebuilding the engine emits the existing `CirclePacking/Item.cs` CS0108/CS0114 warnings; rebuilding the test project also emits its existing nullable/xUnit warnings. The frozen `LegacyFillExtents.cs:30` adds one benign CS8625 warning because the unchanged legacy `reportProgress = null` default is copied into the nullable-enabled test project. It is retained verbatim for reference fidelity, not presented as a pre-existing test warning. No new production warnings or unexpected test failures were found.

Per the user's no-subagents instruction, spec review and subsequent quality/integration review were performed serially by the same agent, not independently. Spec review checked the algebraic guard, caller audit, exact frozen reference, behavior/ownership/cancellation/overlap coverage, genuine red/green work assertions, and Task 2-only scope. Quality review checked the production/test diff, Debug counter isolation and Release compilation convention, absence of new secrets or unsafe I/O, runtime/harness provenance, and all final raw TRX rows against console output. No blocking findings remained after the test-only whitespace correction. Changed-file `dotnet format whitespace --verify-no-changes` and `git diff --check` passed.

README documents the extents benchmark and work-test commands. The previously denied `CLAUDE.md` workflow sync remains blocked pending explicit approval; it was not retried. No representative corpus, Windows UI runtime test, or actual ONNX inference was run. This slice does not complete Tasks 3–5 or authorize gated follow-ups.

### Raw measured batches

Milliseconds per 200 actual production fills. Each row contains both implementations from one process pair; every batch produced 4,800 parts. Allocation totals for every row are the constant spacing-specific values above.

| Process pair | Spacing | Batch | Before ms | After ms |
| --- | ---: | ---: | ---: | ---: |
| forward | 0 | 1 | 463.229998 | 439.473112 |
| forward | 0 | 2 | 466.545605 | 437.349468 |
| forward | 0 | 3 | 450.826218 | 441.484339 |
| forward | 0 | 4 | 456.276359 | 438.751402 |
| forward | 0 | 5 | 462.975196 | 434.451956 |
| forward | 0 | 6 | 445.596554 | 433.705799 |
| forward | 0 | 7 | 441.048033 | 431.653954 |
| forward | 0.5 | 1 | 465.387515 | 443.018255 |
| forward | 0.5 | 2 | 462.017170 | 441.358437 |
| forward | 0.5 | 3 | 466.132129 | 438.198245 |
| forward | 0.5 | 4 | 450.824107 | 431.081841 |
| forward | 0.5 | 5 | 462.570224 | 438.123662 |
| forward | 0.5 | 6 | 454.096721 | 433.895019 |
| forward | 0.5 | 7 | 448.964925 | 437.877448 |
| reverse | 0 | 1 | 480.732828 | 463.287364 |
| reverse | 0 | 2 | 445.947233 | 434.966923 |
| reverse | 0 | 3 | 450.506707 | 442.001094 |
| reverse | 0 | 4 | 433.764890 | 446.086663 |
| reverse | 0 | 5 | 437.597684 | 448.988001 |
| reverse | 0 | 6 | 433.044245 | 433.120823 |
| reverse | 0 | 7 | 448.325975 | 439.151450 |
| reverse | 0.5 | 1 | 460.518547 | 447.369861 |
| reverse | 0.5 | 2 | 461.616765 | 437.593637 |
| reverse | 0.5 | 3 | 466.188871 | 453.049694 |
| reverse | 0.5 | 4 | 463.178063 | 453.343928 |
| reverse | 0.5 | 5 | 453.526082 | 440.726846 |
| reverse | 0.5 | 6 | 444.802234 | 440.614301 |
| reverse | 0.5 | 7 | 448.043300 | 437.582121 |

### Reproduction and evidence retention

```bash
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~Extents_ReportsRepeatedColumnRebuilds' \
  --logger 'console;verbosity=detailed'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~FillExtentsTests|FullyQualifiedName~StrategyOverlapTests'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~FillExtentsTests|FullyQualifiedName~StrategyOverlapTests'
```

For an independent before/after comparison, keep the delivered tests/diagnostics identical in two disposable trees and use the baseline `FillExtents.cs` only in the before tree. Rebuild each configuration and verify the source/harness hashes. Do not change production spacing policy to reproduce invalid-domain cases or substitute the frozen test type for the measured production method.

Final logs, TRX files, source restoration snapshot, manifests, and parsed rows were held in `/home/aj/.hermes/cache/scratch/opennest-task2-resume-20260925/` through review. The report preserves all final timing rows, allocation totals, source hashes, runtime and test summaries; temporary evidence and the superseded resumed exploration directory are removed before commit. Next planned hardening is the separately reviewable redundant-bounds slice with threshold-fit and canonical-frame safety, not a geometry repair bundled into this change.

## Task 3 — remove provably redundant bounds walks — 2026-09-25

### Scope and per-site decisions

`Part.Offset` translates the cached `BoundingBox` arithmetically and `Part.Clone` copies it exactly, so several `UpdateBounds()` calls in the extents/pattern paths recompute a box that just changed by pure translation. Recomputation is mathematically equivalent but not bitwise identical to accumulated translates, and the extents pipeline compares boxes at `Tolerance.Epsilon` fit thresholds. Each candidate removal therefore had to keep every characterized layout, box, and threshold branch bitwise identical (the differential harness compares `(X, Y, Length, Width)` and location tuples with exact `double` equality, not tolerance — `Vector.Equals` itself is tolerance-based, so location/coordinate snapshots were switched to scalar tuples to expose ulp deltas).

| Site | Decision | Evidence |
| --- | --- | --- |
| `BuildPair` after `Offset(startOffset, 0)` | Removed | All characterization green; slide uses locations, not cached bounds. |
| `BuildPair` after `Offset(-dist, 0)` | Removed | All characterization green. |
| `TryShiftDirection` after `Offset(partSpacing, 0)` | Removed | The retained post-vertical-shift recompute covers both offsets. |
| `TryShiftDirection` after `Offset(0, verticalShift)` | Retained | Rotated triangle spacing 0.5: box Y `5.6025660004937174` → `5.6025660004937183`. `Compactor.Push` box thresholds consume this box. |
| `AnchorToWorkArea` part1/part2 recompute | Retained | Part2-only removal changed rectangle differential X `13.499999999999998` → `13.5` and arc pair height `8.0000000000000036` → `8`; part1 removal changed X `3.1000000000000005` → `3.100000000000001`. Part1-only removal failed 6 of 101 characterization cases; part2-only removal failed 25 of 101, including overlap fallback. The anchor `workArea.X - bbox.Left` round-trip lands a ulp away from `X += anchor`. |
| `BuildRotatedPattern` after `Clone()` | Retained | Accumulated-translation input at angle 0: box `1.3000000000000003` → `1.3`. Source comments now record why each retained site stays. |

No change to `Part.Offset`, `Part.Clone`, `Part.Rotate`, `Part.UpdateBounds`, `PairBbox`, `pattern.UpdateBounds()`, the Task 2 pitch guard, or `LegacyFillExtents.cs` (frozen SHA-256 `b50b3b64014446d9688facc0b711b59cf126b64037fdf667603ce3d85859a324` unchanged). `PerfCounters.PartBoundsUpdates` (Debug-only, incremented at the top of `Part.UpdateBounds`) was added per the plan.

### Source and measurement provenance

- Base: `4ec92c95ec10c5c877e9fb0f34f197d7e23b87b0` on `master`. Final implementation is delivered with this report; resolve with `git log -1 --format=%H -- OpenNest.Engine/Fill/FillExtents.cs`.
- `FillExtents.cs` SHA-256 before `efaacfca83a6d94bb7fdc65ba4da4be09c97c14c9c4e161491d48d64a69d1be3` (Task 2 delivered) → after `13be137690ad5f60e0fe3874ec0cb0b93e9864b97d7eee7f3aa65c9b962053a4`.
- `FillHelpers.cs` before `a1c8a1bc155dbe62439f8345a3a54e06147a5404a6a46835a30b66d52cceccfa` → after `0a35075a573c7f17f9c8095a5a7bd56d0a4ea935ec1c9d8da2c4c9d78e0a9af0` (comment-only; site retained).
- Identical before/after harness `FillPerformanceTests.cs` SHA-256 `93ec06c5b59aecfe5cb9311380afea507cc134e77df7535036fb451ab0f63e29`.
- Before timings ran the actual pre-change production files (restored temporarily, restored back in `finally`); after timings the delivered files. Same machine, serial Release: `hermes`, Ubuntu 24.04.5 LTS x64, KVM, four vCPUs presented as AMD Ryzen 9 5900X, SDK 10.0.112, .NET 8.0.31, 1 GHz stopwatch. Shared VM, no pinning.

### Work-removal evidence (genuine red/green)

New Debug counter tests (serialized in `FillCacheCollection`, reset in `finally`) failed first on the unmodified production code: `Fill_BoundsWork_PerformsFewerUpdates` expected fewer than 13 part-bounds updates, actual 13 (both spacings); the group-construction work assertion expected 0/2 updates at angles 0/0.37, actual 2/4. After the change: full triangle fill 13 → 10 updates with 24 bitwise-identical parts (both spacings). The group assertions were rewritten to pin the retained counts (2 at angle 0, 4 at 0.37 = retained clone recompute + rotate recompute per part) because removing the clone recompute failed behavior characterization — disclosed partial delivery, not an optimized site. `Pattern.UpdateBounds` aggregates boxes and does not call `Part.UpdateBounds`, so it is outside this counter. Per fill the three removals save three `UpdateBounds` calls (three `Box` allocations — 144 bytes) reproduced exactly in every measured batch.

### Characterization added (all passing pre-change, 133 targeted Release)

- 16 extents threshold cases: pair-fit, column-tiling, and column-count branches on both sides of `Tolerance.Epsilon`, nonzero work-area origin, progress layouts, input snapshots.
- 16 adjacent-double pair/column cases (`BitIncrement`/`BitDecrement` neighborhoods with asserted both-side decisions) and 16 adjacent-double `TryShiftDirection` width-acceptance cases (binary-searched rejected/accepted doubles located using only the frozen pre-change path, ±shifts).
- 12 `BuildRotatedPattern` cases: translated, accumulated-translation, pre-rotated, canonical-frame, native-arc, and nonzero-program-origin groups at angles 0 and 0.37, checking exact poses/bounds against a local pre-change reference, input non-mutation, clone program ownership, and no double rotation of pre-rotated inputs.
- Frozen-legacy differentials, threshold branch equality, and per-site removal trials (each candidate removed alone, characterization run, restored) reconcile the per-site table above; the trial logs were retained through review in the scratch directory and removed afterwards, with the resulting ulp deltas and failure counts recorded in this report.

### Tests (parent independently reran the full suites on the final tree)

| Suite | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Targeted Release (`FillExtentsTests|FillHelpersTests|CanonicalFrameTests|StripeFillerTests`) | 133 | 0 | 0 |
| AGENTS.md Debug work filter | 137 | 0 | 0 |
| Full main Release | 1266 | 16 | 0 |
| Full main Debug | 1283 | 16 | 0 |
| Full engine Release | 300 | 0 | 0 |

Skips are the 12 optional CHR-font fixtures plus four opt-in benchmarks (the new rotated-pattern case adds one). Gate verified: unset and `0` skip all category tests. `.NET 8.0.31` adapters confirmed. Changed-file `dotnet format whitespace --verify-no-changes` and `git diff --check` passed; no new production warnings.

### Reviews

Implementation, spec review, and quality/integration review ran as three independent agent sessions (the earlier same-agent serial constraint was lifted by the user for this session). Spec review initially found one Important issue — the canonical fixture consumed a stale `Source.Angle` and duplicated the pre-rotated scenario — plus one Minor report-wording error; both were fixed (fixture now refreshes the canonical angle and asserts a nonzero source-to-canonical transformation; the failure counts are stated as 6/101 and 25/101), and the strengthened fixture additionally surfaced a benign test-assertion normalization issue at a baked 2π rotation, fixed by normalizing both sides. Subsequent independent quality/integration review returned PASS with no Critical, Important, or Minor findings: production diff traced through slide/Compactor consumers, counter isolation, adjacent-double oracle independence, hash/median/allocation reconciliation against raw logs, integration sweep across fill-strategy and jobs callers, and disclosure honesty were all verified by that reviewer. Pre-existing DXF-fixture-dependent tests still return early without their Windows fixture; no Windows UI runtime test or ONNX inference was run.

### Results

All before/after timing ranges overlap: elapsed-time improvement is inconclusive, consistent with three saved `Program.BoundingBox()` walks being small against full fills. Allocation savings are exact and reproduce in every batch: extents spacing 0 `839,624` → `839,480` bytes/fill and spacing 0.5 `1,028,544` → `1,028,400` bytes/fill (−144 B/fill). Rotated-pattern construction is an unchanged-code control: identical allocations per call at both angles.

Extents: milliseconds per 200 production fills, seven batches each, alternating spacing order, warmup excluded. Rotated-pattern: milliseconds per 5,000 constructions of a 32-part native-arc group.

| Workload | Batch | Before ms | After ms |
| --- | ---: | ---: | ---: |
| extents spacing 0 | 1 | 461.646777 | 502.378600 |
| extents spacing 0 | 2 | 439.442203 | 446.609639 |
| extents spacing 0 | 3 | 450.272109 | 457.012036 |
| extents spacing 0 | 4 | 433.095759 | 454.402604 |
| extents spacing 0 | 5 | 443.489319 | 447.382522 |
| extents spacing 0 | 6 | 433.504166 | 448.477156 |
| extents spacing 0 | 7 | 441.588374 | 439.879594 |
| extents spacing 0.5 | 1 | 460.691045 | 439.973796 |
| extents spacing 0.5 | 2 | 457.895526 | 443.225746 |
| extents spacing 0.5 | 3 | 444.315207 | 451.898487 |
| extents spacing 0.5 | 4 | 436.733945 | 447.851680 |
| extents spacing 0.5 | 5 | 451.266260 | 431.428815 |
| extents spacing 0.5 | 6 | 438.338321 | 455.657180 |
| extents spacing 0.5 | 7 | 448.995034 | 438.982789 |
| rotated-pattern angle 0 | 1 | 92.121779 | 95.085965 |
| rotated-pattern angle 0 | 2 | 85.115166 | 83.764479 |
| rotated-pattern angle 0 | 3 | 84.692335 | 82.859009 |
| rotated-pattern angle 0 | 4 | 90.233547 | 85.003329 |
| rotated-pattern angle 0 | 5 | 85.081651 | 83.281808 |
| rotated-pattern angle 0 | 6 | 78.809908 | 77.355562 |
| rotated-pattern angle 0 | 7 | 78.280977 | 80.372130 |
| rotated-pattern angle 0.37 | 1 | 184.337007 | 186.875744 |
| rotated-pattern angle 0.37 | 2 | 170.493990 | 155.509393 |
| rotated-pattern angle 0.37 | 3 | 153.992846 | 155.681698 |
| rotated-pattern angle 0.37 | 4 | 155.207434 | 156.663232 |
| rotated-pattern angle 0.37 | 5 | 158.498327 | 149.814054 |
| rotated-pattern angle 0.37 | 6 | 157.148604 | 150.090405 |
| rotated-pattern angle 0.37 | 7 | 150.252399 | 150.547539 |

Medians: extents spacing 0 2207.942 → 2242.386 µs/call, spacing 0.5 2244.975 → 2216.129 µs/call; rotated-pattern angle 0 17.016 → 16.656 µs/call, angle 0.37 31.430 → 31.102 µs/call. Every batch produced the expected part counts (extents 4,800; rotated-pattern 160,000) and layout assertions ran on the timed path's output outside timing. Current-thread synchronous allocations only; no whole-job or RSS claims.

### Reproduction and limitations

```bash
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'Category=FillPerformance' --logger 'console;verbosity=detailed'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~FillExtentsTests|FullyQualifiedName~FillHelpersTests|FullyQualifiedName~CanonicalFrameTests|FullyQualifiedName~StripeFillerTests'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug \
  --filter 'FullyQualifiedName~DefaultFillComparerWorkTests|FullyQualifiedName~FillHelpersTests|FullyQualifiedName~FillExtentsTests|FullyQualifiedName~StrategyOverlapTests'
```

This is a partial delivery by design: three of six candidate recomputations were removable; the three retained sites are documented with their observed ulp deltas, and their `UpdateBounds` calls must not be removed without re-running the threshold and overlap-fallback characterization. Timing is inconclusive; only allocation and work-counter removal are demonstrated. No representative production corpus, Windows UI runtime test, or ONNX inference was run. Task 4 and gated follow-ups are not included.

## Task 4a — scalar-only feature extraction — 2026-09-25

### Scope

`FeatureExtractor.Extract` always built a 32×32 bitmask — 1024 `Polygon.ContainsPoint` ray-cast tests plus a 1,024-byte array — even though the only production consumer at runtime (the `AngleCandidateBuilder` ML branch) reads nothing from `PartFeatures.Bitmask`. This slice adds `Extract(Drawing, bool includeBitmask)`; the original one-argument signature is preserved as a source- and binary-compatible wrapper that keeps the training bitmap. Every scalar formula, tolerance, material filter, and the canonical-frame normalization run identically in both modes. Training consumers (`OpenNest.Training`) are unchanged, and `AngleCandidateBuilder` is not yet switched over — Task 4b gates the inference call site. No production behavior changes for any existing caller.

`PerfCounters.FeatureBitmaskCells` (Debug-only, incremented inside the bitmask scan loop) was added to prove the point-in-polygon work is genuinely skipped.

### Source provenance

- Base: `6863c8bdb1c7d98eb5b7c45153e062e7cb335464` on `master`. Final implementation is delivered with this report.
- `FeatureExtractor.cs` SHA-256 before `1094d386595c0513fcb6db1e5ba7b2c7a0b8c19cf351f6cf9a462356156fdd15` → after `2d7b45350abd94d7e728c962aca06f4a86920d0328d8025c75f51ebc7e5ed6b3`.
- `PerfCounters.cs` before `0790401ae0433e416c4ac40d678403407497226d6bd40057b7c4e959da6e0582` → after `b596437210719981940ebf7ae03b31b7493f44002cf545bbe638c35bebf5b65d`.
- Harness `FillPerformanceTests.cs` at measurement time: before/after runs identical at `c257b27a5a4335278c99b8f623540b5580b51d1cc9d31a04bc8d8641a9c722a0` (new method added to base `93ec06c5b59aecfe5cb9311380afea507cc134e77df7535036fb451ab0f63e29`; existing methods byte-identical). The delivered tree ships `27c213f202e46cdd9a1e46bb0263784277f2aecc17469ddd83c5db26c1b129aa` — disclosure/comment wording only, timed code paths identical.
- Environment: same shared KVM VM as prior slices — Ubuntu 24.04.5 LTS x64, four vCPUs presented as AMD Ryzen 9 5900X, SDK 10.0.112, .NET 8.0.31 adapters confirmed, 1 GHz stopwatch, serial Release.

### Work-removal evidence (genuine red/green)

The change is additive, so red was demonstrated by neutralizing the new flag in the delivered overload (forcing always-bitmap construction — exactly the original production behavior): on the final tree 8 of 38 Debug cases failed — the five `ScalarOnlyOverload_OmitsBitmap` theory cases, `ScribeAndRapidMarks_DoNotAffectEitherOverload`, and `RotatedInput_FeaturesMatchAxisAlignedEquivalent` with `Assert.Null() Failure: Value is not null`, plus `ScalarOnlyOverload_RunsNoBitmaskCellScan` failing the same assertion inside its counted window. Restoring the flag turned all 38 Debug tests green. On the delivered tree: default extraction counts exactly 1,024 bitmask cells per call (matching the documented scan), scalar-only counts 0, and failed extraction (rapid-only program) counts 0.

New `OpenNest.Tests/ML/FeatureExtractorTests.cs` (35 behavior cases plus 3 Debug counter cases, serialized in `FillCacheCollection` for the counter tests): exact-equality scalar snapshots across both overloads on five shapes (rectangle, right triangle, L-shape concave profile, native-arc ring, native circle); explicit-true bitmap equality bit-for-bit against the default overload; default training-bitmap shape/content preservation (1,024 cells, 0/1 only, mixed for ring/L-shape, all-set for a full rectangle); rotated-input invariance against an axis-aligned equivalent; canonical-copy input; scribe/rapid marks ignored by both overloads with identical bitmaps; input non-mutation; determinism across repeated extraction. Because `ExplicitTrue_MatchesDefaultOverloadBitForBit` alone cannot detect a rasterization change applied to both overloads (the default delegates to it), the five fixtures are additionally pinned against frozen SHA-256 hashes of the bitmaps emitted by the actual pre-change implementation at `6863c8b` (captured by running the original code, cross-checked by the spec reviewer against an in-memory HEAD compilation). Pre-existing edge behavior is characterized, not changed: rapid-only drawings throw `ArgumentOutOfRangeException` in `ShapeProfile` and null drawings throw `NullReferenceException` identically through both overloads.

### Tests

| Suite | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Targeted Release (`FeatureExtractorTests|CanonicalFrameTests`) | 40 | 0 | 0 |
| Targeted Debug (`FeatureExtractorTests`) | 38 | 0 | 0 |
| Full main Release | 1,301 | 17 | 0 |
| Full main Debug | 1,321 | 17 | 0 |
| Full engine Release | 300 | 0 | 0 |

Skips: 12 optional CHR-font fixtures plus five opt-in benchmarks (the new feature-extraction case adds one). Gate verified: unset skips all five category tests. Changed-file `dotnet format whitespace --verify-no-changes` and `git diff --check` passed; no new production warnings.

### Results

Same-harness, same-machine, serial Release; ring OD=20 ID=8 (perimeter plus one circular cutout); warmup 2×200, measured 7×1,000 calls, alternating mode order, correctness checks outside timing; allocations are current-thread synchronous only. The before run exercised the original always-bitmap implementation in both modes (harness scalar lambda temporarily pointed at the full path); the after run is the delivered tree.

| Batch | Before full ms / B | Before scalar-mode ms / B | After full ms / B | After scalar-only ms / B |
| ---: | --- | --- | --- | --- |
| 1 | 154.692010 / 35,338,120 | 154.066548 / 35,332,896 | 197.474351 / 35,338,120 | 31.020699 / 34,252,896 |
| 2 | 161.643928 / 35,338,120 | 151.896080 / 35,332,896 | 157.026495 / 35,332,896 | 29.342949 / 34,252,896 |
| 3 | 149.279498 / 35,332,896 | 155.797847 / 35,332,896 | 154.219132 / 35,332,896 | 32.988987 / 34,268,256 |
| 4 | 153.772483 / 35,332,896 | 153.226512 / 35,332,896 | 161.453458 / 35,332,688 | 41.092334 / 34,252,688 |
| 5 | 155.591257 / 35,338,120 | 155.760388 / 35,332,896 | 151.984770 / 35,332,688 | 30.768833 / 34,252,688 |
| 6 | 153.993059 / 35,332,896 | 154.410087 / 35,332,896 | 153.549576 / 35,332,688 | 29.824879 / 34,252,688 |
| 7 | 155.984019 / 35,338,120 | 159.061932 / 35,332,896 | 157.327152 / 35,332,688 | 29.530524 / 34,252,688 |

Medians (µs/call): before scalar-mode (original behavior) 154.410 → after scalar-only 30.769 (−80.1%). Allocations: medians 35,332.896 → 34,252.688 B/call. Within the after run, full minus scalar-only per batch is 1,085.224 / 1,080.000 / 1,064.640 / 1,080.000 / 1,080.000 / 1,080.000 / 1,080.000 B/call (median 1,080.000): the 1,048-byte `byte[1024]` allocation plus ~32 B from the harness's in-window bitmap-count consumption (a reviewer probe on .NET 8.0.31 measured 1,048 and 32 respectively) — i.e. extraction-plus-consumption overhead, not 1,080 B of production extraction alone; the production `GenerateBitmask` loop itself allocates nothing beyond the array. Before scalar-mode and before-full rows agree within noise, confirming the harness adaptation introduced no bias. Timing likewise includes that enumeration in full mode.

Default-overload (training) timing is inconclusive: after-full median 157.026 vs before-full 154.692 µs/call (+1.5%, inside the overlapping batch ranges; the after run's batch 1 at 197.474 is a warmup-adjacent outlier included unmodified). Overlapping ranges establish neither regression nor non-regression for the full mode.

### Reproduction and limitations

```bash
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~FeatureExtraction_ReportsFullAndScalarOnly' --logger 'console;verbosity=detailed'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~FeatureExtractorTests|FullyQualifiedName~CanonicalFrameTests'
```

This is a capability slice, not yet a production speedup: the inference call site keeps using the default overload until Task 4b gates predictor availability, so real nesting runs show no change from this commit. The measured gain applies to inference callers once wired. No representative production corpus, Windows UI runtime test, or ONNX inference was performed; the Debug counter increments compile away in Release.

## Task 4b — gate angle features on predictor availability — 2026-09-26

### Scope and contract

`AngleCandidateBuilder` now checks `AnglePredictor.IsAvailable` before extraction, and available inference requests `includeBitmask: false`. The public parameterless constructor remains; readonly per-instance delegates provide isolated test doubles without process-global overrides. The prediction default threshold (0.3), `Width` then `Length` argument order, prediction-prefix duplicates, tolerance-based appended-angle deduplication, base/sweep order, constraints, classification shortcuts and known-good pruning are unchanged. The legacy repeated-addition sweep includes a value just below π: 37 sweep entries, not an integer-indexed 36-angle replacement.

`IsAvailable` and `PredictAngles` use the same initializer. The existing lock/volatile one-attempt pattern lives in the narrow internal `SingleAttemptLoader<T>` (generic solely to test with reference objects instead of ONNX files). Completion is published in `finally`, after assignment or definitive failure; a concurrent reader waits rather than seeing transient unavailability. Missing and failed loads remain cached for the process lifetime; no model reload policy, independent availability cache, model-file changes, packages or projects were added. Training extraction is unchanged.

### Provenance and verification

- Base: `d70505b7c0e41692a031971fb437bbb165493b9f` on `master`; this section accompanies the final implementation commit. The baseline was an isolated `git archive` with only the identical measurement harness added. Production/project comparison found only `AngleCandidateBuilder.cs` and `AnglePredictor.cs` changed; actual baseline production was measured, not a reimplementation.
- Environment: Ubuntu 24.04.5 LTS x64 shared KVM VM, four vCPUs presented as AMD Ryzen 9 5900X; SDK 10.0.112. Test adapters and benchmark output confirm .NET 8.0.31; Stopwatch frequency 1,000,000,000 ticks/s. Serial Release processes, forward then reversed before/after order.
- Parent applied whitespace-only fixes to the builder constructor and the loader concurrency test after implementation-agent tests; full suites were rerun afterward. Published measurements use the final source/harness hashes below. An initial short no-model warmup showed transient timing spread; it was replaced in **both** trees by 2×20,000 warmup calls and 7×20,000 measured calls. Only the complete rerun pairs below are acceptance measurements.

| Source | Before SHA-256 | Delivered/measurement SHA-256 |
| --- | --- | --- |
| `OpenNest.Engine/Fill/AngleCandidateBuilder.cs` | `5182f69f604332e1b8bfb46d807c8f830194a88b47f4fd4e90bcea6e91e522c4` | `97bbb82680b90d950f480c65dffab8702bd9402ef849639286d7fc90f1b6b162` |
| `OpenNest.Engine/ML/AnglePredictor.cs` | `84bc0eafffe84447dc83e3c7084e5562a67409572fa413f78abe6e7102ba3c72` | `047e3bdb8d9aec1add49ba36e5b7947d1b375e6099a3f36f54061b6ae4d16f7a` |
| `OpenNest.Engine/ML/FeatureExtractor.cs` | `2d7b45350abd94d7e728c962aca06f4a86920d0328d8025c75f51ebc7e5ed6b3` | `2d7b45350abd94d7e728c962aca06f4a86920d0328d8025c75f51ebc7e5ed6b3` |
| `OpenNest.Tests/Fill/FillPerformanceTests.cs` | `f90412c87393f8542769aae5abef5e340b827003c134d0bf525efef3a3fefa62` | `f90412c87393f8542769aae5abef5e340b827003c134d0bf525efef3a3fefa62` |

The harness hash in both columns is the newly added identical harness, not the original base harness. Existing feature-extraction timed code is unchanged.

Genuine RED/GREEN evidence (temporary source mutations restored in `finally`): removing the availability gate failed the exact call sequence (expected availability only, actual availability/extraction/prediction); forcing `true` failed the scalar flag assertion; the real-extractor Debug integration check counted 1,024 bitmap cells instead of 0; publishing before the blocked load failed all three concurrent-reader cases (success, missing, throwing). Restored targeted suites pass. Counter tests use the nonparallel `FillCacheCollection` and reset in `finally`. Release-zero counters are not evidence. The loader concurrency test observes a blocked dedicated reader, not merely an unscheduled task; ten additional process runs passed all three cases.

| Verification | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Pre-change builder characterization | 17 | 0 | 0 |
| Targeted Release (builder/predictor/extractor) | 77 | 0 | 0 |
| Targeted Debug (builder/predictor/extractor) | 81 | 0 | 0 |
| Full main Release, final code | 1,335 | 18 | 0 |
| Full main Debug, final code | 1,356 | 18 | 0 |
| Full engine Release, final code | 300 | 0 | 0 |
| Performance category, variable unset | 0 | 6 | 0 |
| Performance category, variable `0` | 0 | 6 | 0 |
| Each enabled two-case measurement process (four runs) | 2 | 0 | 0 |

Full-suite skips are 12 optional CHR-font fixtures plus six opt-in benchmarks. Parent parsed TRX outcomes and reconciled all published raw measurement rows against console output. Changed-file whitespace verification and `git diff --check` passed. Existing optional-fixture, frozen-reference nullable and obsolete API warnings remain outside this slice.

### Warm no-model path measurements

Closed concave L vertices `(0,0),(20,0),(20,6),(8,6),(8,14),(0,14)`, primary angle 0.13 radians, work area `(3,5,120,80)`, `ForceFullSweep=true`. Public production builder, no delegate substitution. Missing-model initialization, construction, assertions and output occur outside timing. Exact ordered angles are checked against the pre-change fallback expression. Each process has two 20,000-call warmups and seven 20,000-call batches. Loop/result-count consumption and GC are included; allocations are synchronous current-thread bytes, not process RSS. The benchmark skips if a model exists and never removes it.

| Batch | Before forward ms / B | After forward ms / B | After reverse ms / B | Before reverse ms / B |
| ---: | --- | --- | --- | --- |
| 1 | 1217.927296 / 503,670,984 | 55.374701 / 119,200,000 | 52.644238 / 119,200,000 | 1184.910444 / 503,670,984 |
| 2 | 1187.003770 / 503,660,640 | 54.922257 / 119,200,000 | 53.317338 / 119,200,000 | 1245.986050 / 503,660,640 |
| 3 | 1191.853980 / 503,660,640 | 53.574434 / 119,200,000 | 52.454731 / 119,200,000 | 1234.360386 / 503,660,640 |
| 4 | 1207.375477 / 503,660,640 | 52.648817 / 119,200,000 | 51.054148 / 119,200,000 | 1247.194291 / 503,660,640 |
| 5 | 1212.013076 / 503,670,984 | 52.498333 / 119,200,000 | 51.060020 / 119,200,000 | 1236.030807 / 503,670,984 |
| 6 | 1223.337623 / 503,660,640 | 51.969916 / 119,200,000 | 51.673347 / 119,200,000 | 1265.240358 / 503,660,640 |
| 7 | 1249.298485 / 503,660,640 | 51.376286 / 119,200,000 | 52.600186 / 119,200,000 | 1264.421373 / 503,660,640 |

| Process | µs/call min / median / max | B/call min / median / max |
| --- | --- | --- |
| before-measure | 59.350 / 60.601 / 62.465 | 25,183.0320 / 25,183.0320 / 25,183.5492 |
| after-measure | 2.569 / 2.632 / 2.769 | 5,960.0000 / 5,960.0000 / 5,960.0000 |
| after-repeat | 2.553 / 2.623 / 2.666 | 5,960.0000 / 5,960.0000 / 5,960.0000 |
| before-repeat | 59.246 / 62.299 / 63.262 | 25,183.0320 / 25,183.0320 / 25,183.5492 |

Measured median warm-path time changes are -95.66% and -95.79% in the two process orders. Median allocations fall from 25,183.032 to 5,960 B/call (19,223.032 B/call, 76.33%). After allocations are exactly 5,960 B/call in every batch; baseline totals have small variations disclosed above. These results support a local warm no-model improvement, not a stable cross-machine latency guarantee or a whole-nesting-job speedup. No cold-start or failed-model timing is claimed.

### Separate extraction control

Existing `FeatureExtraction_ReportsFullAndScalarOnly`: native-arc ring OD20/ID8, two 200-call warmups, seven 1,000-call batches per mode, alternating mode order. This is a separate extraction benchmark, not available-model inference. Feature extraction is identical across these trees; the full versus scalar difference is the already-delivered Task 4a capability, now used by the builder. Timing/bytes include the harness's bitmap-count consumption for full mode (see Task 4a's ~32-byte consumption disclosure), so the delta is not solely production extraction allocation. Process-to-process differences are shared-VM noise/control observations, not a Task 4b extraction speedup claim.

| Process | Batch | Full ms / B | Scalar-only ms / B |
| --- | ---: | --- | --- |
| before-measure | 1 | 217.757693 / 35,332,688 | 37.378244 / 34,263,032 |
| before-measure | 2 | 170.635584 / 35,332,688 | 34.053642 / 34,252,688 |
| before-measure | 3 | 172.566367 / 35,343,032 | 34.951535 / 34,252,688 |
| before-measure | 4 | 170.000626 / 35,332,688 | 33.384049 / 34,273,376 |
| before-measure | 5 | 169.570514 / 35,332,688 | 34.399103 / 34,252,688 |
| before-measure | 6 | 171.122593 / 35,332,688 | 33.865496 / 34,283,720 |
| before-measure | 7 | 168.040086 / 35,332,688 | 32.055312 / 34,263,032 |
| after-measure | 1 | 227.885101 / 35,332,896 | 32.285726 / 34,252,896 |
| after-measure | 2 | 175.358064 / 35,338,120 | 30.359862 / 34,252,896 |
| after-measure | 3 | 184.505182 / 35,332,896 | 31.867147 / 34,252,896 |
| after-measure | 4 | 169.872394 / 35,332,896 | 29.338054 / 34,252,896 |
| after-measure | 5 | 168.238732 / 35,338,120 | 30.606238 / 34,252,896 |
| after-measure | 6 | 189.879784 / 35,332,896 | 30.637576 / 34,252,896 |
| after-measure | 7 | 174.944123 / 35,332,896 | 30.820803 / 34,252,896 |
| after-repeat | 1 | 272.298581 / 35,332,896 | 52.314757 / 34,252,896 |
| after-repeat | 2 | 194.604106 / 35,338,120 | 52.072921 / 34,252,896 |
| after-repeat | 3 | 159.231266 / 35,332,896 | 33.244075 / 34,252,896 |
| after-repeat | 4 | 152.710834 / 35,332,896 | 31.001913 / 34,252,896 |
| after-repeat | 5 | 154.032296 / 35,338,120 | 31.102123 / 34,252,896 |
| after-repeat | 6 | 150.289445 / 35,332,896 | 29.016949 / 34,252,896 |
| after-repeat | 7 | 156.205127 / 35,332,896 | 30.082710 / 34,252,896 |
| before-repeat | 1 | 215.909255 / 35,332,688 | 37.149342 / 34,252,688 |
| before-repeat | 2 | 188.558239 / 35,332,688 | 35.593749 / 34,252,688 |
| before-repeat | 3 | 198.129599 / 35,343,032 | 38.193683 / 34,252,688 |
| before-repeat | 4 | 186.170223 / 35,332,688 | 36.974173 / 34,252,688 |
| before-repeat | 5 | 193.646279 / 35,332,688 | 46.604443 / 34,252,688 |
| before-repeat | 6 | 195.491390 / 35,332,688 | 50.731669 / 34,252,688 |
| before-repeat | 7 | 191.895666 / 35,332,688 | 40.247489 / 34,252,688 |

| Process | Full µs/call min / median / max | Scalar µs/call min / median / max |
| --- | --- | --- |
| before-measure | 168.040 / 170.636 / 217.758 | 32.055 / 34.054 / 37.378 |
| after-measure | 168.239 / 175.358 / 227.885 | 29.338 / 30.638 / 32.286 |
| after-repeat | 150.289 / 156.205 / 272.299 | 29.017 / 31.102 / 52.315 |
| before-repeat | 186.170 / 193.646 / 215.909 | 35.594 / 38.194 / 50.732 |

### Reproduction, review and remaining limits

```bash
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~IrregularAngles_ReportsWarmNoModelPath|FullyQualifiedName~FeatureExtraction_ReportsFullAndScalarOnly' \
  --logger 'console;verbosity=detailed'
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~AngleCandidateBuilderTests|FullyQualifiedName~AnglePredictorTests|FullyQualifiedName~FeatureExtractorTests'
```

Repeat the targeted command in Debug for bitmap work assertions. Shared safeguards moved intact (with Task 4b additions) from near-capacity `AGENTS.md` into [fill verification](fill-verification.md); the combined `AGENTS.md` + `CLAUDE.md` is now 31,434 bytes, below 32 KiB. README and the shared instructions describe availability-gated scalar inference; `CLAUDE.md` remains the thin import.

Independent spec review **PASS**, followed by independent quality/integration review **APPROVED**; neither found Critical, Important or Minor issues. Both reconciled source/harness provenance, raw measurements, summaries, red/green and full-suite evidence. Each independently reran the targeted suites (77 Release, 81 Debug) and both disabled benchmark gates (six skipped each); the spec reviewer ran ten more three-case concurrency processes, and the quality reviewer ran three plus the full engine suite (300 passed). Raw scratch evidence was removed after review, retaining the tables, hashes and test summaries here. No actual ONNX inference or model accuracy test, Windows UI runtime test, or representative production corpus measurement was performed. Task 5 combined acceptance/report remains separate; gated A/B/C follow-ups were not started.

## Task 5 — combined initial-batch acceptance — 2026-09-26

This section closes the initial batch (Tasks 1–4b, delivered as `b318950`, `cec7396`, `6efa6b1`, `4553f8a`/`6863c8b`, `8188533`, `1e8e532`) by rerunning the complete verification surface on the final tree and adding the first whole-job before/after measurement. No production code changed in this task.

### Acceptance suite results on the final tree

Final tree: `1e8e532063434cbbc4aeeea6d50e090f4998ac00` on `master` (SDK 10.0.112, target runtime Microsoft.NETCore.App 8.0). Same machine as all earlier sections (KVM guest, 4 vCPUs presented as AMD Ryzen 9 5900X, Ubuntu 24.04).

| Run | Passed | Skipped | Failed | vs Task 0 baseline |
| --- | ---: | ---: | ---: | --- |
| Main suite, Release | 1,335 | 18 | 0 | +195 passed; skips are 12 optional CHR fixtures + 6 opt-in benchmarks |
| Main suite, Debug | 1,356 | 18 | 0 | +216 passed; same skip composition |
| Engine suite, Release | 300 | 0 | 0 | identical to baseline |

Baseline was 1,140 passed / 12 skipped / 0 failed (main) and 300 / 0 / 0 (engine). Every added test from Tasks 1–4b passes; no test failures were observed. With `OPENNEST_RUN_FILL_PERF` unset the Release category run skips all six benchmarks (verified: 6 skipped / 0 failed).

### Final-tree opt-in benchmark reproduction

All six `Category=FillPerformance` cases passed enabled on the final tree in one run. Medians reproduce the per-task delivered numbers within shared-VM process noise (no before/after pair was rerun here; each task section retains its own same-harness pairs):

| Case | Final-tree median | Delivered reference |
| --- | --- | --- |
| unequal-counts actual | 1.583 ns/call | 1.878 ns/call (Task 1) |
| unequal-counts reference | 11,956.654 ns/call | 11,979.646 ns/call (Task 1) |
| equal-count control actual/reference | 11,164.062 / 11,190.333 ns/call | same range as reference (Task 1) |
| group-pattern default / custom | 640.955 / 635.597 µs/call | overlapping-range control (Task 1b) |
| extents spacing 0 / 0.5 | 2,188.589 µs/call, 839,480 B/call; 2,201.340 µs/call, 1,028,400 B/call | 839,624 / 1,028,544 B (Task 2/3) |
| feature full / scalar-only | 163.135 / 29.558 µs/call (−81.9%); Δ 1,080.2 B/call | −80.1%; 1,080 B (Task 4a) |
| rotated-pattern angle 0 / 0.37 | 15.923 / 30.306 µs/call | Task 3 control |
| no-model-angles | 2.647 µs/call, 5,960 B/call | 2.632 / 2.623 µs/call, 5,960 B/call (Task 4b) |

### First whole-job before/after corpus measurement

Inspecting `OpenNest.Benchmark/Program.cs` first, as planned: the CLI accepts a `.nest` file, folder, or `*.manifest.json`; registered engines are `Default`, `Strip`, `Vertical Remnant`, `Horizontal Remnant`, `StockLadder`; no `Engines/` plug-ins are installed. The repository contains no `.nest` corpus, so a small representative corpus was built from the user-supplied production archive `/mnt/rogers/PEP Drawings/Archive/` (27,722 files, 27,420 of them `.dxf` by case-insensitive name) as a four-DXF manifest — a 1.5-inch tube with tapped hole, a 17×13 bent plate (`1214 A02 PT04`), a 4.44×2 bracket (`1000-164-11`), and a 1.5-inch pipe saddle, quantities 60/28/45/36 (169 parts), one 48x96 sheet candidate, part/edge spacing 0.5, rotation allowed. Manifest SHA-256 `3f13c7a674b2a33a26f31dcbf8fec3fc70ab7ef7f10bd801fb784d3d9179987b`; DXF SHA-256s `c44ee5f49a8b0bccd810b202fcc96f065bac5e1b49b51b5073003116011b1852`, `a8155c68ac30e165743676f68c339c03f4633945ccd1559c4e929c0f8d6f8f2c`, `3dff4381653e1bf7843f25dd8d4e09a253393e320285ec29faabea37c8270e59`, `366e2e3fceea673d0563ae6a16feac4e93b7867ed6b8313d9c8841cf40c6ff01`. Selection was bounded to a handful of drawings per the user's instruction.

Corpus limitations recorded honestly: circle-heavy archive parts (small washers, OD plates) validate INVALID at baseline `1b862dc` — identical on both trees, a pre-existing spacing-validation behavior outside this batch's scope — so they were excluded rather than papered over. At higher quantities (280 parts) both trees crash identically with `InvalidOperationException: Candidate placements violate required part spacing`, again baseline-equal and out of scope. The accepted workload is the 169-part job that validates cleanly on both trees.

Before tree: baseline `1b862dc` built via a separate git worktree. Final tree: `1e8e532`. Neither build has `Models/angle_predictor.onnx`, so both run under the same model-absent conditions with Task 4b's fallback behavior preserved (the executed paths intentionally differ: baseline extracts features before discovering prediction is unavailable, the final tree checks availability first). Four alternating baseline/final process repetitions each, `--engines Default,StockLadder --parallel 1`, same manifest and arguments; no salvage options were passed, so salvage credit resolves disabled on both trees. Each repetition is a fresh process (no process-global or JIT state carries across repetitions), and each solve builds its own `NestJob` snapshot and materialized drawings; within one repetition the two engine solves run sequentially in that process (StockLadder first, then Default). No `.nest` best-fit caches are involved and DXF import happens once during manifest loading, outside the timed region. Reported `Time(ms)` starts before `BuildNestJob` and covers geometry-snapshot construction, solving, materialization and validation per `BenchmarkRunner`, and no whole-job allocation totals were measured. Wall-clock time per solve:

| Engine | Baseline times (ms) | Final times (ms) | Median Δ |
| --- | --- | --- | --- |
| Default | 41,108 / 40,711 / 40,396 / 40,553 | 40,466 / 40,689 / 40,229 / 40,939 | −0.13% |
| StockLadder | 1,216 / 1,164 / 1,108 / 1,171 | 1,156 / 1,197 / 1,134 / 1,165 | −0.60% |

All eight runs classify identically: valid, fully placed 169/169, cost 9,216.00, 2 plates (48x96×2), empty validation notes — the acceptance gate's valid/invalid, requested/placed, and cost non-regression requirements pass on outcomes. Baseline run-to-run spread alone is 712 ms (Default) and 108 ms (StockLadder), wider than the medians' differences and overlapping the final ranges, so no reproducible material timing difference was observed in either direction — timing is inconclusive, not a measured equivalence. That fits scope: the hot phases on this job (NFP pair evaluation, BestFit sweeps, validation) were not touched by Tasks 1–4b, whose measured wins are localized to comparer dispatch, discarded extents pitch geometry, redundant bounds walks, and the no-model feature path. No scheduling was altered to influence these results.

Preserved raw evidence (outside the repository): manifest `pep-archive-benchmark-manifest.json`, per-run `baseline-rep1..4` / `final-rep1..4` CSV and console logs, and the full-tree benchmark log `fillperf-final-tree.log`, all under `/home/aj/extracted/2026-09-26/`. Reproduce with:

```bash
dotnet OpenNest.Benchmark/bin/Release/net8.0/OpenNest.Benchmark.dll \
  /home/aj/extracted/2026-09-26/pep-archive-benchmark-manifest.json \
  --engines Default,StockLadder --parallel 1 --csv <path>
```

### Batch-level conclusion and limits

- Removed work is demonstrated by the deterministic Debug counters and red/green evidence in each task section; correctness is carried by the final-tree 1,335/1,356/300 passing suites; measurable local gains exist where allocation or scanning work was actually eliminated (Tasks 2, 3, 4a, 4b), with timing claims bounded to their same-harness pairs.
- The whole-job corpus measurement above confirms identical valid classification, placed/requested counts, and cost on one small real-drawing job, with timing differences inside baseline run-to-run spread — inconclusive timing, not a measured performance equivalence, and not a speedup claim. A representative production `.nest` corpus at scale, Windows UI runtime testing, and actual ONNX-model inference remain untested.
- Gated follow-ups A (offset-geometry reuse), B (sorted overlap broad phase), and C remain unauthorized and were not started.

## Follow-up A — reuse FillLinear offset geometry — 2026-09-26

### Status and exact-layout acceptance

Accepted after resolving an initial exact-layout stop. The first whole-job comparison (normal thread pool, `whole-*` runs below) showed different placements between before and after runs, so implementation work stopped at that gate. Acceptance rests on the serialized corpus comparison below, the frozen-reference differential tests (including concurrent calls on one instance), and inspection that the cache is call-local and never shared. Under normal scheduling, the observed differences are most plausibly pre-existing tie-breaking nondeterminism; the evidence:

- Under the normal thread pool the before tree alone produced two distinct exact layouts across its three runs (`whole-before-1` differs from the identical `whole-before-2`/`-3`), and one after run (`whole-after-2`) reproduced the dominant before layout bit-for-bit. The first observed difference, `whole-before-1` versus `whole-after-1` at `Plates[0].Placements[0]` (`1214 A02 PT04`, before X/Y/rotation `8.312499999999998`/`19.093722188575505`/`4.71238898038469`, after `2.5000000000000013`/`8.864574062858505`/`1.5707963267948966`), is also exactly the difference between `whole-before-1` and `whole-before-2`.
- A plausible mechanism (not proven here) is that parallel producers (`FillHelpers.FillPattern`, `PairEvaluator.EvaluateAll`, `BestFitFinder` strategy fan-out) collect into `ConcurrentBag` and later selection keeps the first of equal-scoring results, so ties follow thread completion order.
- Determinism probe: a small console program (source and project files retained with the evidence) calls `DxfManifestLoader.Load` and `NestingEngineRegistry.Create("Default").Solve(...)` directly with `DOTNET_PROCESSOR_COUNT=1` and the thread pool capped at one worker (`SetMinThreads(1,1)`/`SetMaxThreads(1,1)`, verified before solving), then writes every placement's X/Y/rotation as IEEE-754 bits. Serial runs before-1, after-1, before-2, after-2 produced byte-identical files (SHA-256 `62b5a8d065c889e3599e3bd00e3f591502553c24e81962ccdcb06c4b06159708` for all four; 169 placements, two plates). The probe referenced each tree's own `OpenNest.Benchmark` project; its `OpenNest.Engine.dll` hashes matched the corresponding benchmark builds (`a43d9db5…` before, `ff2bc016…` after). That serialized layout also matches the dominant normal-pool layout, as a placement multiset ignoring order and instance numbering, seen in `whole-before-2`, `whole-before-3` and `whole-after-2`.
- An earlier probe variant that ran `Solve` on a one-thread custom `TaskScheduler` did not serialize the engine (`Parallel.For`/`Parallel.Invoke` do not inherit a custom scheduler) and gave identical before layouts (`3f00c783…`) but two different after layouts (`f571c52e…`, `2c9b9c35…`); it is superseded, and its output files were not retained.

Conclusion: with scheduling held fixed, the optimized tree's whole-job output is bit-for-bit identical to the base tree's on this corpus job. Serialized runs cannot exclude a difference that appears only under concurrency; that risk is covered by the concurrent differential test and the per-call cache ownership, not by this probe. Under normal parallel scheduling, neither tree gives reproducible exact layouts, so that comparison cannot serve as an exact-layout oracle. The scheduling nondeterminism is pre-existing and out of this slice's scope; it is recorded here, not fixed or tuned around. Evidence: `/home/aj/extracted/2026-09-26/followup-a/determinism-probe/` (probe source, projects, placement TSVs, and `det1-run-stdout.txt` with the exact invocation and per-run output) and `layout-comparison-blocker.json`. An independent spec review rebuilt the probe against isolated trees and reproduced the same SHA-256 in four fresh processes.

### Scope, design and preservation contract

`PartGeometry.GetOffsetPerimeterEntities(CNC.Program, spacing)` prepares a fresh local-frame perimeter and increments `OffsetPerimeterEntities` once. The Part overload delegates then translates that fresh list in place, preserving the old arithmetic/list semantics. No other PartGeometry method changed.

Each public `FillLinear.Fill` / `FillRow` entry owns a fresh private cache keyed by Program **reference identity**, with fixed half-spacing. Private calls thread that cache through the grid/tile/distance chain. Cached entities never escape: each world-space result clones then offsets each entity. Moving locations remain `part.Location + offset`, in the same addition order as `CloneAtOffset`. Lazy `??=` preparation follows unchanged bbox prefilters. Pair order, DirectionalDistance arguments, invalid-distance fallback, max-copy logic, tiling and overlap/bbox fallback are unchanged. No shared instance/static cache, overlap algorithm, Compactor, RotationSlideStrategy or StripeFiller change.

`LegacyFillLinear` was copied before production edits from `094c4c1`; undoing only the header, added imports, namespace, visibility and class/constructor names reproduces the base file exactly (formatter sorted the added imports). It is test-only and frozen. The independent entity oracle recomputes ConvertProgram → material filter → ShapeProfile → OffsetOutward → location translation, rather than comparing two delegating wrappers.

Rechecked `SpatialQuery.cs` lines 627–960: DirectionalDistance, RayEntityDistance, ExtractEntityVertices, ArcToLineClosestDistance and AddArcExtremeVertices use points/centers/radii/angles, not entity BoundingBox/Left/Right/Top/Bottom. Line.Offset incrementally translates its box, while Line.Clone recomputes it; the new code clones **local** prepared entities before world translation. Across the delivered entity oracle's 783 entity comparisons, type/order/layer/geometric fields and bounding boxes were bitwise equal (rectangle 216, concave 297, arc 216, circle 27, ring 27; zero bbox divergences). This fixture evidence is not a universal box-equivalence proof. Full-fill differential tests additionally compare bitwise output locations/boxes/rotations, drawing identity and Program-sharing equivalence. Empty programs preserve the pre-existing ArgumentOutOfRangeException; no valid nonnegative-spacing null-offset fixture was established.

Coverage includes both directions, zero/positive spacing, nonzero origins, orthogonal/non-orthogonal rotations, Drawing/Pattern/FillRow, shared-Program patterns, distinct rotations of one drawing, BuildRotatedPattern, adjacent-double last-copy thresholds, repeated calls and 48 bounded concurrent calls on one filler. The fallback fixture deliberately starts with overlapping seeds and proves legacy bbox fallback without claiming it repairs invalid seeds. Input geometry/program/location ownership is checked. The initial nominal rectangle threshold assumption failed two tests; it was replaced with adjacent-double threshold discovery using only the frozen reference, then passed.

### Provenance

Base `094c4c196bc793ef184fae15380a7c94544697de`, branch master. Ubuntu 24.04.5 LTS x64, shared KVM VM with four processors, SDK 10.0.112; measurement output reports .NET 8.0.31 and Stopwatch frequency 1,000,000,000 ticks/s. Before is the detached baseline worktree with **only** the identical benchmark test file copied in. The original base harness did not contain this new test. The manifest and all four referenced archive DXFs were reachable.

| Source | Before SHA-256 | Delivered/measurement SHA-256 |
| --- | --- | --- |
| `OpenNest.Engine/Fill/FillLinear.cs` | `c7dc33a67d1571e671b2a90dd0b9c585dce9c7e94d532891ed7b646189918b80` | `3a0dca084aa9b8f4e61e3ed836e49ed2cfbf311c92d5076460983c573cdcc061` |
| `OpenNest.Core/PartGeometry.cs` | `fb54f33fd029df2f76bfb30590c976f49995eb041a74c7863d1feafc6fddfc4c` | `5e3c8f9493afffb4b0db69ca6dcf0a738a591e6a636df476ed3b0d8cb6658894` |
| `OpenNest.Tests/Fill/FillPerformanceTests.cs` | `dd33805a723b2fa9068177490f0211ac707b555c46db7ce3e1c9d161c06beba7` | `dd33805a723b2fa9068177490f0211ac707b555c46db7ce3e1c9d161c06beba7` |
| Corpus manifest | `3f13c7a674b2a33a26f31dcbf8fec3fc70ab7ef7f10bd801fb784d3d9179987b` | same file/hash |

The benchmark depends only on APIs/helpers already present at the base. No harness mode was redirected to a reference implementation. Production and harness files were restored after mutation testing before measurements; their final hashes match measurement provenance.

### Genuine red/green and test evidence

Counters are Debug-only; Release zero counters prove nothing. All work assertions use FillCacheCollection and reset in finally.

| Workload | Legacy preparations | Always-miss RED actual / expected | Restored GREEN actual | Output parts |
| --- | ---: | --- | ---: | ---: |
| FillRow rectangle | 2 | 2 / 1 | 1 | 3 |
| Drawing Fill rectangle | 8 | 8 / 1 | 1 | 6 |
| Shared-Program pattern | 12 | 12 / 1 | 1 | 8 |
| Two rotated Programs from one drawing | 12 | 12 / 2 | 2 | 8 |

For RED, replaced the cache lookup/add block with an unconditional preparation in the delivered source: four failures, process exit 1. Restored in finally. Separately changed the key to BaseDrawing, passing Part into AtLocation: shared pattern passed, rotated and built-pair cases failed (2 failures, exit 1). Rotated expected 18 parts, actual 15; built-pair location bits expected 4626892942313738295, actual 4626987413290037556. Restored in finally, byte-for-byte source check passed. Then all 98 targeted Debug cases passed (exit 0).

Exact commands (each with `--logger 'console;verbosity=detailed' --logger 'trx;LogFileName=<name>.trx' --results-directory <evidence>`):

```bash
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug --filter FullyQualifiedName~FillLinearGeometryReuseTests.Work_
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug --filter FullyQualifiedName~FillLinearGeometryReuseTests.Pattern_Matches
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug --filter FullyQualifiedName~FillLinearGeometryReuseTests
```

| Suite / configuration | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Unmodified baseline main Release | 1337 | 18 | 0 |
| Unmodified baseline engine Release | 300 | 0 | 0 |
| Final-source targeted Debug | 98 | 0 | 0 |
| Always-miss mutation Debug | 0 | 0 | 4 |
| Wrong-drawing-key mutation Debug | 1 | 0 | 2 |
| Each of four enabled microbenchmark Release processes | 1 | 0 | 0 |
| Final main Release | 1431 | 19 | 0 |
| Final main Debug | 1456 | 19 | 0 |
| Final engine Release | 300 | 0 | 0 |
| Final targeted Release (`FillLinearGeometryReuseTests`) | 94 | 0 | 0 |
| `Category=FillPerformance`, variable unset / `=0` | 0 / 0 | 7 / 7 | 0 / 0 |

Counts come from individual TRX UnitTestResult outcomes, not aggregate notExecuted. Baseline skips: 12 optional CHR fixtures and six opt-in performance cases; final skips add the new opt-in `LinearGeometryReuse_ReportsPatternAndDrawing`. Main Release delta +94 passed equals the targeted Release count; Debug additionally runs the four `#if DEBUG` work cases (targeted Debug 98). Both disabled gates skip all seven benchmarks. Final-tree suites ran after the determinism investigation on unchanged production/test sources (hashes above).

### Same-harness Release microbenchmark observations

Production Fill only: closed 10×8 native-radius-1 right-corner-arc part; pair seeded at 0/π, second at (10.5,0), then BuildRotatedPattern at 0.37 radians; single Drawing at 0.37. Area `(3.1,5.3,96,48)`, spacing 0.5, Horizontal. Two warmup batches ×100 calls per mode, seven measured batches ×200 calls, alternating mode order including warmup. Four serial fresh processes: before-1, after-1, after-2, before-2. Setup/assertions/output excluded; geometry, tiling, overlap checks, GC, delegate/loop and nonallocating count consumption included. Current-thread synchronous allocations, not RSS. No forced GC. Every pattern batch consumes 7,200 parts (36/call); drawing consumes 8,000 (40/call).

Raw batches, µs/call / B/call:

| Process | Batch | Pattern | Drawing |
| --- | ---: | --- | --- |
| before-1 | 1 | 13890.929840 / 11978904 | 22417.643430 / 15334152 |
| before-1 | 2 | 13156.240410 / 11978904 | 22520.495810 / 15334152 |
| before-1 | 3 | 13412.731580 / 11978904 | 22479.693845 / 15334152 |
| before-1 | 4 | 13284.240955 / 11978904 | 22318.351345 / 15334152 |
| before-1 | 5 | 13139.308525 / 11978904 | 22309.902190 / 15334152 |
| before-1 | 6 | 13262.782455 / 11978904 | 22268.902850 / 15334152 |
| before-1 | 7 | 13018.847040 / 11978904 | 22367.989855 / 15334152 |
| after-1 | 1 | 12025.202410 / 8640400 | 20573.556690 / 12179776 |
| after-1 | 2 | 11512.053070 / 8640400 | 20487.712005 / 12179776 |
| after-1 | 3 | 11656.403895 / 8640400 | 20300.006810 / 12179776 |
| after-1 | 4 | 11628.209575 / 8640400 | 20608.516540 / 12179776 |
| after-1 | 5 | 11590.578130 / 8640400 | 20591.376225 / 12179776 |
| after-1 | 6 | 11741.157625 / 8640400 | 20960.479975 / 12179776 |
| after-1 | 7 | 11603.496715 / 8640400 | 20470.246675 / 12179776 |
| after-2 | 1 | 12322.696725 / 8640400 | 20581.793035 / 12179776 |
| after-2 | 2 | 11570.155470 / 8640400 | 20518.170995 / 12179776 |
| after-2 | 3 | 11512.081200 / 8640400 | 20538.707185 / 12179776 |
| after-2 | 4 | 11700.329105 / 8640400 | 20246.447735 / 12179776 |
| after-2 | 5 | 12010.912020 / 8640400 | 20708.860945 / 12179776 |
| after-2 | 6 | 11509.263120 / 8640400 | 20560.583720 / 12179776 |
| after-2 | 7 | 11499.249420 / 8640400 | 20658.498240 / 12179776 |
| before-2 | 1 | 14218.554935 / 11978904 | 22026.400520 / 15334152 |
| before-2 | 2 | 13131.679745 / 11978904 | 22372.373875 / 15334152 |
| before-2 | 3 | 13206.351480 / 11978904 | 22058.792355 / 15334152 |
| before-2 | 4 | 13092.448795 / 11978904 | 21892.855215 / 15334152 |
| before-2 | 5 | 13108.256065 / 11978904 | 22404.148505 / 15334152 |
| before-2 | 6 | 13450.579065 / 11978904 | 22287.616975 / 15334152 |
| before-2 | 7 | 13139.747450 / 11978904 | 21835.373995 / 15334152 |

| Process / mode | µs/call min / median / max | B/call min / median / max |
| --- | --- | --- |
| before-1 / pattern | 13018.847040 / 13262.782455 / 13890.929840 | 11978904 / 11978904 / 11978904 |
| before-1 / drawing | 22268.902850 / 22367.989855 / 22520.495810 | 15334152 / 15334152 / 15334152 |
| after-1 / pattern | 11512.053070 / 11628.209575 / 12025.202410 | 8640400 / 8640400 / 8640400 |
| after-1 / drawing | 20300.006810 / 20573.556690 / 20960.479975 | 12179776 / 12179776 / 12179776 |
| after-2 / pattern | 11499.249420 / 11570.155470 / 12322.696725 | 8640400 / 8640400 / 8640400 |
| after-2 / drawing | 20246.447735 / 20560.583720 / 20708.860945 | 12179776 / 12179776 / 12179776 |
| before-2 / pattern | 13092.448795 / 13139.747450 / 14218.554935 | 11978904 / 11978904 / 11978904 |
| before-2 / drawing | 21835.373995 / 22058.792355 / 22404.148505 | 15334152 / 15334152 / 15334152 |

### Whole-job measurement

Same real 169-part, four-DXF corpus manifest as Task 5; Default only, `--parallel 1`, six fresh processes in B,A,B,A,B,A order. No StockLadder run. DXF import precedes timing. With `--output`, Time(ms) includes BuildNestJob, Solve, materialization, validation/scoring preparation **and output .nest/JSON serialization** through Stopwatch.Stop in BenchmarkRunner. No whole-job allocation measurements. Both builds lacked an installed angle model.

| Run | Time (ms) | Valid | Placed / requested | Cost | Plates |
| --- | ---: | --- | --- | ---: | ---: |
| before-1 | 40636 | True | 169 / 169 | 9216.00 | 2 |
| after-1 | 18904 | True | 169 / 169 | 9216.00 | 2 |
| before-2 | 40932 | True | 169 / 169 | 9216.00 | 2 |
| after-2 | 18810 | True | 169 / 169 | 9216.00 | 2 |
| before-3 | 40715 | True | 169 / 169 | 9216.00 | 2 |
| after-3 | 18451 | True | 169 / 169 | 9216.00 | 2 |

Before min/median/max: 40636 / 40715 / 40932 ms; after: 18451 / 18810 / 18904 ms. All runs are valid, fully placed and have empty validation notes. Ranges do not overlap: after-tree median is 53.8% lower than before (−21,905 ms) on this one corpus job, on a shared four-vCPU VM. That is a single-job observation, not a general speedup guarantee. The separate one-worker determinism probe (Status section) shows the same direction: 47,328/47,446 ms before versus 24,095/23,182 ms after. Its timings are only indicative because it runs on a single worker.

Exact comparison reads each JSON's ordered Plates/Placements and compares double IEEE-754 bytes; PartId maps to names from saved .nest/nest.json. The first pair's JSON and .nest poses agree within each run. Additionally compared placement multisets without instance index/order. No tolerance or coordinate rounding was used. Exact layout classes over the six normal-pool runs, as placement multisets: {before-2, before-3, after-2} identical; before-1, after-1 and after-3 each distinct. Because the before tree alone yields multiple classes, exact-layout acceptance rests on the serialized determinism probe, not on these runs. Evidence: `layout-comparison-blocker.json` plus every raw output directory.

### Reproduction, limitations and remaining work

```bash
# Prefix both scoped formatter commands with EnableWindowsTargeting=true on Linux.
dotnet format OpenNest.sln --include OpenNest.Core/PartGeometry.cs OpenNest.Engine/Fill/FillLinear.cs OpenNest.Tests/Fill/LegacyFillLinear.cs OpenNest.Tests/Fill/FillLinearGeometryReuseTests.cs OpenNest.Tests/Fill/FillPerformanceTests.cs
# Repeat exactly with --verify-no-changes.
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release   --filter FullyQualifiedName~LinearGeometryReuse_ReportsPatternAndDrawing --logger 'console;verbosity=detailed'
dotnet OpenNest.Benchmark/bin/Release/net8.0/OpenNest.Benchmark.dll   /home/aj/extracted/2026-09-26/pep-archive-benchmark-manifest.json   --engines Default --parallel 1 --csv <path> --output <directory>
```

Unqualified solution formatter and verify each initially exited 1 (`Restore operation failed`); explicit restore diagnosed NETSDK1100, Windows targeting disabled. With environment `EnableWindowsTargeting=true`, the same solution/include formatter and verify each exited 0. No project files or broader source formatting changed. `git diff --check` exited 0. AGENTS.md gained one clause on the per-call `FillLinear` Program cache and its lifetime; CLAUDE.md is unchanged. Combined size is 31,610 bytes (AGENTS.md 31,599 + CLAUDE.md 11), under the 32,768-byte limit. README and the pre-existing untracked planning files were untouched.

Timings are from a shared four-vCPU VM, one synthetic micro workload and one corpus; not general latency guarantees. When ranges overlap, timing is inconclusive, never evidence of unchanged performance. No Windows runtime tests or ONNX accuracy/inference checks, no optional StockLadder measurement. The normal-pool nondeterminism was characterized (serializing removes it and it appears in the before tree alone) but its source was not pinned down, and no scheduler or tie-break change was made.

Raw logs, TRX, CSV, all saved .nest/JSON layouts, provenance and computed measurement summary are retained under `/home/aj/extracted/2026-09-26/followup-a/`. `micro-commands.json` and `whole-commands.json` record exact process commands/cwds. Final suite logs and TRX are under `final/`, and the determinism probe under `determinism-probe/`.

## Follow-up B′ — overlap-check preparation reuse (Slices 1a, 1b, 2a) — 2026-09-27

### Status, scope and exact-layout acceptance

This replaces the original Follow-up B (X-sorted overlap broad phase). A profile of `82feb78` on the corpus job showed that overlap checks took about 53% of main-thread wall time, but the bounding-box loop itself had almost no self time. The cost was per-pair preparation inside `Part.Intersects`: rebuilding each polygon from its `Program`, triangulating both polygons, and computing crossing points that overlap-only callers discard. Three slices remove that work without changing any verdict:

| Slice | Commit | Change |
| --- | --- | --- |
| 1a | `2b5485f` (harness `a98a49c`) | `Collision.HasOverlap` skips crossing points; `Check` is unchanged. |
| 1b | `a27290a` | `PartOverlapChecker`: each distinct `Program` (reference identity) and each part's world polygon is prepared once per `HasOverlappingParts` call; both loops use it. |
| 2a | this change | The checker also triangulates each part at most once per call, lazily after the bounding-box gate, and reuses those triangles for later pairs. |

Loop order, bounding-box prefilter, early exit, returned indices and `Part.Intersects` (which still returns crossing points) are unchanged. Frozen test-only oracles hold the pre-change code: `OpenNest.Tests/Geometry/LegacyCollision.cs` and `OpenNest.Tests/Fill/LegacyPartOverlap.cs`.

Exact-layout acceptance follows Follow-up A. The serialized probe (`DOTNET_PROCESSOR_COUNT=1`, one pool thread) produced the same placement file on base, 1a, 1b and 2a: SHA-256 `62b5a8d065c889e3599e3bd00e3f591502553c24e81962ccdcb06c4b06159708` (base ×2, 1a ×1, 1b ×2, 2a ×2).

Under the normal thread pool, base alone produced three distinct placement multisets in three untimed runs. 4 of 6 untimed 1b/2a runs match one of them bit for bit. The other two share one class not seen in this small base sample. That class is not proven equivalent, but it is consistent with base producing a new class on every run. All nine runs were valid, placed 169/169 on 2 plates at the same cost, and had no violations.

Step 2b is not implemented. It would triangulate per `Program` in the local frame, and a different frame can move triangle bounds by ulps and flip near-contact verdicts. "Validate on promotion only" and StripeFiller span reuse remain listed follow-ups.

### Provenance

- **Environment:** Ubuntu 24.04 x64 on a shared four-vCPU KVM VM; SDK 10.0.112; .NET 8.0.31 runtime.
- **Before trees (detached worktrees):**
  - For 1a and 1b: base `82feb78`, with the then-uncommitted harness byte-copied in.
  - For 2a: the 1b tree.
- **2a after tree:** `2b5485f` + 1b + 2a. Its Core and Engine are byte-identical to `a27290a` + 2a.
- **Harness:** `OpenNest.Tests/Fill/OverlapCheckPerformanceTests.cs`, SHA-256 `efde386b150614c8ce21c101df1f3435e0de80f1aa2936e1161044cb2653bdea`, identical in every before and after tree.
- **Corpus manifest:** SHA-256 `3f13c7a674b2a33a26f31dcbf8fec3fc70ab7ef7f10bd801fb784d3d9179987b`, the same file as Task 5 and Follow-up A.
- **Delivered 2a sources:**
  - `Collision.cs`: `aac902bfbb55c4ce0c4e95f2e17d53aad84b95e3695459f6ed8f9eeaec27abe8`
  - `PartOverlapChecker.cs`: `606a540407df8ed5c47834070cc6aa712da419a7808ef7b96dc9cb1c6a509e46`

Task 0 work counts (instrumented copy of base, corpus job):

| Work | Base | After 1b | After 2a |
| --- | ---: | ---: | ---: |
| Exact pair tests | 65,410 | 65,410 | 65,410 |
| Polygon builds | 65,410 | 3,250 (sum of distinct Programs per call) | 3,250 |
| Triangulations | 65,410 | 65,410 | 45,476 |

Triangulations fall less than polygon builds because most exact-tested parts meet only one neighbour.

The corpus in-fill checks ran 32,705 exact tests and found zero overlaps. They still decide local fallbacks, so they were made cheaper, not removed.

### Genuine red/green, mutations and tests

Counters are Debug-only (`CrossingPointScans`, `OverlapPolygonPreparations`, `PolygonTriangulations`), so Release zeros prove nothing.

| Evidence | Result |
| --- | --- |
| 1a RED: `HasOverlap` pointed back at the full path | work test fails, 5 crossing-point scans vs 0 expected |
| 1b RED: loops back on uncached `Part.Intersects` | preparations 246 vs 1 (single-Program grid), 64 vs 2 (pair grid) |
| 1b mutant: world polygon offset +1e-6 | bitwise world-polygon test fails |
| 1b mutant: world polygon cached per Program, not per part | 22 failures, including the existing FillLinear layout differentials |
| 2a RED: triangles not cached | triangulations 246 vs 40, 64 vs 36 (= parts reaching an exact test) |
| 2a mutant: triangles shared per Program | 23 failures, including the FillLinear layout differentials |

Differentials:

- **Collision vs `LegacyCollision`:** 50,000 seeded `HasOverlap` decisions and 2,400 bitwise `Check` comparisons.
- **Cached triangles:** 49,000 decisions (22,027 overlaps; 4,146 bounding-box hits that are clear), with triangles cached once per polygon and reused across all pairs. Afterwards the triangles are still bit-identical to a fresh triangulation.
- **Fill grids vs `LegacyPartOverlap`:** verdicts, indices and world polygons are bit-identical across 5 shapes × 2 spacings × 3 angles × 2 directions, with ±1e-9 and 0.05 shifts. Also covered: shared, rotated and built-pair patterns; an overlapping seed; touching and epsilon gaps; scribe-only, rapid-only and empty programs; concurrent calls.

The review audited every clipping and hole-subtraction path in `Collision.cs` and `ConvexDecomposition.cs`: cached triangles and their vertex lists are only read.

Invalid-input carve-out (1a onward): a polygon whose `Vertices` is null still throws when bounding boxes overlap. The exception type changed from `NullReferenceException` (thrown in `ToLines`) to `ArgumentNullException` (thrown in triangulation), including through the prepared-triangles overload. No production code creates such polygons or catches either exception type. Separated boxes still return false without touching vertices or resolving triangles.

| Suite / configuration (TRX outcomes) | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Base main Release (`82feb78`) | 1439 | 19 | 0 |
| 1a main Release / Debug | 1451 / 1475 | 20 / 20 | 0 |
| 1b main Release / Debug | 1496 / 1524 | 20 / 20 | 0 |
| 2a main Release / Debug | 1496 / 1526 | 22 / 22 | 0 |
| Engine Release (each slice) | 300 | 0 | 0 |
| IO Release (each slice) | 41 | 0 | 0 |
| 2a targeted Release / Debug | 325 / 346 | 1 / 1 | 0 |

The two extra 2a skips are the CI Fiber sample regressions. They skip because the gitignored `OpenNest.Tests/test-config.json` was absent in the 2a worktree. For 1b and 2a, clean `EnableWindowsTargeting=true` solution builds give 47 warnings and 0 errors, identical to their bases (independent reviews).

### Same-harness Release microbenchmark observations

The benchmark is `OverlapCheck_ReportsPolygonPairsAndGridChecks`. Setup: area (3.1, 5.3, 96, 48), spacing 0.5, Horizontal. Modes:

- **`grid-single`:** `FillLinear.Fill` of the arc fixture at 0.37 rad (40 parts, one Program), checked through `FillHelpers.HasOverlappingParts`.
- **`grid-pair`:** a 0/π concave pair via `BuildRotatedPattern` at 0.37 rad (36 parts), checked the same way.
- **`polygon-pairs`:** `Collision.HasOverlap` timed over the 155 bounding-box-overlapping neighbour pairs, with polygons prepared once.

Every verdict is clear. Each process warms up with 2 × 20 calls per mode, then measures 7 batches × 50 calls, rotating mode order per batch. Processes ran in the order before-1, after-1, after-2, before-2.

The grids are smaller than the planned ~100–200 parts. Grid size only scales the pair count, and the corpus job is the size-representative measurement.

Slice 1a (before = base plus harness):

`polygon-pairs` (µs/call per batch 1–7; B/call is constant across batches):

| Process | µs/call, batches 1–7 | B/call |
| --- | --- | ---: |
| before-1 | 19176.5 / 18973.2 / 18913.7 / 18944.3 / 18040.0 / 17797.8 / 17851.1 | 10,030,640 |
| after-1 | 10399.9 / 11020.1 / 10887.9 / 10221.8 / 10459.4 / 10185.3 / 10285.5 | 7,921,168 |
| after-2 | 10462.4 / 10669.8 / 10346.5 / 10498.0 / 10651.5 / 10547.5 / 10413.3 | 7,921,168 |
| before-2 | 19033.7 / 18849.8 / 18593.8 / 18340.6 / 18633.5 / 17674.8 / 17724.0 | 10,030,640 |

Slice 1b (before = base plus harness; after = 1a plus 1b):

`grid-single` (µs/call per batch 1–7; B/call is constant across batches):

| Process | µs/call, batches 1–7 | B/call |
| --- | --- | ---: |
| before-1 | 21360.5 / 21387.9 / 20873.0 / 21314.1 / 20192.0 / 20093.7 / 19978.9 | 11,897,168 |
| after-1 | 10425.0 / 10207.0 / 10164.8 / 10432.8 / 10418.8 / 10170.6 / 10457.9 | 7,860,696 |
| after-2 | 10398.3 / 10480.3 / 10418.6 / 10599.3 / 10750.0 / 10839.8 / 10677.2 | 7,860,696 |
| before-2 | 21816.8 / 20794.4 / 21098.9 / 20600.5 / 20408.2 / 20278.8 / 20122.4 | 11,897,168 |

`grid-pair` (µs/call per batch 1–7; B/call is constant across batches):

| Process | µs/call, batches 1–7 | B/call |
| --- | --- | ---: |
| before-1 | 1023.5 / 1027.3 / 1019.3 / 989.2 / 1017.6 / 992.6 / 989.4 | 465,408 |
| after-1 | 272.0 / 280.1 / 278.1 / 284.2 / 272.7 / 284.6 / 273.7 | 133,144 |
| after-2 | 282.5 / 270.1 / 303.8 / 282.4 / 276.9 / 286.7 / 286.6 | 133,144 |
| before-2 | 1046.7 / 1042.3 / 1017.0 / 995.5 / 1110.9 / 1026.7 / 1022.5 | 465,408 |

Slice 2a (before = 1b):

`grid-single` (µs/call per batch 1–7; B/call is constant across batches):

| Process | µs/call, batches 1–7 | B/call |
| --- | --- | ---: |
| before-1 | 10625.9 / 10874.6 / 10452.9 / 10305.0 / 10455.8 / 10542.9 / 10581.2 | 7,860,696 |
| after-1 | 3867.9 / 3787.2 / 3462.5 / 3544.0 / 3443.5 / 3414.5 / 3776.1 | 5,308,808 |
| after-2 | 3812.0 / 3488.3 / 3704.7 / 3464.9 / 3585.1 / 3678.8 / 3585.7 | 5,308,808 |
| before-2 | 10566.4 / 10763.5 / 10646.8 / 10641.7 / 10476.5 / 10780.6 / 10749.1 | 7,860,696 |

`grid-pair` (µs/call per batch 1–7; B/call is constant across batches):

| Process | µs/call, batches 1–7 | B/call |
| --- | --- | ---: |
| before-1 | 284.5 / 263.5 / 279.4 / 284.0 / 272.8 / 278.6 / 281.3 | 133,144 |
| after-1 | 250.5 / 232.8 / 188.1 / 177.4 / 179.7 / 181.4 / 184.6 | 97,016 |
| after-2 | 182.1 / 184.7 / 189.7 / 176.1 / 192.2 / 239.9 / 182.2 | 97,016 |
| before-2 | 275.7 / 297.5 / 286.6 / 280.5 / 277.6 / 275.4 / 290.8 | 133,144 |

Medians per process (before-1 / after-1 / after-2 / before-2, µs):

| Slice | Mode | Medians |
| --- | --- | --- |
| 1a | `polygon-pairs` | 18,914 / 10,400 / 10,498 / 18,594 |
| 1b | `grid-single` | 20,873 / 10,419 / 10,599 / 20,600 |
| 1b | `grid-pair` | 1,018 / 278 / 283 / 1,027 |
| 2a | `grid-single` | 10,543 / 3,544 / 3,586 / 10,647 |
| 2a | `grid-pair` | 279 / 185 / 185 / 280 |

In every mode, the after and before batch ranges do not overlap.

### Whole-job measurement

This is the same corpus job as Task 5 and Follow-up A: Default engine only, `--parallel 1`, Release, alternating fresh processes, and timing runs without `--output`. The CSV `ElapsedMs` covers the engine run, not DXF import.

| Comparison | Before runs (ms) | After runs (ms) | Median before → after |
| --- | --- | --- | --- |
| Base `82feb78` → 1a+1b | 18926, 18734, 18844, 19061 | 13572, 13175, 13459, 13468 | 18,885 → 13,464 (−28.7%) |
| 1b → 1b+2a | 13402, 13544, 13353, 13394 | 12788, 13113, 12615, 12554 | 13,398 → 12,702 (−5.2%) |

- **Run order:** B,A,B,A,B,A,A,B. Before and after ranges do not overlap in either comparison.
- **Outcomes:** all 16 runs were valid and none crashed. Each placed 169/169 on 2 plates, with utilization 0.5346 and cost 9,216.
- **Cumulative figure:** the two comparisons ran in separate batches, so the cumulative base → 2a improvement (about −33%) is indicative only.
- **Serialized probe times** (one process each) are not timing claims: base 23,518/23,607 ms; 1a 23,286; 1b 17,762/17,651; 2a 16,389/16,945.

Profile after 1b (dotnet-trace, main-thread root samples):

| Frame | Share |
| --- | ---: |
| `PartOverlapChecker.Overlaps` (total) | 35.0% |
| — `TriangulateWithBounds` | 27.1% |
| — clipping | about 4.4% |
| `ConvertProgram.AddProgram` (Program and polygon preparation) | 2.1% |

Triangulation dominating is what justified 2a. After 2a, the remaining overlap cost is mostly triangulating each part once, plus clipping.

### Reproduction and limitations

```bash
# Linux: prefix both scoped formatter commands with EnableWindowsTargeting=true.
dotnet format OpenNest.sln --verify-no-changes --include <changed .cs files>
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Debug --filter 'FullyQualifiedName~CollisionOverlapOnlyTests|FullyQualifiedName~PartOverlapCheckerTests'
OPENNEST_RUN_FILL_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter FullyQualifiedName~OverlapCheck_ReportsPolygonPairsAndGridChecks --logger 'console;verbosity=detailed'
dotnet OpenNest.Benchmark/bin/Release/net8.0/OpenNest.Benchmark.dll /home/aj/extracted/2026-09-26/pep-archive-benchmark-manifest.json --engines Default --parallel 1 --csv <path>
```

Limitations:

- The timings come from one shared VM, one synthetic micro workload and one real corpus job; they are not general latency guarantees.
- Serialized identity covers one thread schedule. Concurrency-only differences are covered by per-call cache ownership and the concurrent differential tests, not by the probe.
- No Windows runtime tests were run.

Raw logs, TRX files, CSVs, placement files, profiles, probe sources and reviews are retained under `/home/aj/extracted/2026-09-27/followup-b/` (`task0/`, `slice1a/`, `slice1b/`, `slice2a/`, `layout-classes/`).
