# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

OpenNest is a Windows desktop application for CNC nesting — arranging 2D parts on material plates to minimize waste. It imports DXF drawings, places parts onto plates using NFP-based (No Fit Polygon) and rectangle-packing algorithms, and can export nest layouts as DXF or post-process them to G-code for CNC cutting machines.

## Build

This is a .NET 8 solution using SDK-style `.csproj` files targeting `net8.0-windows`. Build with:

```bash
dotnet build OpenNest.sln
```

NuGet dependencies: `ACadSharp` 3.1.32 (DXF/DWG import/export, in OpenNest.IO), `System.Drawing.Common` 8.0.10, `ModelContextProtocol` + `Microsoft.Extensions.Hosting` (in OpenNest.Mcp), `Microsoft.ML.OnnxRuntime` (in OpenNest.Engine for ML angle prediction), `Microsoft.EntityFrameworkCore.Sqlite` (in OpenNest.Training).

## Architecture

Eight projects form a layered architecture:

### OpenNest.Core (class library)
Domain model, geometry, and CNC primitives organized into namespaces:

- **Root** (`namespace OpenNest`): Domain model — `Nest` → `Plate[]` → `Part[]` → `Drawing` → `Program`. A `Nest` is the top-level container. Each `Plate` has a size, material, quadrant, spacing, and contains placed `Part` instances. Each `Part` references a `Drawing` (the template) and has its own location/rotation. A `Drawing` wraps a CNC `Program`. Also contains utilities: `PartGeometry`, `Align`, `Sequence`, `Timing`.
- **CNC** (`CNC/`, `namespace OpenNest.CNC`): `Program` holds a list of `ICode` instructions (G-code-like: `RapidMove`, `LinearMove`, `ArcMove`, `SubProgramCall`). Programs support absolute/incremental mode conversion, rotation, offset, bounding box calculation, and cloning.
- **Geometry** (`Geometry/`, `namespace OpenNest.Geometry`): Spatial primitives (`Vector`, `Box`, `Size`, `Spacing`, `BoundingBox`, `IBoundable`) and higher-level shapes (`Line`, `Arc`, `Circle`, `Polygon`, `Shape`) used for intersection detection, area calculation, and DXF conversion. Also contains `Intersect` (intersection algorithms), `ShapeBuilder` (entity chaining), `GeometryOptimizer` (line/arc merging), `SpatialQuery` (directional distance, ray casting, box queries), `ShapeProfile` (perimeter/area analysis), `NoFitPolygon`, `InnerFitPolygon`, `ConvexHull`, `ConvexDecomposition`, and `RotatingCalipers`.
- **Converters** (`Converters/`, `namespace OpenNest.Converters`): Bridges between CNC and Geometry — `ConvertProgram` (CNC→Geometry), `ConvertGeometry` (Geometry→CNC), `ConvertMode` (absolute↔incremental).
- **Math** (`Math/`, `namespace OpenNest.Math`): `Angle` (radian/degree conversion), `Tolerance` (floating-point comparison), `Trigonometry`, `Generic` (swap utility), `EvenOdd`, `Rounding` (factor-based rounding). Note: `OpenNest.Math` shadows `System.Math` — use `System.Math` fully qualified where both are needed.
- **CNC/CuttingStrategy** (`CNC/CuttingStrategy/`, `namespace OpenNest.CNC`): `ContourCuttingStrategy` orchestrates cut ordering, lead-ins/lead-outs, and tabs. Includes `LeadIn`/`LeadOut` hierarchies (line, arc, clean-hole variants), `Tab` hierarchy (normal, machine, breaker), and `CuttingParameters`/`AssignmentParameters`/`SequenceParameters` configuration.
- **Collections** (`Collections/`, `namespace OpenNest.Collections`): `ObservableList<T>`, `DrawingCollection`.
- **Quadrant system**: Plates use quadrants 1-4 (like Cartesian quadrants) to determine coordinate origin placement. This affects bounding box calculation, rotation, and part positioning.

### OpenNest.Engine (class library, depends on Core)
Nesting algorithms with a pluggable engine architecture. `NestEngineBase` is the abstract base class; `DefaultNestEngine` (formerly `NestEngine`) provides the multi-phase fill strategy. `NestEngineRegistry` manages available engines (built-in + plugins from `Engines/` directory) and the globally active engine. `AutoNester` handles mixed-part NFP-based nesting with simulated annealing (not yet integrated into the registry).

- **Engine hierarchy**: `NestEngineBase` (abstract) → `DefaultNestEngine` (Linear, Pairs, RectBestFit, Remainder phases). Custom engines subclass `NestEngineBase` and register via `NestEngineRegistry.Register()` or as plugin DLLs in `Engines/`.
- **NestEngineRegistry**: Static registry — `Create(Plate)` factory, `ActiveEngineName` global selection, `LoadPlugins(directory)` for DLL discovery. All callsites use `NestEngineRegistry.Create(plate)` except `BruteForceRunner` which uses `new DefaultNestEngine(plate)` directly for training consistency.
- **Fill/** (`namespace OpenNest.Engine.Fill`): Fill algorithms — `FillLinear` (grid-based), `FillExtents` (extents-based pair tiling), `PairFiller` (interlocking pairs), `ShrinkFiller`, `RemnantFiller`/`RemnantFinder`, `Compactor` (post-fill gravity compaction), `FillScore` (lexicographic comparison: count > utilization > compactness), `Pattern`/`PatternTiler`, `PartBoundary`, `RotationAnalysis`, `AngleCandidateBuilder`, `BestCombination`, `AccumulatingProgress`.
- **Strategies/** (`namespace OpenNest.Engine.Strategies`): Pluggable fill strategy layer — `IFillStrategy` interface, `FillContext`, `FillStrategyRegistry` (auto-discovers strategies via reflection, supports plugin DLLs), `FillHelpers`. Built-in strategies: `LinearFillStrategy`, `PairsFillStrategy`, `RectBestFitStrategy`, `ExtentsFillStrategy`.
- **BestFit/** (`namespace OpenNest.Engine.BestFit`): NFP-based pair evaluation pipeline — `BestFitFinder` orchestrates angle sweeps, `PairEvaluator`/`IPairEvaluator` scores part pairs, `RotationSlideStrategy`/`ISlideComputer` computes slide distances. `BestFitCache` and `BestFitFilter` optimize repeated lookups.
- **RectanglePacking/** (`namespace OpenNest.RectanglePacking`): `FillBestFit` (single-item fill, tries horizontal and vertical orientations), `PackBottomLeft` (multi-item bin packing, sorts by area descending). Both operate on `Bin`/`Item` abstractions.
- **CirclePacking/** (`namespace OpenNest.CirclePacking`): Alternative packing for circular parts.
- **Nfp/** (`namespace OpenNest.Engine.Nfp`): NFP-based nesting (not yet integrated) — `AutoNester` (mixed-part nesting with simulated annealing), `BottomLeftFill` (BLF placement), `NfpCache` (computed NFP caching), `SimulatedAnnealing` (optimizer), `INestOptimizer`/`NestResult`.
- **ML/** (`namespace OpenNest.Engine.ML`): `AnglePredictor` (ONNX model for predicting good rotation angles), `FeatureExtractor` (part geometry features), `BruteForceRunner` (full angle sweep for training data).
- `NestItem`: Input to the engine — wraps a `Drawing` with quantity, priority, and rotation constraints.
- `NestProgress`: Progress reporting model with `NestPhase` enum for UI feedback.

### OpenNest.IO (class library, depends on Core)
File I/O and format conversion. Uses ACadSharp for DXF/DWG support.

- `DxfImporter`/`DxfExporter` — DXF file import/export via ACadSharp.
- `NestReader`/`NestWriter` — custom ZIP-based nest format (JSON metadata + G-code programs, v2 format).
- `ProgramReader` — G-code text parser.
- `Extensions` — conversion helpers between ACadSharp and OpenNest geometry types.

### OpenNest.Console (console app, depends on Core + Engine + IO)
Command-line interface for batch nesting. Supports DXF import, plate configuration, linear fill, and NFP-based auto-nesting (`--autonest`).

### OpenNest.Gpu (class library, depends on Core + Engine)
GPU-accelerated pair evaluation for best-fit nesting. `GpuPairEvaluator` implements `IPairEvaluator`, `GpuSlideComputer` implements `ISlideComputer`, and `PartBitmap` handles rasterization. `GpuEvaluatorFactory` provides factory methods.

### OpenNest.Training (console app, depends on Core + Engine)
Training data collection for ML angle prediction. `TrainingDatabase` stores per-angle nesting results in SQLite via EF Core for offline model training.

### OpenNest.Mcp (console app, depends on Core + Engine + IO)
MCP server for Claude Code integration. Exposes nesting operations as MCP tools over stdio transport. Published to `~/.claude/mcp/OpenNest.Mcp/`.

- **Tools/InputTools**: `load_nest`, `import_dxf`, `create_drawing` (built-in shapes or G-code).
- **Tools/SetupTools**: `create_plate`, `clear_plate`.
- **Tools/NestingTools**: `fill_plate`, `fill_area`, `fill_remnants`, `pack_plate`.
- **Tools/InspectionTools**: `get_plate_info`, `get_parts`, `check_overlaps`.
- `NestSession` — in-memory state across tool calls (current Nest, standalone plates/drawings).

### OpenNest (WinForms WinExe, depends on Core + Engine + IO)
The UI application with MDI interface.

- **Forms/**: `MainForm` (MDI parent), `EditNestForm` (MDI child per nest), plus dialogs for plate editing, auto-nesting, DXF conversion, cut parameters, etc.
- **Controls/**: `PlateView` (2D plate renderer with zoom/pan, supports temporary preview parts), `DrawingListBox`, `DrawControl`, `QuadrantSelect`.
- **Actions/**: User interaction modes — `ActionSelect`, `ActionClone`, `ActionFillArea`, `ActionSelectArea`, `ActionZoomWindow`, `ActionSetSequence`.
- **Post-processing**: `IPostProcessor` plugin interface loaded from DLLs in a `Posts/` directory at runtime.

## File Format

Nest files (`.nest`, ZIP-based) use v2 JSON format:
- `nest.json` — single JSON file containing all nest metadata: nest info (name, units, customer, dates, notes), plate defaults (size, thickness, quadrant, spacing, material, edge spacing), drawings array (id, name, color, quantity, priority, rotation constraints, material, source), and plates array (id, size, material, edge spacing, parts with drawingId/x/y/rotation)
- `programs/program-N` — G-code text for each drawing's cut program (N = drawing id)
- `bestfits/bestfit-N` — JSON array of best-fit pair evaluation results per drawing, keyed by plate size/spacing (optional, only present if best-fit data was computed)

## Tool Preferences

Always use Roslyn Bridge MCP tools (`mcp__RoslynBridge__*`) as the primary method for exploring and analyzing this codebase. It is faster and more efficient than file-based searches. Use it for finding symbols, references, diagnostics, type hierarchies, and code navigation. Only fall back to Glob/Grep when Roslyn Bridge cannot fulfill the query.

## Code Style

- Always use `var` instead of explicit types (e.g., `var parts = new List<Part>();` not `List<Part> parts = new List<Part>();`).

## Documentation Maintenance

Always keep `README.md` and `CLAUDE.md` up to date when making changes that affect project structure, architecture, build instructions, dependencies, or key patterns. If you add a new project, change a namespace, modify the build process, or alter significant behavior, update both files as part of the same change.

## Key Patterns

- OpenNest.Core uses multiple namespaces: `OpenNest` (root domain), `OpenNest.CNC`, `OpenNest.Geometry`, `OpenNest.Converters`, `OpenNest.Math`, `OpenNest.Collections`.
- OpenNest.Engine uses sub-namespaces: `OpenNest.Engine.Fill` (fill algorithms), `OpenNest.Engine.Strategies` (pluggable strategy layer), `OpenNest.Engine.BestFit`, `OpenNest.Engine.Nfp` (NFP-based nesting, not yet integrated), `OpenNest.Engine.ML`, `OpenNest.Engine.RapidPlanning`, `OpenNest.Engine.Sequencing`.
- `ObservableList<T>` provides ItemAdded/ItemRemoved/ItemChanged events used for automatic quantity tracking between plates and drawings.
- Angles throughout the codebase are in **radians** (use `Angle.ToRadians()`/`Angle.ToDegrees()` for conversion).
- `Tolerance.Epsilon` is used for floating-point comparisons across geometry operations.
- Nesting uses async progress/cancellation: `IProgress<NestProgress>` and `CancellationToken` flow through the engine to the UI's `NestProgressForm`.
- `Compactor` performs post-fill gravity compaction — after filling, parts are pushed toward a plate edge using directional distance calculations to close gaps between irregular shapes.
- `FillScore` uses lexicographic comparison (count > utilization > compactness) to rank fill results consistently across all fill strategies.
