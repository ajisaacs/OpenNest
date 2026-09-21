# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

OpenNest is a Windows desktop application for CNC nesting — arranging 2D parts on material plates to minimize waste. It imports DXF drawings, places parts onto plates using NFP-based (No Fit Polygon) and rectangle-packing algorithms, and can export nest layouts as DXF or post-process them to G-code for CNC cutting machines.

## Build

This is a .NET 8 solution using SDK-style `.csproj` files. The desktop app and Windows-dependent projects target `net8.0-windows`; the core libraries and `OpenNest.Console` target `net8.0`. Build the full solution on Windows with:

```bash
dotnet build OpenNest.sln
```

Cross-platform whole-job engine tests (net8.0, runs on Linux/macOS/Windows without the desktop project or DXF fixtures): `dotnet test OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj`. The main `OpenNest.Tests` suite also targets `net8.0`: run `dotnet test OpenNest.Tests/OpenNest.Tests.csproj` independently on Linux/macOS/Windows. It must not reference the WinForms `OpenNest` project. The API, Data, Cincinnati, and GravographIS libraries target `net8.0`; post-processor build deployment still targets the desktop app's `net8.0-windows/Posts` directory. Optional CHR-font fixtures are configured through `OpenNest.Tests/test-config.json` and skip when absent.

`OpenNest.WinForms.Tests` contains the desktop-assembly-dependent `CadBendNoteTests` (`CadText`) and `CuttingParametersSerializerTests` (`CuttingParametersSerializer`). It targets `net8.0-windows`, references `OpenNest`, and requires a Windows runner: `dotnet test OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj`. Keep future desktop-dependent tests here rather than in `OpenNest.Tests`. Linux cross-compilation uses `dotnet build OpenNest.WinForms.Tests/OpenNest.WinForms.Tests.csproj -p:EnableWindowsTargeting=true`; cross-compilation is not Windows runtime verification.

Cross-platform CAD import tests: `dotnet test OpenNest.IO.Tests/OpenNest.IO.Tests.csproj`. These synthetic-DXF and bend-repair tests target `net8.0`, require no external fixtures, and are included in the solution. Build the headless console independently with `dotnet build OpenNest.Console/OpenNest.Console.csproj`.

NuGet dependencies: `ACadSharp` 3.1.32 (DXF/DWG import/export, in OpenNest.IO), `System.Drawing.Common` 8.0.10, `ModelContextProtocol` + `Microsoft.Extensions.Hosting` (in OpenNest.Mcp), `Microsoft.ML.OnnxRuntime` (in OpenNest.Engine for ML angle prediction), `Microsoft.EntityFrameworkCore.Sqlite` (in OpenNest.Training).

## Architecture

Nine projects form a layered architecture:

### OpenNest.Core (class library)
Domain model, geometry, and CNC primitives organized into namespaces:

- **Root** (`namespace OpenNest`): Domain model — `Nest` → `Plate[]` → `Part[]` → `Drawing` → `Program`. A `Nest` is the top-level container. Each `Plate` has a size, material, quadrant, spacing, and contains placed `Part` instances. Each `Part` references a `Drawing` (the template) and has its own location/rotation. A `Drawing` wraps a CNC `Program`. Also contains utilities: `PartGeometry`, `Align`, `Sequence`, `Timing`.
- **CNC** (`CNC/`, `namespace OpenNest.CNC`): `Program` holds a list of `ICode` instructions (G-code-like: `RapidMove`, `LinearMove`, `ArcMove`, `SubProgramCall`) and an optional `Variables` dictionary of `VariableDefinition` entries. Programs support absolute/incremental mode conversion, rotation, offset, bounding box calculation, and cloning. `VariableDefinition` stores a named variable's expression, resolved value, and flags (`Inline`, `Global`). `ProgramVariableManager` manages numbered machine variables for post-processor output.
- **Geometry** (`Geometry/`, `namespace OpenNest.Geometry`): Spatial primitives (`Vector`, `Box`, `Size`, `Spacing`, `BoundingBox`, `IBoundable`) and higher-level shapes (`Line`, `Arc`, `Circle`, `Polygon`, `Shape`) used for intersection detection, area calculation, and DXF conversion. Also contains `Intersect` (intersection algorithms), `ShapeBuilder` (entity chaining), `GeometryOptimizer` (line/arc merging), `SpatialQuery` (directional distance, ray casting, box queries), `ShapeProfile` (perimeter/area analysis), `NoFitPolygon`, `ConvexHull`, `ConvexDecomposition`, `RotatingCalipers`, and `Collision` (overlap detection with Sutherland-Hodgman polygon clipping and hole subtraction).
- **Converters** (`Converters/`, `namespace OpenNest.Converters`): Bridges between CNC and Geometry — `ConvertProgram` (CNC→Geometry), `ConvertGeometry` (Geometry→CNC), `ConvertMode` (absolute↔incremental).
- **Math** (`Math/`, `namespace OpenNest.Math`): `Angle` (radian/degree conversion), `Tolerance` (floating-point comparison), `Trigonometry`, `Generic` (swap utility), `EvenOdd`, `Rounding` (factor-based rounding), `ExpressionEvaluator` (arithmetic expression parser for G-code variable expressions with `$name` references). Note: `OpenNest.Math` shadows `System.Math` — use `System.Math` fully qualified where both are needed.
- **CNC/CuttingStrategy** (`CNC/CuttingStrategy/`, `namespace OpenNest.CNC`): `ContourCuttingStrategy` orchestrates cut ordering, lead-ins/lead-outs, and tabs. Includes `LeadIn`/`LeadOut` hierarchies (line, arc, clean-hole variants), `Tab` hierarchy (normal, machine, breaker), and `CuttingParameters`/`AssignmentParameters`/`SequenceParameters` configuration.
- **Collections** (`Collections/`, `namespace OpenNest.Collections`): `ObservableList<T>`, `DrawingCollection`.
- **CutOffs** (`namespace OpenNest`): `CutOff` (axis-aligned cut line with position, axis, optional start/end limits), `CutOffAxis` enum (`Horizontal`, `Vertical`), `CutOffSettings` (clearance, overtravel, min segment length, direction), `CutDirection` enum (`TowardOrigin`, `AwayFromOrigin`). Cut-offs generate CNC `Program` objects with trimmed line segments that avoid parts.
- **Splitting** (`Splitting/`, `namespace OpenNest`): `DrawingSplitter` splits a Drawing into multiple pieces along split lines. `ISplitFeature` strategy pattern with implementations: `StraightSplit` (clean edge), `WeldGapTabSplit` (rectangular tab spacers on one side), `SpikeGrooveSplit` (interlocking spike/V-groove pairs). `AutoSplitCalculator` computes split lines for fit-to-plate and split-by-count modes. Supporting types: `SplitLine`, `SplitParameters`, `SplitFeatureResult`.
- **Quadrant system**: Plates use quadrants 1-4 (like Cartesian quadrants) to determine coordinate origin placement. This affects bounding box calculation, rotation, and part positioning.

### OpenNest.Engine (class library, depends on Core)
Nesting algorithms provide both a legacy single-plate API and a whole-job API. The legacy path centers on `NestEngineBase`, `DefaultNestEngine` (formerly `NestEngine`), and the global `NestEngineRegistry`. New job callers use immutable, ID-based contracts in `Jobs/`: `INestingEngine.Solve(NestJob)` returns `NestJobResult`; `NestJobRunner` alone commits demand and finite/unlimited stock accounting; `IPlateNester` only proposes a one-sheet candidate; and `PlateNesterFactory` resolves a named strategy without reading or changing the process-global registry.

- **Whole-job API (`Jobs/`)**: `NestJob` owns part requirements, physical stock, and options for one material/thickness/unit system. `PartGeometrySnapshot` contains owned flat rapid/line/arc geometry; results contain stock IDs and placement poses (radians), not mutable desktop models. `NestJobPlacementValidator` validates contours, rotation, usable work area, overlap, and spacing before accounting commits. The runner selects valid trial candidates greedily by priority vector, sheet area, envelope, and input order; an incomplete result reports why but does not prove geometric impossibility. `DrawingJobMapper` and `NestResultMaterializer` are the domain-boundary adapters.
- **Placement boundary (`Jobs/Placement/`, `Jobs/Adapters/`)**: `DefaultPlateNester` and `StripPlateNester` are migrated built-ins with run-scoped private geometry; `LegacyPlateNesterAdapter` remains for remnant strategies and legacy plugins/callers during rollout. Job-path identity is reference-based rather than drawing name; `PlateOptimizer` retains legacy name-based helpers and is deliberately outside the runner path.
- **Engine hierarchy**: `NestEngineBase` (abstract) → `DefaultNestEngine` (Linear, Pairs, RectBestFit, Remainder phases) → `VerticalRemnantEngine` (optimizes for right-side drop), `HorizontalRemnantEngine` (optimizes for top-side drop). Custom engines subclass `NestEngineBase` and register via `NestEngineRegistry.Register()` or as plugin DLLs in `Engines/`. Existing desktop, CLI, and MCP callers remain on this compatibility path until separate migrations preserve their existing-plate, preview, and accept/cancel semantics.
- **IFillComparer**: Interface enabling engine-specific scoring. `DefaultFillComparer` (count-then-density), `VerticalRemnantComparer` (minimize X-extent), `HorizontalRemnantComparer` (minimize Y-extent). Engines provide their comparer via `CreateComparer()` factory, grouped into `FillPolicy` on `FillContext`.
- **NestEngineRegistry**: Static registry — `Create(Plate)` factory, `ActiveEngineName` global selection, `LoadPlugins(directory)` for DLL discovery. All callsites use `NestEngineRegistry.Create(plate)` except `BruteForceRunner` which uses `new DefaultNestEngine(plate)` directly for training consistency.
- **Fill/** (`namespace OpenNest.Engine.Fill`): Fill algorithms — `FillLinear` (grid-based), `FillExtents` (extents-based pair tiling), `PairFiller` (interlocking pairs), `ShrinkFiller`, `RemnantFiller`/`RemnantFinder`, `Compactor` (post-fill gravity compaction), `FillScore` (lexicographic comparison: count > utilization > compactness), `Pattern`/`PatternTiler`, `PartBoundary`, `RotationAnalysis`, `AngleCandidateBuilder`, `BestCombination`, `AccumulatingProgress`.
- **Strategies/** (`namespace OpenNest.Engine.Strategies`): Pluggable fill strategy layer — `IFillStrategy` interface, `FillContext`, `FillStrategyRegistry` (auto-discovers strategies via reflection, supports plugin DLLs), `FillHelpers`. Built-in strategies: `LinearFillStrategy`, `PairsFillStrategy`, `RectBestFitStrategy`, `ExtentsFillStrategy`.
- **BestFit/** (`namespace OpenNest.Engine.BestFit`): NFP-based pair evaluation pipeline — `BestFitFinder` orchestrates angle sweeps, `PairEvaluator`/`IPairEvaluator` scores part pairs, `RotationSlideStrategy`/`ISlideComputer` computes slide distances. `BestFitCache` and `BestFitFilter` optimize repeated lookups.
- **RectanglePacking/** (`namespace OpenNest.Engine.RectanglePacking`): `FillBestFit` (single-item fill, tries horizontal and vertical orientations), `PackBottomLeft` (multi-item bin packing, sorts by area descending). Both operate on `Bin`/`Item` abstractions.
- **CirclePacking/** (`namespace OpenNest.Engine.CirclePacking`): Alternative packing for circular parts.
- **ML/** (`namespace OpenNest.Engine.ML`): `AnglePredictor` (ONNX model for predicting good rotation angles), `FeatureExtractor` (part geometry features), `BruteForceRunner` (full angle sweep for training data).
- `NestItem`: Input to the engine — wraps a `Drawing` with quantity, priority, and rotation constraints.
- `NestProgress`: Progress reporting model with `NestPhase` enum for UI feedback.

### OpenNest.IO (class library, depends on Core)
File I/O and format conversion. Uses ACadSharp for DXF/DWG support.

- `DxfImporter`/`DxfExporter` — DXF file import/export via ACadSharp.
- `NestReader`/`NestWriter` — custom ZIP-based nest format (JSON metadata + G-code programs, v2 format).
- `ProgramReader` — G-code text parser.
- `Extensions` — conversion helpers between ACadSharp and OpenNest geometry types.
- `CadImporter` — shared "DXF → Drawing" service used by the UI, console, MCP, API, and training projects. Two-stage API: `Import(path, options)` loads raw entities, runs bend detection, and returns a mutable `CadImportResult`; `BuildDrawing(result, visible, bends, quantity, customer, editedProgram)` produces a fully-populated `Drawing` with `Source.Offset`, `SourceEntities`, `SuppressedEntityIds`, and bends. `ImportDrawing(path, options)` composes both stages for headless callers.
- `CadImportOptions`, `CadImportResult` — inputs and intermediate state for `CadImporter`.
- `Bending/BendRepair` — conservative opt-in repair configured by `CadImportOptions.BendRepair`. Requires explicit inches/mm source units and an endpoint movement limit above 0.001 and at most 3.175 physical mm. Only unambiguous paired ETCH/SCRIBE ticks may move along the existing bend axis; cut geometry and unrelated marks must remain unchanged. Opt-in imports preserve source marks without blanket etch regeneration and expose per-bend outcomes in `CadImportResult.BendRepairReports`.

### OpenNest.Console (console app, depends on Core + Engine + IO)
Command-line interface for batch nesting (`net8.0`). Supports DXF import, plate configuration, linear fill, and multi-drawing auto-nesting through the active engine's `Nest()` (`--autonest`). `--repair-bends-mm <limit> --cad-units inches|mm` opts newly imported DXFs into conservative bend repair and prints per-bend reports; it does not rescale coordinates or repair saved nests.

### OpenNest.Gpu (class library, depends on Core + Engine)
GPU-accelerated pair evaluation for best-fit nesting. `GpuPairEvaluator` implements `IPairEvaluator`, `GpuSlideComputer` implements `ISlideComputer`, and `PartBitmap` handles rasterization. `GpuEvaluatorFactory` provides factory methods.

### OpenNest.Training (console app, depends on Core + Engine)
Training data collection for ML angle prediction. `TrainingDatabase` stores per-angle nesting results in SQLite via EF Core for offline model training.

### OpenNest.Benchmark (console app, depends on Core + Engine + IO)
Compares registered `INestingEngine` implementations against each other on real `.nest` files. Each engine solves the whole job — it owns its own multi-plate/size strategy rather than being handed one already-sized plate at a time. Fully generic — it never hardcodes drawing geometry, just reads whatever drawings/quantities/plate settings each input file already has.

- `JobLoader` builds `BenchmarkJob`s from a `.nest` file or a folder of them via `NestReader`, using every drawing with `Quantity.Required > 0`. `--sheet-sizes` can sweep a fixed list of plate sizes instead of each file's own.
- `DxfManifestLoader` builds a `BenchmarkJob` from a JSON manifest (`sheetSizes`, `spacing`, `edgeSpacing`, `quadrant`, `parts[] { dxf, quantity, allowRotation }`) instead of a `.nest`, importing each DXF with `CadImporter.ImportDrawing`. DXF paths resolve relative to the manifest; sheet sizes are required (manifest or `--sheet-sizes`, which overrides). `allowRotation: false` locks rotation the same way `NestRunner` does. `JobLoader.Load` routes `*.json` inputs to it, and folder scans pick up `*.nest` plus `*.manifest.json` (plain `*.json` is ignored so `--output` reports are never read as manifests). Invalid manifests throw rather than being skipped.
- `BenchmarkJob.BuildNestJob(maxPlates)` converts the job into a `NestJob`: one `NestJobPart` per requested drawing (via `DrawingJobMapper.FromDrawing`) and one `NestPlateStock` per candidate sheet size (unlimited quantity — the engine decides how many of each size it uses).
- `BenchmarkRunner` fans the (job × engine) pairs out with `Parallel.ForEach` (`NoBuffering`, `MaxDegreeOfParallelism` from `--parallel`, CLI default 3, `Run`'s own default 1) and writes results by index so report order stays job-then-engine. Each solve builds its own `NestJob` snapshot and materialized drawings, so solves share no mutable drawing state. Concurrent solves compete for cores, so `Time(ms)` is only clean at `--parallel 1`. It calls each engine's `INestingEngine.Solve(NestJob)` once per job, under a wall-clock timeout so a runaway or hanging engine can't stall the whole benchmark run, then materializes the result back into legacy `Plate`/`Part` objects via `NestResultMaterializer` for scoring.
- `NestValidator` checks the returned layout: every part inside `Plate.WorkArea()`, every pair at least `Plate.PartSpacing` apart (checked geometrically via each part's own world-space polygon, inflated by the spacing — works on arbitrary concave/holed shapes, not just bounding boxes), and no drawing over its requested quantity. An invalid, throwing, or timed-out run scores zero for that job.
- Scoring matches `Plate.Utilization()` (placed drawing area / full sheet area, `Plate.Area()`). If an engine placed every requested part, ties are broken by fewer plates used (`Report`'s ranking rule) — using fewer sheets to do the same job wastes less material.
- `--engines Name1,Name2` filters to specific registered engines (default: all); `--csv <path>` writes a flat per-job CSV alongside the console report.

### OpenNest.Mcp (console app, depends on Core + Engine + IO)
MCP server for Claude Code integration. Exposes nesting operations as MCP tools over stdio transport. Published to `~/.claude/mcp/OpenNest.Mcp/`.

- **Tools/InputTools**: `load_nest`, `import_dxf`, `create_drawing` (built-in shapes or G-code).
- **Tools/SetupTools**: `create_plate`, `clear_plate`.
- **Tools/NestingTools**: `fill_plate`, `fill_area`, `fill_remnants`, `pack_plate`.
- **Tools/InspectionTools**: `get_plate_info`, `get_parts`, `check_overlaps`.
- `NestSession` — in-memory state across tool calls (current Nest, standalone plates/drawings).

### OpenNest (WinForms WinExe, depends on Core + Engine + IO)
The UI application with MDI interface.

- **Forms/**: `MainForm` (MDI parent), `EditNestForm` (MDI child per nest), `SplitDrawingForm` (split oversized drawings into smaller pieces, launched from CadConverterForm), plus dialogs for plate editing, auto-nesting, DXF conversion, cut parameters, etc.
- **Controls/**: `PlateView` (2D plate renderer with zoom/pan, supports temporary preview parts), `DrawingListBox`, `DrawControl`, `QuadrantSelect`.
- **Actions/**: User interaction modes — `ActionSelect`, `ActionClone`, `ActionFillArea`, `ActionSelectArea`, `ActionZoomWindow`, `ActionSetSequence`, `ActionCutOff`.
- **Post-processing**: `IPostProcessor` plugin interface loaded from DLLs in a `Posts/` directory at runtime.

## File Format

Nest files (`.nest`, ZIP-based) use v2 JSON format:
- `nest.json` — single JSON file containing all nest metadata: nest info (name, units, customer, dates, notes), plate defaults (size, thickness, quadrant, spacing, material, edge spacing), drawings array (id, name, color, quantity, priority, rotation constraints, material, source), and plates array (id, size, material, edge spacing, parts with drawingId/x/y/rotation, cutoffs with x/y/axis/startLimit/endLimit)
- `programs/program-N` — G-code text for each drawing's cut program (N = drawing id)
- `bestfits/bestfit-N` — JSON array of best-fit pair evaluation results per drawing, keyed by plate size/spacing (optional, only present if best-fit data was computed)

## Tool Preferences

Always use Roslyn Bridge MCP tools (`mcp__RoslynBridge__*`) as the primary method for exploring and analyzing this codebase. It is faster and more efficient than file-based searches. Use it for finding symbols, references, diagnostics, type hierarchies, and code navigation. Only fall back to Glob/Grep when Roslyn Bridge cannot fulfill the query.

## Code Style

- Always use `var` instead of explicit types (e.g., `var parts = new List<Part>();` not `List<Part> parts = new List<Part>();`).

## Documentation Maintenance

Always keep `README.md` and `CLAUDE.md` up to date when making changes that affect project structure, architecture, build instructions, dependencies, or key patterns. If you add a new project, change a namespace, modify the build process, or alter significant behavior, update both files as part of the same change.

**Do not commit** design specs, implementation plans, or other temporary planning documents (`docs/superpowers/` etc.) to the repository. These are working documents only — keep them local and untracked.

## Key Patterns

- OpenNest.Core uses multiple namespaces: `OpenNest` (root domain), `OpenNest.CNC`, `OpenNest.Geometry`, `OpenNest.Converters`, `OpenNest.Math`, `OpenNest.Collections`.
- OpenNest.Engine uses sub-namespaces: `OpenNest.Engine.Fill` (fill algorithms), `OpenNest.Engine.Strategies` (pluggable strategy layer), `OpenNest.Engine.BestFit`, `OpenNest.Engine.Jobs` (whole-job API, with `.Placement` and `.Adapters`), `OpenNest.Engine.ML`, `OpenNest.Engine.RapidPlanning`, `OpenNest.Engine.Sequencing`, `OpenNest.Engine.RectanglePacking`, `OpenNest.Engine.CirclePacking`. All Engine types live in namespaces matching their directory under `OpenNest.Engine/` (project files use `namespace X;` file-scoped or block style); consumers reference them via explicit `using OpenNest.Engine[.Sub];` directives.
- `ObservableList<T>` provides ItemAdded/ItemRemoved/ItemChanged events used for automatic quantity tracking between plates and drawings.
- Angles throughout the codebase are in **radians** (use `Angle.ToRadians()`/`Angle.ToDegrees()` for conversion).
- `Tolerance.Epsilon` is used for floating-point comparisons across geometry operations.
- Nesting uses async progress/cancellation: `IProgress<NestProgress>` and `CancellationToken` flow through the engine to the UI's `NestProgressForm`.
- `Compactor` performs post-fill gravity compaction — after filling, parts are pushed toward a plate edge using directional distance calculations to close gaps between irregular shapes.
- `FillScore` uses lexicographic comparison (count > utilization > compactness) to rank fill results consistently across all fill strategies.
- **Cut-off materialization lifecycle**: `CutOff` objects live on `Plate.CutOffs`. Each generates a `Drawing` (with `IsCutOff = true`) whose `Program` contains trimmed line segments. `Plate.RegenerateCutOffs(settings)` removes old cut-off Parts, recomputes programs, and re-adds them to `Plate.Parts`. Regeneration triggers: cut-off add/remove/move, part drag complete, fill complete, plate transform. Cut-off Parts are excluded from quantity tracking, utilization, overlap detection, and nest file serialization (programs are regenerated from definitions on load).
- **User-defined G-code variables**: Programs can contain named variable definitions (`name = expression [inline] [global]`) referenced in coordinates with `$name`. Variables resolve to doubles at parse time for geometry/nesting. `VariableRefs` on `Motion`/`Feedrate` track the symbolic link so post processors can emit machine variable references. Cincinnati post maps non-inline variables to numbered machine variables (`#200+`) with descriptive comments. Global variables share a number across programs; local variables get per-drawing numbers. `ProgramReader` uses a two-pass parse (collect definitions, then parse G-code with substitution). `NestWriter` serializes definitions and `$references` back to text for round-trip fidelity.
- **CAD import pipeline**: All "DXF → Drawing" conversion goes through `OpenNest.IO.CadImporter`. The UI form uses `Import` on file load (storing the mutable result in a `FileListItem`) and `BuildDrawing` on save (passing the user's current visible entities and bends). MCP, API, and Training projects use `ImportDrawing` for headless conversion. The console uses `Import` followed by `BuildDrawing` so it can report bend-repair outcomes. This guarantees all callers produce drawings with the same shape: pierce-point `Source.Offset`, stable `SourceEntities` with GUIDs, `SuppressedEntityIds`, detected bends, and metadata.
- **GravographIS engrave/cut passes**: The `OpenNest.Posts.GravographIS` post splits geometry by `LayerType` into ordered tool passes — engrave (`Scribe`) then cut (`Cut`/`Leadin`/`Leadout`); `Display` is skipped. `ConvertGeometry` tags DXF layers `ENGRAVE`/`ETCH` (lines, arcs, circles) as `Scribe`; the layer round-trips through `.nest` via `NestWriter`/`ProgramReader`. `NestPolylineExtractor.ExtractLayered` carries `LayerType` per polyline (splitting a continuous chain at any layer change); `GravographISPostProcessor.BuildPasses` groups them and `GravographISWriter.Write(IReadOnlyList<GravographPass>, …)` emits each pass at its own feed/depth, parking to origin and emitting an operator pause (motor off → aux off → `LB` console message → motor on) before any pass whose config has `PauseBefore`. Per-pass parameters live in `GravographISPostConfig` (an `IConfigurablePostProcessor` config with `Engrave`/`Cut` `LayerCutConfig` blocks), edited in the shared `PostProcessorConfigForm` PropertyGrid and persisted to JSON. The cut block pauses by default so the operator can swap/adjust the tool (the spring-floated spindle means programmed `DZ` depth is not the real cut depth).
