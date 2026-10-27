# OpenNest agent instructions

Shared instructions; keep `CLAUDE.md` as the thin `@AGENTS.md` import.
OpenNest is a .NET 8 Windows CNC-nesting application with cross-platform libraries.

## Working rules

- Prefer Roslyn Bridge MCP for symbols, references and diagnostics when available; fall back to text search.
- Use `var` for locals and namespaces matching project directories. Follow `.editorconfig`; format only changed C# files with `dotnet format OpenNest.sln --include <paths>`, then repeat with `--verify-no-changes`. On Linux, prefix both commands with `EnableWindowsTargeting=true`.
- Keep instructions concise: commands, boundaries and non-obvious safeguards, not class inventories or session history. Update affected instructions and user-facing docs with behavior/build changes; put detailed contracts in `docs/`. User guides and their screenshots live in the separate `ajisaacs/opennest-help` repo: update or recapture them when a UI change alters a documented screen, and never rename a `HelpTopics` ID.
- Never commit design specs, implementation plans, progress notes or temporary benchmark reports. Keep working records under local, ignored `.hermes/plans/` or `.hermes/progress/`; retain reusable verification procedures in `docs/`.
- Keep vendor manuals/full-text extracts out of source control unless redistribution is authorized. Write project-specific behavior summaries with citations, separating controller rules, machine macros and unconfirmed behavior.

## Build and test

```sh
# Full solution: Windows
dotnet build OpenNest.sln

# Cross-platform suites: run independently on Linux/macOS/Windows
# Use Release for routine runs; use Debug explicitly for DEBUG-only work counters.
dotnet test OpenNest.Tests/OpenNest.Tests.csproj -c Release
dotnet test OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj -c Release
dotnet test OpenNest.IO.Tests/OpenNest.IO.Tests.csproj -c Release
dotnet test OpenNest.Desktop.Tests/OpenNest.Desktop.Tests.csproj -c Release
dotnet test OpenNest.Server.Tests/OpenNest.Server.Tests.csproj -c Release
dotnet test OpenNest.FrontEnd.Tests/OpenNest.FrontEnd.Tests.csproj -c Release

# Windows runtime tests
dotnet test OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj
```

After changing planning, contour preparation or geometry checks, run the [problem-nest corpus](docs/problem-nests.md) (`Category=ProblemNests`, opt-in via `OPENNEST_PROBLEM_NESTS_SERVER`); no plate may rank below its baseline. Keep desktop-dependent tests in `OpenNest.WinForms.Tests`, never add a WinForms reference to `OpenNest.Tests`. Optional CHR fixtures use local `OpenNest.Tests/test-config.json` and skip when absent. On Linux, build Windows projects with `-p:EnableWindowsTargeting=true`; this is not Windows runtime verification. The headless console builds independently with `dotnet build OpenNest.Console/OpenNest.Console.csproj`.

Releases: follow [the release procedure](docs/releasing.md) and `scripts/Publish-Windows.ps1`; workflow artifacts are candidates, not published releases. GitHub (`ajisaacs/OpenNest`) is the primary repository; Gitea is a read-only backup mirror.

## Project map and boundaries

- `OpenNest.Core`: domain (`Nest -> Plate -> Part -> Drawing -> CNC.Program`), geometry, cutting strategies and diagnostics. Angles are radians; use `Tolerance.Epsilon` for geometry comparisons. `OpenNest.Math` shadows `System.Math`, so qualify the latter.
- `OpenNest.Engine`: whole-job API in `Jobs/`, interactive proposals via `PlateFillService`, fill strategies, best-fit pairs, packing, sequencing and rapid planning. `INestingEngine.Solve(NestJob)` returns stock IDs/poses; boundary adapters map drawings and materialize results. `NestJobRunner` validates its candidates before committing demand/stock accounting. Do not assume arbitrary plug-in output or interactive paths received that validation. Job identity is reference-based, not drawing-name-based.
- Built-in whole-job engines live in `OpenNest.Engine/NestingEngines/<Name>/`, named for the jobs they suit; see [nesting engines](docs/nesting-engines.md). A change must beat that engine's current benchmark result with every layout valid. External plug-ins implement `INestingEngine` with a public parameterless constructor and load from `Engines/` beside the host; keep their projects out of this solution.
- `OpenNest.IO`: ACadSharp import/export and ZIP-based `.nest` persistence; depends on Core only (an IO.Tests guard enforces it). Best fits persist only when callers pass `BestFitCacheStore.Shared` as `NestReader`/`NestWriter.BestFitStore`. All DXF-to-Drawing conversion goes through `CadImporter`: `Import` + `BuildDrawing` for editable/reporting flows, `ImportDrawing` for headless callers. Preserve source offsets, entity IDs, suppressed entities and bends. Bend repair is opt-in, requires explicit source units and may not alter cut geometry or unrelated marks.
- `OpenNest`: WinForms UI (`Forms/`, `Controls/PlateView`, `Actions/`). `OpenNest.Data` holds cross-platform persistence; new-nest defaults live in `%APPDATA%\OpenNest\defaults.json` (in Database mode, the server's shared copy once it holds one). Posts live in `Posts/OpenNest.Posts.<Name>/` and deploy to the desktop output's `Posts/` directory. Desktop nest edits go through `BeginChange`/`RecordChange`; see [undo and redo](docs/undo-redo.md).
- `OpenNest.Desktop` (net8.0): toolkit-neutral desktop logic shared by the WinForms app and `OpenNest.Avalonia`, the cross-platform desktop in progress. It must not reference WinForms/WindowsDesktop, `System.Drawing.Common` or Avalonia (`OpenNest.Desktop.Tests` guards this, including transitive packages); put toolkit-specific rendering, dialogs and settings behind adapters in the front ends.
- `OpenNest.Console`, `OpenNest.Mcp`, `OpenNest.Api`: front ends; `OpenNest.Benchmark`: whole-job engine comparisons; `OpenNest.Gpu`: GPU evaluators; `OpenNest.Training`: ML data collection. Benchmark timing comparisons require `--parallel 1`; validate layouts and fulfillment, not just elapsed time.

## Geometry and ownership safeguards

- Marks are not material: use `SpecialLayers.IsMaterial` when deriving nesting/collision geometry; exclude rapid and scribe moves without removing them from display, cutting time or posts.
- Engine/validator collision paths retain the hand-written `Collision` kernel; its GPU port is on hold. Material-overlap diagnostics use Clipper2 per pair for connected shared-material islands and interior markers; see [material-overlap diagnostics](docs/geometry/visual-overlap-check.md). Polygon consumers use `ClipperBridge`; directional-distance consumers retain native-arc offsets. Validation uses `OffsetForValidation` and `NestTolerances.SpacingSlack`, not conservative display/preparation padding; Auto Nest's layout check, the runner's candidate validation and the pre-post part-spacing review all find spacing candidates with Core's `SpacingOutline` and confirm them on `NativeContours` (the outline circumscribes arcs by more than the slack), so they agree; `NestLayoutCheck.Clears` skips the confirmation and stays stricter for engine certification. Do not loosen tolerances to hide failures.
- `FillLinear` geometry caches are per public call, keyed by `Program` reference identity; never share them across calls/threads. `PartOverlapChecker` is per check; parts/programs must not mutate during its lifetime.
- `FillScore` ranks count, utilization, compactness; exact ties keep the current layout. Custom comparers remain authoritative. Preserve extents' negative/nonfinite-input fallback, pair preparation and adjusted-column overlap checks. Do not remove bounds recomputations without threshold/rounding characterization.
- `ObservableList` events own drawing/plate quantity tracking; avoid double accounting. Cutoff parts are excluded from quantity, utilization and overlap checks.
- Cutoffs persist as definitions on `Plate.CutOffs`; apply through `RegenerateCutOffs`, never preview parts. Preserve sequence positions. Batch planning must finish before mutation and roll back on failure. Use `PlateSequencing.Apply` for automatic cutoff dependencies, with nominal spans/reference identity rather than trimmed geometry/names.
- A cutoff starts only at the origin-side edge: stock can be oversize, so the far edge is unknown. Only a head-locked overcut (`CutOff.HeadLockOvercut`, `HeadHeightLock` markers) may start its far-edge segment there; posts refuse it without lock codes.
- Fixed emissions (locked programs, drawing-pinned contours) are accepted as given: their violations warn on an UNVERIFIED proposal and never steer the search, while every planner-chosen motion stays strict. Plates without them must keep the `PlanningCharacterizationTests` digests.
- An empty diagnostic is not a clear result unless `IsComplete`. Posting must run checks before writing CNC output; warnings require explicit per-attempt consent, never a persisted bypass. Keep inputs stable through analysis/cancellation.
- Preserve symbolic G-code variable definitions/references in file round trips. Keep training bitmaps by default; inference checks predictor availability before scalar-only extraction.

Read the relevant contract before changing its behavior:

- [Nest file format](docs/nest-file-format.md)
- [Directional slides](docs/geometry/directional-slides.md) and [pair-spacing limits](docs/geometry/pair-spacing.md)
- [Lead-in placement](docs/geometry/lead-in-placement.md) (including the planner's eroded pierce for cutouts too small for their lead-in) and [pierce zones](docs/geometry/pierce-zone.md): opt-in `PierceDiameter` clearance, the hole pole fallback and the planner's perimeter scrap fallback
- [Breakoffs](docs/breakoffs.md): tabbed scrap; `Break`-layer open cuts that are cut but are not material
- [Tabbed cutouts](docs/tabbed-cutouts.md): holes cut in pieces around `Tab`-layer spans that are material but never cut; nesting reads material through `NestingMaterial.ForNesting`, so nothing nests in them
- [Support tabs](docs/support-tabs.md): the nest's support grid, the center-of-gravity check, and Plan Cutting's tab placement for parts the grid cannot hold level
- [Material-overlap diagnostics](docs/geometry/visual-overlap-check.md)
- [Automatic cutoffs and sequencing](docs/automatic-scrap-cutoffs.md)
- [Pre-post verification](docs/post-verification.md)
- [Machine cutting defaults](docs/machine-cutting-defaults.md): the nest's machine supplies leads, kerf and cut-off settings for plates without their own parameters
- [Shared settings](docs/shared-settings.md): in Database mode machines, post settings, clamps and nest defaults come from the server once it holds them; offline uses the last copy read, read-only
- [Cut-time estimates](docs/cut-time-estimates.md): simulator/Calculate Cut Time acceleration planner and placeholder machine limits
- [Cincinnati CL](docs/cincinnati-post-output.md) and [CI Fiber](docs/cincinnati-ci-fiber-post-output.md)
- [Fill verification](docs/performance/fill-verification.md): opt-in benchmarks, frozen oracles, Debug-only counters and predictor initialization. Zero Release counters do not prove work removal.
- [Desktop auto-update](docs/auto-update.md): GitHub Releases by default, opt-in rolling `dev-build` pre-release from green master pushes, `UpdateFeedUrl` for a self-hosted feed, release Velopack assets and `Deploy-OpenNest.ps1`.
- [Undo and Redo](docs/undo-redo.md): whole-nest history, the recording rule, what a restore rebuilds and what is excluded.
- [Desktop help](docs/desktop-help.md): WebView2 help window, F1 topic IDs and the pinned help release both packaging scripts install.
