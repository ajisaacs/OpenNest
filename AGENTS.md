# OpenNest agent instructions

Shared instructions; keep `CLAUDE.md` as the thin `@AGENTS.md` import.
OpenNest is a .NET 8 Windows CNC-nesting application with cross-platform libraries.

## Working rules

- Prefer Roslyn Bridge MCP for symbols, references and diagnostics when available; fall back to text search.
- Use `var` for locals and namespaces matching project directories. Follow `.editorconfig`; format only changed C# files with `dotnet format OpenNest.sln --include <paths>`, then repeat with `--verify-no-changes`. On Linux, prefix both commands with `EnableWindowsTargeting=true`.
- Keep instructions concise: commands, boundaries and non-obvious safeguards, not class inventories or session history. Update affected instructions and user-facing docs with behavior/build changes; put detailed contracts in `docs/`.
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
dotnet test OpenNest.Server.Tests/OpenNest.Server.Tests.csproj -c Release

# Windows runtime tests
dotnet test OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj
```

Keep desktop-dependent tests in `OpenNest.WinForms.Tests`, never add a WinForms reference to `OpenNest.Tests`. Optional CHR fixtures use local `OpenNest.Tests/test-config.json` and skip when absent. On Linux, build Windows projects with `-p:EnableWindowsTargeting=true`; this is not Windows runtime verification. The headless console builds independently with `dotnet build OpenNest.Console/OpenNest.Console.csproj`.

Releases: follow [the release procedure](docs/releasing.md) and `scripts/Publish-Windows.ps1`; workflow artifacts are candidates, not published releases. GitHub (`ajisaacs/OpenNest`) is the primary repository; Gitea is a read-only backup mirror.

## Project map and boundaries

- `OpenNest.Core`: domain (`Nest -> Plate -> Part -> Drawing -> CNC.Program`), geometry, cutting strategies and diagnostics. Angles are radians; use `Tolerance.Epsilon` for geometry comparisons. `OpenNest.Math` shadows `System.Math`, so qualify the latter.
- `OpenNest.Engine`: whole-job API in `Jobs/`, interactive proposals via `PlateFillService`, fill strategies, best-fit pairs, packing, sequencing and rapid planning. `INestingEngine.Solve(NestJob)` returns stock IDs/poses; boundary adapters map drawings and materialize results. `NestJobRunner` validates its candidates before committing demand/stock accounting. Do not assume arbitrary plug-in output or interactive paths received that validation. Job identity is reference-based, not drawing-name-based.
- Built-in whole-job engines live in `OpenNest.Engine/NestingEngines/<Name>/`, named for the jobs they suit; see [nesting engines](docs/nesting-engines.md). A change must beat that engine's current benchmark result with every layout valid. External plug-ins implement `INestingEngine` with a public parameterless constructor and load from `Engines/` beside the host; keep their projects out of this solution.
- `OpenNest.IO`: ACadSharp import/export and ZIP-based `.nest` persistence. All DXF-to-Drawing conversion goes through `CadImporter`: `Import` + `BuildDrawing` for editable/reporting flows, `ImportDrawing` for headless callers. Preserve source offsets, entity IDs, suppressed entities and bends. Bend repair is opt-in, requires explicit source units and may not alter cut geometry or unrelated marks.
- `OpenNest`: WinForms UI (`Forms/`, `Controls/PlateView`, `Actions/`). `OpenNest.Data` holds cross-platform persistence; new-nest defaults live in `%APPDATA%\OpenNest\defaults.json`. Posts live in `Posts/OpenNest.Posts.<Name>/` and deploy to the desktop output's `Posts/` directory.
- `OpenNest.Console`, `OpenNest.Mcp`, `OpenNest.Api`: front ends; `OpenNest.Benchmark`: whole-job engine comparisons; `OpenNest.Gpu`: GPU evaluators; `OpenNest.Training`: ML data collection. Benchmark timing comparisons require `--parallel 1`; validate layouts and fulfillment, not just elapsed time.

## Geometry and ownership safeguards

- Marks are not material: use `SpecialLayers.IsMaterial` when deriving nesting/collision geometry; exclude rapid and scribe moves without removing them from display, cutting time or posts.
- Clipper is for cached CPU region preparation, never per-pair hot loops. Preserve the hand-written `Collision` kernel's GPU-port contract. Polygon consumers use `ClipperBridge`; directional-distance consumers retain native-arc offsets. Validation uses `OffsetForValidation` and `NestTolerances.SpacingSlack`, not conservative display/preparation padding. Do not loosen tolerances to hide failures.
- `FillLinear` geometry caches are per public call, keyed by `Program` reference identity; never share them across calls/threads. `PartOverlapChecker` is per check; parts/programs must not mutate during its lifetime.
- `FillScore` ranks count, utilization, compactness; exact ties keep the current layout. Custom comparers remain authoritative. Preserve extents' negative/nonfinite-input fallback, pair preparation and adjusted-column overlap checks. Do not remove bounds recomputations without threshold/rounding characterization.
- `ObservableList` events own drawing/plate quantity tracking; avoid double accounting. Cutoff parts are excluded from quantity, utilization and overlap checks.
- Cutoffs persist as definitions on `Plate.CutOffs`; apply through `RegenerateCutOffs`, never preview parts. Preserve sequence positions. Batch planning must finish before mutation and roll back on failure. Use `PlateSequencing.Apply` for automatic cutoff dependencies, with nominal spans/reference identity rather than trimmed geometry/names.
- An empty diagnostic is not a clear result unless `IsComplete`. Posting must run checks before writing CNC output; warnings require explicit per-attempt consent, never a persisted bypass. Keep inputs stable through analysis/cancellation.
- Preserve symbolic G-code variable definitions/references in file round trips. Keep training bitmaps by default; inference checks predictor availability before scalar-only extraction.

Read the relevant contract before changing its behavior:

- [Nest file format](docs/nest-file-format.md)
- [Directional slides](docs/geometry/directional-slides.md) and [pair-spacing limits](docs/geometry/pair-spacing.md)
- [Lead-in placement](docs/geometry/lead-in-placement.md)
- [Material-overlap diagnostics](docs/geometry/visual-overlap-check.md)
- [Automatic cutoffs and sequencing](docs/automatic-scrap-cutoffs.md)
- [Pre-post verification](docs/post-verification.md)
- [Cincinnati CL](docs/cincinnati-post-output.md) and [CI Fiber](docs/cincinnati-ci-fiber-post-output.md)
- [Fill verification](docs/performance/fill-verification.md): opt-in benchmarks, frozen oracles, Debug-only counters and predictor initialization. Zero Release counters do not prove work removal.
