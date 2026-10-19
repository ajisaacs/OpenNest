# Problem-nest regression corpus

Real nests that once failed to plan are kept on a dedicated OpenNest server that holds nothing
else, not in this repository: they carry customer geometry and names. After a change to planning, contour preparation or
the geometry checks, plan them all and confirm that no plate got worse.

## Running

```sh
# Windows PowerShell: $env:OPENNEST_PROBLEM_NESTS_SERVER = "http://<server>:<port>"
OPENNEST_PROBLEM_NESTS_SERVER=http://<server>:<port> \
  dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release \
  --filter "Category=ProblemNests" --logger "console;verbosity=detailed"
```

Without the variable the test skips, so routine runs never reach the network. With it, an
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

Plates use their own cutting parameters. A plate without them uses line lead-ins of 0.15 at
90 degrees (external, internal and arc/circle) and no lead-outs.

## Adding a nest

1. In the desktop app, point **File > Storage Mode...** at the corpus server, then drag the
   `.nest` file onto the window. Switch the server back afterwards.
2. In **Nest > Nest Info**, name it after the problem, and in the comments describe what failed
   and which commit fixed it, if any.
3. Add a baseline line to the comments, with plate numbers counted from the first plate:
   `planning: 1=NoSolutionWithinBudget 2=BestEffort`.
   To get the line, save without one and run the test: the failure prints the observed line.
4. Save. Every save keeps the earlier versions, so a baseline change can be reviewed later.

Record the outcome the code produces today, not the outcome you want. A nest that is still
blocked guards against getting worse, and its baseline moves up once a fix lands.
