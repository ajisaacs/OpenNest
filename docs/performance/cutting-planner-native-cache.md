# Cutting-planner native curve reuse

The opt-in 144-part synthetic grid probe measures only `CuttingPlanService.Plan`, not build,
fixture creation, assertions, desktop rendering or posting:

```sh
OPENNEST_RUN_CUTTING_PERF=1 dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~ReorderSearchTests.DenseGrid144_PerformanceProbe' \
  --logger 'console;verbosity=detailed'
```

Run separate fresh processes on the same machine with build and filesystem caches warm;
there is no in-process plan/JIT warmup, so these are first-call measurements. Compare
`Cutting144 elapsedMs`, `expansions`, `status` and `executionSha256`. The hash covers
expanded owned motions (including called hole programs), their layers, rapid flags,
exact coordinate bits, curve type and arc/circle parameters in source order; it does
not claim byte-identical authored G-code flags or post output. The test also requires
a Ready, independently replayed result and exact source-part conservation. Without
the environment flag it skips. Timing is diagnostic, not a deadline assertion.

The native curve wrapper now lazily owns one private native `Entity` for repeated
contact/containment queries. `ToEntity()` still makes a fresh object for other callers.
No lead or rapid check is skipped: long-lead/tiny-circle numerical contacts make a
fixed-extent material prefilter unsafe. Retest the cutting-planner and native-contact
regressions, especially near tangencies, repeated/concurrent queries and incomplete
material; do not infer posted NC or machine acceptance from this probe.
