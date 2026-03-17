# OpenNest

A Windows desktop app for CNC nesting — imports DXF drawings, arranges parts on plates and exports layouts as DXF or G-code for cutting.

![OpenNest - parts nested on a 36x36 plate](screenshots/screenshot-nest-1.png)

OpenNest takes your part drawings, lets you define your sheet (plate) sizes, and arranges the parts to make efficient use of material. The result can be exported as DXF files or post-processed into G-code that your CNC cutting machine understands.

## Features

- **DXF Import/Export** — Load part drawings from DXF files and export completed nest layouts
- **Multiple Fill Strategies** — Grid-based linear fill, NFP (No Fit Polygon) pair fitting, and rectangle bin packing
- **Part Rotation** — Automatically tries different rotation angles to find better fits
- **Gravity Compaction** — After placing parts, pushes them together to close gaps
- **Multi-Plate Support** — Work with multiple plates of different sizes and materials in a single nest
- **G-code Output** — Post-process nested layouts to G-code for CNC cutting machines
- **Built-in Shapes** — Create basic geometric parts (circles, rectangles, triangles, etc.) without needing a DXF file
- **Interactive Editing** — Zoom, pan, select, clone, and manually arrange parts on the plate view
- **Lead-in/Lead-out & Tabs** — Configure cutting parameters like approach paths and holding tabs

![OpenNest - 44 parts nested on a 60x120 plate](screenshots/screenshot-nest-2.png)

## Prerequisites

- **Windows 10 or later**
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Getting Started

### Build

```bash
git clone https://github.com/ajisaacs/OpenNest.git
cd OpenNest
dotnet build OpenNest.sln
```

### Run

```bash
dotnet run --project OpenNest/OpenNest.csproj
```

Or open `OpenNest.sln` in Visual Studio and run the `OpenNest` project.

### Quick Walkthrough

1. **Create a nest** — File > New Nest
2. **Add drawings** — Import DXF files or create built-in shapes (rectangles, circles, etc.). DXF drawings should be 1:1 scale CAD files.
3. **Set up a plate** — Define the plate size and material
4. **Fill the plate** — The nesting engine will automatically arrange parts on the plate
5. **Export** — Save as a `.nest` file, export to DXF, or post-process to G-code

<!-- TODO: Add screenshots for each step -->

## Project Structure

```
OpenNest.sln
├── OpenNest/              # WinForms desktop application (UI)
├── OpenNest.Core/         # Domain model, geometry, and CNC primitives
├── OpenNest.Engine/       # Nesting algorithms (fill, pack, compact)
├── OpenNest.IO/           # File I/O — DXF import/export, nest file format
├── OpenNest.Console/      # Command-line interface for batch nesting
├── OpenNest.Gpu/          # GPU-accelerated nesting evaluation
├── OpenNest.Training/     # ML training data collection
├── OpenNest.Mcp/          # MCP server for AI tool integration
└── OpenNest.Tests/        # Unit tests
```

For most users, only the first four matter:

| Project | What it does |
|---------|-------------|
| **OpenNest** | The app you run. WinForms UI with plate viewer, drawing list, and dialogs. |
| **OpenNest.Core** | The building blocks — parts, plates, drawings, geometry, G-code representation. |
| **OpenNest.Engine** | The brains — algorithms that decide where parts go on a plate. |
| **OpenNest.IO** | Reads and writes files — DXF (via ACadSharp), G-code, and the `.nest` ZIP format. |

## Keyboard Shortcuts

| Key | Action |
|-----|--------|
| `Ctrl+F` | Fill the area around the cursor with the selected drawing |
| `F` | Zoom to fit the plate view |

## Supported Formats

| Format | Import | Export |
|--------|--------|--------|
| DXF (AutoCAD Drawing Exchange) | Yes | Yes |
| DWG (AutoCAD Drawing) | Yes | No |
| G-code | No | Yes (via post-processors) |
| `.nest` (ZIP-based project file) | Yes | Yes |

## Status

OpenNest is under active development. The core nesting workflows function, but there's plenty of room for improvement in packing efficiency, UI polish, and format support. Contributions and feedback are welcome.

## License

This project is licensed under the [MIT License](LICENSE).
