# Small synthetic-nest validity gate

From the repository root, with .NET 8 and Python 3.9 or later:

```sh
python3 scripts/check-synthetic-nests.py
```

The runner builds the existing `OpenNest.Benchmark` in Release and invokes it with
`--engines Irregular --parallel 2 --progress`. It prints the evidence directory;
`build.log`, `benchmark.log`, `results.csv` and (only on success) `accepted.json`
are retained there. Use `--output <directory>` to choose a durable location.
Progress is written to `benchmark.log`, not streamed to the terminal.

The entire build + benchmark has a 300-second watchdog. Nonzero subprocess exits,
cancellation and timeout fail the gate. On Unix the runner kills its owned process
session, including descendants; Windows uses `taskkill /T /F`. Build servers are
disabled for this check. `--timeout <seconds>` may shorten, never extend, the budget.
Default concurrency is two; `--parallel 1` is serial and `--parallel 4` is the upper
bound for a machine with adequate resources. Do not use the override to oversubscribe
CI or a busy host.

## The six committed fixtures

These are wholly synthetic, generated from CNC polylines through `Drawing`, `Nest`
and `NestWriter`. No drawing archives or customer jobs are involved. All use inches,
quadrant 1, part spacing 0.25, edge spacing 0.25 on every side, and no salvage credit.
Sheet dimensions below are X length by Y width (the `Size` API takes width first).

| File stem | Synthetic demand | Sheet X × Y | Expected sheets |
| --- | --- | --- | --- |
| `single-triangle` | one 6 × 5 triangle | 20 × 20 | 1 |
| `paired-wedges` | two wedges, vertices (0,0), (8,0), (6,20), (0,20) | 30 × 15 | 1 |
| `repeated-ell-fill` | seven 9 × 7 L shapes, arm thickness 3 | 60 × 40 | 1 |
| `mixed-irregular` | one triangle, two L shapes, three pentagons | 30 × 25 | 1 |
| `rotation-edge-fit` | one 8 × 3 rectangle that must turn to fit | 3.52 × 8.52 | 1 |
| `multi-sheet` | three 8 × 8 squares, only one fits each sheet | 10 × 10 | 3 |

All drawings permit automatic rotation. The edge-fit fixture deliberately leaves
0.02 extra work-area room: it is a tight rotated fit, not an exact-contact test.
The qty-two fixture checks one-sheet interlocking fulfillment; the qty-greater-than-two
fixture exercises a job eligible for Fill-block candidates. This smoke gate does not
assert which internal candidate won; the existing pair/block unit tests retain those
contracts. These are ordinary solid contours, not acceptance of future hole nesting,
containment sequencing or shop-use safety.

To regenerate the six archives through the repository's IO APIs:

```sh
dotnet run --project test-data/synthetic-nests/Generator/Generator.csproj -- test-data/synthetic-nests
```

The generator fixes dates/ZIP timestamps and leaves customer, maker and source-path
metadata empty. Inspect the generated diff before committing. It never reads external
DXFs. Fixtures contain demand and plate defaults, not prepacked layouts.

## Fail-closed result checking

The runner requires exactly the six expected input files, known positive demand and
exactly one Irregular result for each. `Baseline` rows are ignored, not accepted as
engine results. Unknown engines/jobs, duplicates, missing results, empty output,
invalid layouts, crashes, timeout notes, incomplete fulfillment, changed inputs and
unexpected sheet counts all fail. Benchmark's exit code zero alone is not acceptance.
CSV flags must be the exact emitted `True`/`False` values; placed/requested demand
must match the fixture contract. A saved CSV can be checked without solving:

```sh
python3 scripts/check-synthetic-nests.py --check-csv <results.csv>
python3 scripts/test_check_synthetic_nests.py -v
```

The unit checks use labeled synthetic CSV rows and a real watchdog process-tree
probe. The end-to-end command uses actual benchmark results and the production
independent layout validator. This gate supplements, never replaces, the existing
geometry, contract, cancellation, sequencing, posting and cross-platform unit suites.
GitHub's cross-platform workflow runs both the checker tests and the six-fixture gate
in a separate job, concurrently with four independent unit-suite jobs (main, Engine,
IO and Server). Unit jobs retain the default Debug configuration and do not cancel
siblings on failure. The final `tests` check requires the entire unit matrix and
synthetic job to succeed; failed, cancelled or skipped dependencies cannot produce
a green final check. This preserves the existing check name without changing branch
protection or removing tests. `python3 scripts/test_ci_results.py -v` exercises the
same fail-closed aggregate command used by CI. The six-minute step limit and
`--parallel 2` synthetic-runner limit are unchanged.

This fan-out removes inter-suite serialization, not individual geometry-test work.
It uses additional independent runners and repeats restore/build setup; actual wall
time depends on the slowest job and runner queueing. It is not a CPU speed claim or
a reason to skip correctness fixtures. Windows runtime acceptance is separate; a
Linux pass does not certify Windows tree termination or desktop/shop interactions.

## Validity is not speed

Parallel elapsed times are diagnostic only. Serial and parallel runs must account for
the same fixtures and fulfill the same demands; they do not promise identical poses.
Even `--parallel 1` here is a smoke gate, not a controlled performance comparison.
Actual speed claims need serial quiet, interleaved comparisons with frozen inputs and
source provenance. Full customer-corpus audits remain opt-in and uncommitted.
