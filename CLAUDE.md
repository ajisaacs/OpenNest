# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

OpenNest is a Windows desktop application for CNC nesting — arranging 2D parts on material plates to minimize waste. It imports DXF drawings, places parts onto plates using rectangle-packing algorithms, and can export nest layouts as DXF or post-process them to G-code for CNC cutting machines.

## Build

This is a .NET Framework 4.8 solution using legacy-style `.csproj` files (not SDK-style). Build with:

```bash
msbuild OpenNest.sln /p:Configuration=Release
```

NuGet dependency: `netDxf` (referenced via `packages/` folder, not PackageReference). Restore with `nuget restore OpenNest.sln` if needed.

No test projects exist in this solution.

## Architecture

Three projects form a layered architecture:

### OpenNest.Core (class library)
Domain model, geometry, and CNC primitives organized into namespaces:

- **Root** (`namespace OpenNest`): Domain model — `Nest` → `Plate[]` → `Part[]` → `Drawing` → `Program`. A `Nest` is the top-level container. Each `Plate` has a size, material, quadrant, spacing, and contains placed `Part` instances. Each `Part` references a `Drawing` (the template) and has its own location/rotation. A `Drawing` wraps a CNC `Program`. Also contains utilities: `Helper`, `Align`, `Sequence`, `Timing`.
- **CNC** (`CNC/`, `namespace OpenNest.CNC`): `Program` holds a list of `ICode` instructions (G-code-like: `RapidMove`, `LinearMove`, `ArcMove`, `SubProgramCall`). Programs support absolute/incremental mode conversion, rotation, offset, bounding box calculation, and cloning.
- **Geometry** (`Geometry/`, `namespace OpenNest.Geometry`): Spatial primitives (`Vector`, `Box`, `Size`, `Spacing`, `BoundingBox`, `IBoundable`) and higher-level shapes (`Line`, `Arc`, `Circle`, `Polygon`, `Shape`) used for intersection detection, area calculation, and DXF conversion.
- **Converters** (`Converters/`, `namespace OpenNest.Converters`): Bridges between CNC and Geometry — `ConvertProgram` (CNC→Geometry), `ConvertGeometry` (Geometry→CNC), `ConvertMode` (absolute↔incremental).
- **Math** (`Math/`, `namespace OpenNest.Math`): `Angle` (radian/degree conversion), `Tolerance` (floating-point comparison), `Trigonometry`, `Generic` (swap utility), `EvenOdd`. Note: `OpenNest.Math` shadows `System.Math` — use `System.Math` fully qualified where both are needed.
- **Collections** (`Collections/`, `namespace OpenNest.Collections`): `ObservableList<T>`, `DrawingCollection`.
- **Quadrant system**: Plates use quadrants 1-4 (like Cartesian quadrants) to determine coordinate origin placement. This affects bounding box calculation, rotation, and part positioning.

### OpenNest.Engine (class library, depends on Core)
Nesting algorithms. `NestEngine` orchestrates filling plates with parts.

- **RectanglePacking/**: `FillBestFit` (single-item fill, tries horizontal and vertical orientations), `PackBottomLeft` (multi-item bin packing, sorts by area descending). Both operate on `Bin`/`Item` abstractions.
- **CirclePacking/**: Alternative packing for circular parts.
- `NestItem`: Input to the engine — wraps a `Drawing` with quantity, priority, and rotation constraints.
- `BestCombination`: Finds optimal mix of normal/rotated columns for grid fills.

### OpenNest (WinForms WinExe, depends on Core + Engine)
The UI application with MDI interface.

- **Forms/**: `MainForm` (MDI parent), `EditNestForm` (MDI child per nest), plus dialogs for plate editing, auto-nesting, DXF conversion, cut parameters, etc.
- **Controls/**: `PlateView` (2D plate renderer with zoom/pan), `DrawingListBox`, `DrawControl`, `QuadrantSelect`.
- **Actions/**: User interaction modes — `ActionSelect`, `ActionAddPart`, `ActionClone`, `ActionFillArea`, `ActionZoomWindow`, `ActionSetSequence`.
- **IO/**: `DxfImporter`/`DxfExporter` (via netDxf library), `NestReader`/`NestWriter` (custom ZIP-based format with XML metadata + G-code programs), `ProgramReader`.
- **Post-processing**: `IPostProcessor` plugin interface loaded from DLLs in a `Posts/` directory at runtime.

## File Format

Nest files (`.zip`) contain:
- `info` — XML with nest metadata and plate defaults
- `drawing-info` — XML with drawing metadata (name, material, quantities, colors)
- `plate-info` — XML with plate metadata (size, material, spacing)
- `program-NNN` — G-code text for each drawing's cut program
- `plate-NNN` — G-code text encoding part placements (G00 for position, G65 for sub-program call with rotation)

## Key Patterns

- OpenNest.Core uses multiple namespaces: `OpenNest` (root domain), `OpenNest.CNC`, `OpenNest.Geometry`, `OpenNest.Converters`, `OpenNest.Math`, `OpenNest.Collections`.
- `ObservableList<T>` provides ItemAdded/ItemRemoved/ItemChanged events used for automatic quantity tracking between plates and drawings.
- Angles throughout the codebase are in **radians** (use `Angle.ToRadians()`/`Angle.ToDegrees()` for conversion).
- `Tolerance.Epsilon` is used for floating-point comparisons across geometry operations.
