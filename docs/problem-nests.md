# Problem-nest regression corpus

Every real nest that ever caused a problem is kept on a dedicated OpenNest server that holds
nothing else, not in this repository: they carry customer geometry and names. Each nest's comments
say what went wrong and which commit fixed it, and its `checks:` line names the areas it guards, so
a change runs only the nests that guard what it touches: plan or nest them and confirm none got
worse. Most guard Plan Cutting; `auto-nest` nests guard the nesting engines and `fill` nests the
plate fill engine. The UI, posts and file formats never need the corpus.

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
| `fill` | The plate fill engine: `OpenNest.Engine/Fill/`, `OpenNest.Engine/Strategies/`, `OpenNest.Engine/BestFit/`, `PlateFiller` and `PlateFillService`. Guarded by part count per plate, not by planning. Best fits also feed Auto Nest, so a best-fit change runs `auto-nest` too |

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
`ConstraintConflict`, `InvalidInput`, `Overlap`, `LeadOffPlate`: a Ready plan with a lead-in or
lead-out past the plate's box) above `Timeout`. The test fails when a plate ranks below its
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

A job that does not fill its last sheet can get worse without taking another sheet, so the line
may also say where that sheet ends: `auto-nest: 1 sheets, ends 62.2`. The end is how far along the
sheet's length the last sheet's material reaches, measured from the end of the sheet the layout
starts at (the right edge of a layout packed from the left; the same figure in every quadrant).
With the baseline's sheet count, a layout ending more than 0.05 past the recorded end fails; one
ending sooner is only reported, with the line to record. A line without `ends` checks the sheet
count alone. Every run prints each nest's end, and a missing baseline's failure prints the whole
line, the end rounded up to a tenth.

Some jobs get better with more sheets of less material, so a sheet count would pass a regression
there. Their line also records the job's cost: `auto-nest: 8 sheets, cost 54144` (the job cost
of the layout, rounded up to a whole unit; without stock prices, the sheets' area). With a cost the
cost decides: a layout costing more than 0.5 over it fails whatever its sheet count, and one
costing less is only reported, with the line to record. The end, if the line has one, is then
not checked. Every run prints each nest's cost.

#### Saved layouts

A nest can also hold the layout its baseline describes, so nobody has to nest the old code again
to see it or to compare with it. Set `OPENNEST_PROBLEM_NESTS_RECORD=1` and run the `auto-nest`
area:

```sh
OPENNEST_PROBLEM_NESTS_SERVER=http://<server>:<port> OPENNEST_PROBLEM_NESTS_CHECKS=auto-nest \
OPENNEST_PROBLEM_NESTS_RECORD=1 \
  dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter "Category=ProblemNests" --logger "console;verbosity=detailed"
```

Each nest whose layout is valid, complete and no worse than its baseline is saved back as a new
revision: its plates are replaced by the layout Auto Nest just made, on the sheets the job nested
on (the first plate's spacing, quadrant and clamps, so the saved nest gives the same job again),
and its line becomes `auto-nest: 1 sheets, ends 62.2, layout saved`. A nest without a baseline
gets its first one this way instead of failing. What was on the plates before, usually the hand
layout that showed the problem, stays in the nest's earlier revisions.

- A layout worse than the baseline fails as always and is never recorded. To accept one, edit
  the line by hand, then record.
- A layout within the tolerance past the recorded end keeps that end, so recording again and
  again cannot let the baseline creep.
- A nest that also guards a planner area or `fill` is not saved: those tests read its plates.
  Neither is a layout that leaves one of the nest's sheet sizes unused, because the job takes
  its sizes from the plates. Both print the line to write by hand.
- A nest whose saved layout and line already match is left alone, so a run that changes
  nothing adds no revisions.

Without the variable nothing is written. A run then says, for each nest whose line ends in
`layout saved`, whether Auto Nest still makes exactly that layout (`the saved layout, unchanged`)
or another one (`not the saved layout`): the same sheets and sizes, with every part at the same
pose. An engine change meant to leave layouts alone can be checked against the corpus that way
without a second run on master.

### Fill nests

`ProblemNests_FillNoWorseThanTheirBaselines` takes the nests whose `checks:` line names `fill`
and fills each plate its `fill: 1=33 3=120` comment line lists, the way **Fill Plate** does: the
plate's parts are removed (its cut-offs stay), and the strategy fills the plate's work area with
the plate's one drawing at unlimited quantity, using the plate's own size, spacing, edge spacing,
quadrant and clamps as saved. Lattice fills unless the line names another strategy in
parentheses: `fill (Strip): 1=33`. Each plate prints one line:
`<nest> plate 1: 33 parts (baseline 33), 61.2 s`, or `<nest> plate 1 (Strip): ...` for another
strategy, shaped like the planning test's line so a nightly runner parses every area alike.

A plate fails when it places fewer parts than its baseline, its layout breaks the layout check
(bounds, spacing, clamps), or the fill times out (the same per-plate limit as planning: 120 s, or
`OPENNEST_PROBLEM_NESTS_TIMEOUT` seconds). More parts are only reported; update the baseline.
A fill baseline takes plates holding one drawing: a listed plate holding several, or none, or a
plate number the nest lacks, fails and prints the observed line. A nest without the line fills
every plate holding one drawing and fails with the line to paste. A full Lattice fill of a large
plate takes about a minute, so run the fill area alone. Run it with
`OPENNEST_PROBLEM_NESTS_CHECKS=fill`; a filter naming only planner areas or `auto-nest` skips it,
and the planning and Auto Nest tests skip nests guarding only `fill`.

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
   and which commit fixed it, if any. Keep it scannable: short `Setup:`, `Problem:` and `Fix:`
   sections separated by blank lines, with `-` bullets rather than one long paragraph. Never
   start a prose line with one of the machine-read prefixes below.
3. Add a baseline line to the comments, with plate numbers counted from the first plate:
   `planning: 1=NoSolutionWithinBudget 2=BestEffort`. An Auto Nest problem takes
   `auto-nest: 2 sheets` instead, the sheet count Auto Nest reaches today (`auto-nest: 1 sheets,
   ends 62.2` when the problem was how far a part-filled last sheet reached), and a fill problem
   `fill: 1=33`, the part count each listed plate's fill reaches today (`fill (Strip): 1=33` for
   a strategy other than Lattice). A fill plate must hold one drawing: the fill uses it.
   To get the line, save without one and run the test: the failure prints the observed line.
   An Auto Nest problem can have its line and layout [recorded](#saved-layouts) instead.
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
