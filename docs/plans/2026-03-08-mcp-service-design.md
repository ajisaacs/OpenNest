# OpenNest MCP Service + IO Library Refactor

## Goal

Create an MCP server so Claude Code can load nest files, run nesting algorithms, and inspect results — enabling rapid iteration on nesting strategies without launching the WinForms app.

## Project Changes

```
OpenNest.Core          (no external deps)  — add Plate.GetRemnants()
OpenNest.Engine        → Core
OpenNest.IO (NEW)      → Core + ACadSharp  — extracted from OpenNest/IO/
OpenNest.Mcp (NEW)     → Core + Engine + IO
OpenNest (WinForms)    → Core + Engine + IO (drops ACadSharp direct ref)
```

## OpenNest.IO Library

New class library. Move from the UI project (`OpenNest/IO/`):

- `DxfImporter`
- `DxfExporter`
- `NestReader`
- `NestWriter`
- `ProgramReader`
- ACadSharp NuGet dependency (3.1.32)

The WinForms project drops its direct ACadSharp reference and references OpenNest.IO instead.

## Plate.GetRemnants()

Add to `Plate` in Core. Simple strip-based scan:

1. Collect all part bounding boxes inflated by `PartSpacing`.
2. Scan the work area for clear rectangular strips along edges and between part columns/rows.
3. Return `List<Box>` of usable empty regions.

No engine dependency — uses only work area and part bounding boxes already available on Plate.

## MCP Tools

### Input
| Tool | Description |
|------|-------------|
| `load_nest` | Load a `.nest` zip file, returns nest summary (plates, drawings, part counts) |
| `import_dxf` | Import a DXF file as a drawing |
| `create_drawing` | Create from built-in shape primitive (rect, circle, L, T) or raw G-code string |

### Setup
| Tool | Description |
|------|-------------|
| `create_plate` | Define a plate with dimensions, spacing, edge spacing, quadrant |
| `clear_plate` | Remove all parts from a plate |

### Nesting
| Tool | Description |
|------|-------------|
| `fill_plate` | Fill entire plate with a single drawing (NestEngine.Fill) |
| `fill_area` | Fill a specific box region on a plate |
| `fill_remnants` | Auto-detect remnants via Plate.GetRemnants(), fill each with a drawing |
| `pack_plate` | Multi-drawing bin packing (NestEngine.Pack) |

### Inspection
| Tool | Description |
|------|-------------|
| `get_plate_info` | Dimensions, part count, utilization %, remnant boxes |
| `get_parts` | List placed parts with location, rotation, bounding box |
| `check_overlaps` | Run overlap detection, return collision points |

## Example Workflow

```
load_nest("N0308-008.zip")
→ 1 plate (36x36), 75 parts, 1 drawing (Converto 3 YRD DUMPER), utilization 80.2%

get_plate_info(plate: 0)
→ utilization: 80.2%, remnants: [{x:33.5, y:0, w:2.5, h:36}]

fill_remnants(plate: 0, drawing: "Converto 3 YRD DUMPER")
→ added 3 parts, new utilization: 83.1%

check_overlaps(plate: 0)
→ no overlaps
```

## MCP Server Implementation

- .NET 8 console app using stdio transport
- Published to `~/.claude/mcp/OpenNest.Mcp/`
- Registered in `~/.claude/settings.local.json`
- In-memory state: holds the current `Nest` object across tool calls
