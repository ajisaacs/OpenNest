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
