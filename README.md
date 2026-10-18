# OpenNest

A Windows desktop application for CNC nesting — imports DXF drawings, arranges parts on material plates, and exports layouts as DXF or G-code for cutting.

<p>
  <a href="screenshots/screenshot-nest-1.png"><img src="screenshots/screenshot-nest-1.png" width="420" alt="OpenNest - parts nested on a 36x36 plate"></a>
  <a href="screenshots/screenshot-nest-2.png"><img src="screenshots/screenshot-nest-2.png" width="420" alt="OpenNest - 44 parts nested on a 60x120 plate"></a>
</p>

## Features

- **Import / export** — DXF & DWG parts (ACadSharp), Excel BOMs, bend-line detection, built-in parametric shapes; export DXF or post-processed G-code.
- **Nesting** — automatic multi-plate/multi-material jobs, interlocking pair evaluation, and built-in or plug-in engines. [Choose an engine](docs/nesting-engines.md).
- **Plate operations** — manual sheet cut-offs, [plate- or nest-wide automatic scrap cutoffs with a minimum tail-to-keep setting](docs/automatic-scrap-cutoffs.md), oversized-part splitting (straight, weld-gap tabs, spike-groove), interactive editing, and spacing-aware pushes that can slide along or away from touching parts.
- **Visual overlap check** — highlight shared material on the active plate, rechecked automatically after edits, including containment and cutouts, with area shading, pair markers, and hover details through View > Overlap Check. [Usage and limitations](docs/geometry/visual-overlap-check.md).
- **Cut simulator** — Plate > Simulate Cutting replays the active plate in cut order: the torch traces each cut, rapid and pierce at adjustable feed, rapid and pierce settings, with play/pause, scrubbing, 1×–250× speed and previous/next part (Page Up/Down) and cutout (Ctrl+Left/Right) jumps. Parts the torch has not reached stay greyed. It and Calculate Cut Time use separate outside and hole feeds, skip etch-only contours like the CI Fiber post, and apply the chosen machine's acceleration and corner limits from Machine Configuration (placeholder values until you enter the controller's). [Timing model](docs/cut-time-estimates.md).
- **CNC output** — configurable lead-ins/outs and tabs, contour editing, user-defined G-code variables (`$name` → `#200+` machine variables), plugin post-processors (Cincinnati CL-707/800/900/940/CLX included). [Pre-post verification](docs/post-verification.md) checks overlaps, missing lead-ins, and rapid crossings, with explicit risk acknowledgment required to bypass warnings.

## Requirements

- Windows 10+ for the desktop app; the console, API, and most test projects build on Linux/macOS too.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source.

Windows release ZIPs are self-contained: extract the entire archive into a new folder and run `OpenNest.exe`; no separate .NET installation is needed. The Rectangles and Irregular nesting engines are built in; see [nesting engines](docs/nesting-engines.md). Use the ZIP and SHA-256 checksum from [GitHub Releases](https://github.com/ajisaacs/OpenNest/releases), not the source-code archives.

## Build, Test, Run

```bash
git clone https://github.com/ajisaacs/OpenNest.git
cd OpenNest
dotnet build OpenNest.sln                                       # full solution (Windows)
dotnet test OpenNest.Engine.Tests/OpenNest.Engine.Tests.csproj  # cross-platform engine tests
dotnet test OpenNest.Tests/OpenNest.Tests.csproj                # core/engine/IO/API tests
dotnet test OpenNest.FrontEnd.Tests/OpenNest.FrontEnd.Tests.csproj # console/MCP/API integration
dotnet run --project OpenNest/OpenNest.csproj                   # desktop app (Windows)
```

`OpenNest.WinForms.Tests` (desktop-assembly tests) runs on Windows only; CI runs it and `OpenNest.FrontEnd.Tests` on a GitHub-hosted Windows runner for every master push and pull request.

### Quick start

1. File > New Nest
2. Import DXFs via the CAD Converter (layer/color filtering, per-entity exclude and cut/etch/display-only roles, bend detection, G-code preview) or create built-in shapes
3. Define plate size, material, quadrant, spacing
4. Fill — the engine arranges parts
5. Optionally add cut-off lines, apply Part Sequencing to order crossing cut-offs before their parts, then save `.nest`, export DXF, or post-process to G-code

Review part spacing before cutting, especially for interlocking pairs. See [pair-spacing checks and current limitations](docs/geometry/pair-spacing.md).

## Command-Line Interface

```bash
dotnet run --project OpenNest.Console -- part.dxf --size 60x120               # fill one plate
dotnet run --project OpenNest.Console -- part1.dxf part2.dxf --size 60x120 --autonest
dotnet run --project OpenNest.Console -- project.zip                          # re-fill a nest file
```

| Option | What it does |
|--------|--------------|
| `--size WxL` | Set the plate size; required for DXF-only input. |
| `--autonest` | Run validated whole-job nesting on one sheet. |
| `--engine <name>` | Choose a whole-job engine with `--autonest`, or a fill strategy without it. |
| `--quantity <n>` | Limit parts placed (0 means unlimited). |
| `--spacing <value>` | Override part spacing. |
| `--template <path>` | Read plate defaults from a nest file. |
| `--output <path>` | Set the output nest path. |
| `--check-overlaps` | Check the result for overlaps. |
| `--post <name>` | Run a post-processor after nesting. |
| `--no-save` | Skip saving the nest. |
| `--allow-invalid` | Explicitly keep a representable invalid `--autonest` result; otherwise it is rejected. |

Run `dotnet run --project OpenNest.Console -- --help` for the complete options and their defaults.

## Project Structure

| Project | Purpose |
|---------|---------|
| **OpenNest** | WinForms desktop app |
| **OpenNest.Core** | Domain model, geometry, CNC primitives; [material-overlap diagnostics](docs/geometry/visual-overlap-check.md) |
| **OpenNest.Engine** | Nesting algorithms and whole-job contracts |
| **OpenNest.IO** | DXF/DWG, `.nest`, G-code, BOM I/O; CAD import |
| **OpenNest.Console** | Headless batch nesting |
| **OpenNest.Api / .Data** | Programmatic pipeline; machine & cutting-parameter data |
| **OpenNest.Gpu** | GPU-accelerated pair evaluation (ILGPU) |
| **OpenNest.Benchmark** | Head-to-head engine comparison |
| **OpenNest.Mcp** | MCP server for AI tool integration |
| **Posts/** | Post-processor plugins (Cincinnati CL / CI Fiber lasers, Gravograph IS8000) |
| **\*.Tests** | Cross-platform suites; WinForms tests are Windows-only |

## Nesting Engines

Default compares the built-in Irregular, Rectangles and Circles engines and keeps the best valid whole-job layout. Other engines and plug-ins suit particular jobs; see [which engine to use](docs/nesting-engines.md). Desktop Auto Nest, console autonest, MCP autonest, and the API share an independent validation pipeline, including for plug-ins. [Automatic nesting and validation](docs/automatic-nesting.md) explains how callers handle invalid results.

## File Format

`.nest` files are ZIP archives containing drawing programs, metadata, plates, and placements. Saved nests retain each part's lead-ins, lead-outs, tab gaps, and locks, plus the plate's cutting settings. Changing a drawing's geometry removes obsolete cutting paths from its parts; name, quantity, and color edits preserve them. See the [file-format reference](docs/nest-file-format.md) for compatibility and recovery behavior.

## Supported Formats

| Format | Import | Export |
|--------|--------|--------|
| DXF | Yes | Yes |
| DWG | Yes | No |
| Excel BOM | Yes | No |
| G-code | No | Yes (post-processors) |
| `.nest` | Yes | Yes |

## Keyboard Shortcuts

`Ctrl+F` fill area · `F` zoom to fit · `Shift+wheel` / middle-click rotate · `X`/`Y` push · arrows nudge · `Shift+arrow` push.

## Status & License

Actively developed; core workflows run end-to-end from DXF import to G-code. Contributions welcome. MIT licensed — see [LICENSE](LICENSE).
