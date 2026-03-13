# Nest File Format v2 Design

## Problem

The current nest file format stores metadata across three separate XML files (`info`, `drawing-info`, `plate-info`) plus per-plate G-code files for part placements inside a ZIP archive. This results in ~400 lines of hand-written XML read/write code, fragile dictionary-linking to reconnect drawings/plates by ID after parsing, and the overhead of running the full G-code parser just to extract part positions.

## Design

### File Structure

The nest file remains a ZIP archive. Contents:

```
nest.json
programs/
  program-1
  program-2
  ...
```

- **`nest.json`** — single JSON file containing all metadata and part placements.
- **`programs/program-N`** — G-code text for each drawing's CNC program (1-indexed, no zero-padding). Previously stored at the archive root as `program-NNN` (zero-padded). Parsed by `ProgramReader`, written by existing G-code serialization logic. Format unchanged.

Plate G-code files (`plate-NNN`) are removed. Part placements are stored inline in `nest.json`.

### JSON Schema

```json
{
  "version": 2,
  "name": "string",
  "units": "Inches | Millimeters",
  "customer": "string",
  "dateCreated": "2026-03-12T10:30:00",
  "dateLastModified": "2026-03-12T14:00:00",
  "notes": "string (plain JSON, no URI-escaping)",
  "plateDefaults": {
    "size": { "width": 0.0, "height": 0.0 },
    "thickness": 0.0,
    "quadrant": 1,
    "partSpacing": 0.0,
    "material": { "name": "string", "grade": "string", "density": 0.0 },
    "edgeSpacing": { "left": 0.0, "top": 0.0, "right": 0.0, "bottom": 0.0 }
  },
  "drawings": [
    {
      "id": 1,
      "name": "string",
      "customer": "string",
      "color": { "a": 255, "r": 0, "g": 0, "b": 0 },
      "quantity": { "required": 0 },
      "priority": 0,
      "constraints": {
        "stepAngle": 0.0,
        "startAngle": 0.0,
        "endAngle": 0.0,
        "allow180Equivalent": false
      },
      "material": { "name": "string", "grade": "string", "density": 0.0 },
      "source": {
        "path": "string",
        "offset": { "x": 0.0, "y": 0.0 }
      }
    }
  ],
  "plates": [
    {
      "id": 1,
      "size": { "width": 0.0, "height": 0.0 },
      "thickness": 0.0,
      "quadrant": 1,
      "quantity": 1,
      "partSpacing": 0.0,
      "material": { "name": "string", "grade": "string", "density": 0.0 },
      "edgeSpacing": { "left": 0.0, "top": 0.0, "right": 0.0, "bottom": 0.0 },
      "parts": [
        { "drawingId": 1, "x": 0.0, "y": 0.0, "rotation": 0.0 }
      ]
    }
  ]
}
```

Key details:
- **Version**: `"version": 2` at the top level for future format migration.
- Drawing `id` values are 1-indexed, matching `programs/program-N` filenames.
- Part `rotation` is stored in **radians** (matches internal domain model, no conversion needed).
- Part `drawingId` references the drawing's `id` in the `drawings` array.
- **Dates**: local time, serialized via `DateTime.ToString("o")` (ISO 8601 round-trip format with timezone offset).
- **Notes**: stored as plain JSON strings. The v1 URI-escaping (`Uri.EscapeDataString`) is not needed since JSON handles special characters natively.
- `quantity.required` is the only quantity persisted; `nested` is computed at load time from part placements.
- **Units**: enum values match the domain model: `Inches` or `Millimeters`.
- **Size**: uses `width`/`height` matching the `OpenNest.Geometry.Size` struct.
- **Drawing.Priority** and **Drawing.Constraints** (stepAngle, startAngle, endAngle, allow180Equivalent) are now persisted (v1 omitted these).
- **Empty collections**: `drawings` and `plates` arrays are always present (may be empty `[]`). The `programs/` folder is empty when there are no drawings.

### Serialization Approach

Use `System.Text.Json` with small DTO (Data Transfer Object) classes for serialization. The DTOs map between the domain model and the JSON structure, keeping serialization concerns out of the domain classes.

### What Changes

| File | Change |
|------|--------|
| `NestWriter.cs` | Replace all XML writing and plate G-code writing with JSON serialization. Programs written to `programs/` folder. |
| `NestReader.cs` | Replace all XML parsing, plate G-code parsing, and dictionary-linking with JSON deserialization. Programs read from `programs/` folder. |

### What Stays the Same

| File | Reason |
|------|--------|
| `ProgramReader.cs` | G-code parsing for CNC programs is unchanged. |
| `NestWriter` G-code writing (`WriteDrawing`, `GetCodeString`) | G-code serialization for programs is unchanged. |
| `DxfImporter.cs`, `DxfExporter.cs`, `Extensions.cs` | Unrelated to nest file format. |
| Domain model classes | No changes needed. |

### Public API

The public API is unchanged:
- `NestReader(string file)` and `NestReader(Stream stream)` constructors preserved.
- `NestReader.Read()` returns `Nest`.
- `NestWriter(Nest nest)` constructor preserved.
- `NestWriter.Write(string file)` returns `bool`.

### Callers (no changes needed)

- `MainForm.cs:329` — `new NestReader(path)`
- `MainForm.cs:363` — `new NestReader(dlg.FileName)`
- `EditNestForm.cs:212` — `new NestWriter(Nest)`
- `EditNestForm.cs:223` — `new NestWriter(nst)`
- `Document.cs:27` — `new NestWriter(Nest)`
- `OpenNest.Console/Program.cs:94` — `new NestReader(nestFile)`
- `OpenNest.Console/Program.cs:190` — `new NestWriter(nest)`
- `OpenNest.Mcp/InputTools.cs:30` — `new NestReader(path)`
