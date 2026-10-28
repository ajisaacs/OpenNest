# Problem-nest regression corpus

Real nests that once failed to plan are kept on a dedicated OpenNest server that holds nothing
else, not in this repository: they carry customer geometry and names. Every nest in it guards
Plan Cutting, so only a change to the cutting planner calls for it: plan the nests that guard the
areas the change touches and confirm that no plate got worse. Nesting engines, fills, the UI,
posts and file formats never need it.

## Areas

Each nest names the areas it guards in a `checks:` comment line. Pick every area a change touches;
when a change spans the planner or you cannot tell, run the whole corpus.

| Area | Change touches |
|------|----------------|
| `part-order` | Whole-part order: learned order rules, stalls, budgets and the tours (`JointCuttingPlanSearch` order loop, `CuttingPartOrder`, `CuttingNestedOrder`, `CuttingDependencyGraph`, `CuttingPlanBatch`, the safest-route walk) |
| `entries` | Where and how each contour is entered: entry candidates and ranking, hole order, lead-ins, pierce zones (`ContourEntry*`, `PreferredContourEntries`, `CuttingHoleOrder`, `ScrapPierceSearch`, `JointCuttingPlanSearch` entry and recovery code, lead-in builders) |
| `contours` | Reading programs into contours: closure, seam overcuts, prepared contours (`ExecutionMotionReader`, `PreparedContours`, `IncrementalClosureDrift`, `SeamOvercut`, `NominalContourClosure`, `OwnedProgramCopy`) |
| `geometry-checks` | The checks a route must pass: rapid crossings, lead and material paths, replay, overlaps (`ReleasedContourState`, `LeadPathValidator`, `LeadMaterialSnapshot`, `ContourProgramVerifier`, `PlateOverlapAnalyzer`) |
| `cut-offs` | Cut-off segments in the plan (`CutOffInsertion`, `CutOffSegments`, `PlateSequencing` cut-off placement, automatic scrap cut-offs) |
| `grid` | Large plates: the aligned-grid serpentine, or any work done once per part or per pair that would grow on thousands of parts (`CuttingGridOrder`, per-part capture and checks) |

A new area goes in `ProblemNestRegressionTests.Areas` and this table together. A misspelt area,
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
   `planning: 1=NoSolutionWithinBudget 2=BestEffort`.
   To get the line, save without one and run the test: the failure prints the observed line.
   Add a `checks:` line naming the [areas](#areas) the problem was in, such as `checks: part-order`.
4. Save. Every save keeps the earlier versions, so a baseline change can be reviewed later.

Record the outcome the code produces today, not the outcome you want. A nest that is still
blocked guards against getting worse, and its baseline moves up once a fix lands.
