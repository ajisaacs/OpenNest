# Problem-nest regression corpus

Every real nest that ever caused a problem is kept on a dedicated OpenNest server that holds
nothing else, not in this repository: they carry customer geometry and names. Each nest's comments
say what went wrong and which commit fixed it, and its `checks:` line names the areas it guards, so
a change runs only the nests that guard what it touches: plan or nest them and confirm none got
worse. Most guard Plan Cutting; `auto-nest` nests guard the nesting engines. Fills, the UI, posts
and file formats never need the corpus.

## Areas

Each nest names the areas it guards in a `checks:` comment line. Pick every area a change touches;
when a change spans the planner or you cannot tell, run the whole corpus. `part-order` and
`entries` change the order the search tries things on every plate, so naming either plans every
nest: a plate tagged with another area can still run out of budget after such a change.

| Area | Change touches |
|------|----------------|
| `part-order` | Whole-part order: learned order rules, stalls, budgets and the tours (`JointCuttingPlanSearch` order loop, `CuttingPartOrder`, `CuttingNestedOrder`, `CuttingDependencyGraph`, `CuttingPlanBatch`, the safest-route walk) |
| `entries` | Where and how each contour is entered: entry candidates and ranking, hole order, lead-ins, pierce zones (`ContourEntry*`, `PreferredContourEntries`, `CuttingHoleOrder`, `ScrapPierceSearch`, `JointCuttingPlanSearch` entry and recovery code, lead-in builders) |
| `contours` | Reading programs into contours: closure, seam overcuts, prepared contours (`ExecutionMotionReader`, `PreparedContours`, `IncrementalClosureDrift`, `SeamOvercut`, `NominalContourClosure`, `OwnedProgramCopy`) |
| `geometry-checks` | The checks a route must pass: rapid crossings, lead and material paths, replay, overlaps (`ReleasedContourState`, `LeadPathValidator`, `LeadMaterialSnapshot`, `ContourProgramVerifier`, `PlateOverlapAnalyzer`) |
| `cut-offs` | Cut-off segments in the plan (`CutOffInsertion`, `CutOffSegments`, `PlateSequencing` cut-off placement, automatic scrap cut-offs) |
| `grid` | Large plates: the aligned-grid serpentine, or any work done once per part or per pair that would grow on thousands of parts (`CuttingGridOrder`, per-part capture and checks) |
| `auto-nest` | Auto Nest's layout, not its cutting plan: the whole-job nesting engines (`OpenNest.Engine/NestingEngines/`) and what they share. Guarded by sheet count, not by planning |

A new planner area goes in `ProblemNestRegressionTests.PlannerAreas` and this table together. A misspelt area,
in a comment or the filter, fails the run rather than skipping nests.

## Running

```sh
# Windows PowerShell: $env:OPENNEST_PROBLEM_NESTS_SERVER = "http://<server>:<port>"
OPENNEST_PROBLEM_NESTS_SERVER=http://<server>:<port> \
  dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter "Category=ProblemNests" --logger "console;verbosity=detailed"
```

To plan only the nests guarding some areas, add `OPENNEST_PROBLEM_NESTS_CHECKS=part-order,entries`.
A nest guarding none of them is skipped and listed as skipped; a nest without a `checks:` line is
always planned. Without the filter every nest is planned.

Without the server variable the test skips, so routine runs never reach the network. With it, an
unreachable server or an empty corpus fails instead of passing silently.
`ProblemNestRegressionTests` downloads every nest on the server. A server holding more than 200
nests is refused, since it is not a corpus. It plans each plate that has parts on its own, the way Plan Cutting
does (whole-part reorder, falling back to the current order). It prints each plate's outcome,
search expansions and time. Each plate has a time limit covering both capture and planning:
120 s by default, or `OPENNEST_PROBLEM_NESTS_TIMEOUT` seconds. A plate that hits it is recorded
as `Timeout`, so a plate that never finishes cannot hang the run.

Each plate is ranked: `Ready` (replayed with no overlaps) above `BestEffort` above
`NoSolutionWithinBudget` above every blocked outcome (`UnsupportedGeometry`,
`ConstraintConflict`, `InvalidInput`, `Overlap`) above `Timeout`. The test fails when a plate ranks below its
baseline, when a nest has no baseline, or when the baseline's plate numbers differ from the
nest's. A different outcome at the same or a better rank is only reported. When it is
intended, update the baseline.

### Auto Nest nests

`ProblemNests_AutoNestNoWorseThanTheirBaselines` takes the nests whose `checks:` line names
`auto-nest` and runs the Default engine on their demand, the way the benchmark builds a job from a
saved nest: each drawing's required quantity, the nest's own sheet sizes, and the first plate's
spacing, quadrant and clamps. It fails when a layout breaks the layout check, leaves parts
unplaced, times out, or uses more sheets than the nest's `auto-nest: 2 sheets` comment line;
fewer sheets are only reported. Run it with `OPENNEST_PROBLEM_NESTS_CHECKS=auto-nest`; a filter
naming only planner areas skips it, and the planning test skips nests guarding only `auto-nest`
and runs not at all for a filter naming only `auto-nest`.

## Budget headroom

The planner's budget counts search expansions, not time, so a plate's outcome is repeatable, but
a plate that finds its route just inside the budget is fragile: any change to the order the
search tries parts or entries can push it over, into `NoSolutionWithinBudget`, without touching
the problem it guards. Each plate's line reports the expansions its applied strict search spent
against the plate's budget (`12000 of 20000 expansions (60%)`), and whether the new order ran out
before the current order was kept. Plates without a strict-search route (refusals, the
safest-route fallback, best-effort plans) report no share.

The run ends with the plates that spent at least 80% of their budget. They pass today and fail
next. Compare that list with the same run on master: a change that adds plates to it or raises
their share made the search worse, even when every plate still meets its baseline. Say so in the
change, and fix it, or explain why the plate's search grew.

A nest whose comments also carry a `planning with cut-offs: 1=Ready 2=BestEffort` line is
planned a second time, as Plan Cutting does after **Clear Lead-ins & Cut-offs** and **Add scrap
cut-offs** at their defaults, and each plate is held to that line the same way. Set
`OPENNEST_PROBLEM_NESTS_CUTOFFS=1` to plan every nest with cut-offs and print the outcomes
without comparing them, to record a new line.

Plates use their own cutting parameters. A plate without them uses line lead-ins of 0.15 at
90 degrees (external and internal) and no lead-outs.

## Adding a nest

1. In the desktop app, choose Database mode and the corpus server in **File > Storage Mode...**,
   then drag the `.nest` file onto the window. Keep the corpus server selected until the save in
   step 4 succeeds, then restore your previous storage settings.
2. In **Nest > Edit**, name it after the problem, and in the comments describe what failed
   and which commit fixed it, if any.
3. Add a baseline line to the comments, with plate numbers counted from the first plate:
   `planning: 1=NoSolutionWithinBudget 2=BestEffort`. An Auto Nest problem takes
   `auto-nest: 2 sheets` instead, the sheet count Auto Nest reaches today.
   To get the line, save without one and run the test: the failure prints the observed line.
   Add a `checks:` line naming the [areas](#areas) the problem was in, such as `checks: part-order`.
4. Save. Every save keeps the earlier versions, so a baseline change can be reviewed later.

Record the outcome the code produces today, not the outcome you want. A nest that is still
blocked guards against getting worse, and its baseline moves up once a fix lands.

## Sweeping a production archive

`ProductionCorpusSweepTests` (`Category=ProductionCorpus`) is a survey, not a gate: it plans
every occupied plate of every `.nest` file in a local folder the way Plan Cutting does after
**Clear Lead-ins & Cut-offs** (the parts regenerate from clean geometry, the plate's own cutting
parameters when it has them, else the nest's machine defaults over a cutting-parameters file)
and writes one JSON line per plate: outcome, warnings by kind, safest-route and head-raise
flags, expansions against the budget, time, and the spacing pairs the overlap label would show.
It never fails on an outcome. The folder holds production nests, so it stays outside the
repository, and so does the report.

```sh
OPENNEST_PRODUCTION_CORPUS=<folder of .nest files> \
OPENNEST_PRODUCTION_CORPUS_MACHINES=<folder of machine JSON> \
OPENNEST_PRODUCTION_CORPUS_PARAMETERS=<cutting-parameters JSON> \
OPENNEST_PRODUCTION_CORPUS_REPORT=<report.jsonl> \
  dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release --filter "Category=ProductionCorpus"
```

`OPENNEST_PRODUCTION_CORPUS_PARALLELISM` (default 4) plans that many nests at once, so times
and the 180 s `Timeout` (`..._TIMEOUT`) are under load; `..._FILTER` is a comma-separated list
of file-name fragments and `..._BUDGET` the planning-effort multiplier. Compare two reports plate
by plate: a plate that ranks lower, or keeps its rank with a safest route or more head raises
than before, is a regression. Without the folder variable the test skips.
