# Nest File Format v2 Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the XML+G-code nest file format with a single `nest.json` metadata file plus `programs/` folder inside the ZIP archive.

**Architecture:** Add a `NestFormat` static class containing DTO records and shared JSON options. Rewrite `NestWriter` to serialize DTOs to JSON and write programs under `programs/`. Rewrite `NestReader` to deserialize JSON and read programs from `programs/`. Public API unchanged.

**Tech Stack:** `System.Text.Json` (built into .NET 8, no new packages needed)

**Spec:** `docs/superpowers/specs/2026-03-12-nest-file-format-v2-design.md`

---

## File Structure

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `OpenNest.IO/NestFormat.cs` | DTO records for JSON serialization + shared `JsonSerializerOptions` |
| Rewrite | `OpenNest.IO/NestWriter.cs` | Serialize nest to JSON + write programs to `programs/` folder |
| Rewrite | `OpenNest.IO/NestReader.cs` | Deserialize JSON + read programs from `programs/` folder |

No other files change. `ProgramReader.cs`, `DxfImporter.cs`, `DxfExporter.cs`, `Extensions.cs`, all domain model classes, and all caller sites remain untouched.

---

## Chunk 1: DTO Records and JSON Options

### Task 1: Create NestFormat.cs with DTO records

**Files:**
- Create: `OpenNest.IO/NestFormat.cs`

These DTOs are the JSON shape — flat records that map 1:1 with the spec's JSON schema. They live in `OpenNest.IO` because they're serialization concerns, not domain model.

- [ ] **Step 1: Create `NestFormat.cs`**

```csharp
using System.Text.Json;

namespace OpenNest.IO
{
    public static class NestFormat
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public record NestDto
        {
            public int Version { get; init; } = 2;
            public string Name { get; init; } = "";
            public string Units { get; init; } = "Inches";
            public string Customer { get; init; } = "";
            public string DateCreated { get; init; } = "";
            public string DateLastModified { get; init; } = "";
            public string Notes { get; init; } = "";
            public PlateDefaultsDto PlateDefaults { get; init; } = new();
            public List<DrawingDto> Drawings { get; init; } = new();
            public List<PlateDto> Plates { get; init; } = new();
        }

        public record PlateDefaultsDto
        {
            public SizeDto Size { get; init; } = new();
            public double Thickness { get; init; }
            public int Quadrant { get; init; } = 1;
            public double PartSpacing { get; init; }
            public MaterialDto Material { get; init; } = new();
            public SpacingDto EdgeSpacing { get; init; } = new();
        }

        public record DrawingDto
        {
            public int Id { get; init; }
            public string Name { get; init; } = "";
            public string Customer { get; init; } = "";
            public ColorDto Color { get; init; } = new();
            public QuantityDto Quantity { get; init; } = new();
            public int Priority { get; init; }
            public ConstraintsDto Constraints { get; init; } = new();
            public MaterialDto Material { get; init; } = new();
            public SourceDto Source { get; init; } = new();
        }

        public record PlateDto
        {
            public int Id { get; init; }
            public SizeDto Size { get; init; } = new();
            public double Thickness { get; init; }
            public int Quadrant { get; init; } = 1;
            public int Quantity { get; init; } = 1;
            public double PartSpacing { get; init; }
            public MaterialDto Material { get; init; } = new();
            public SpacingDto EdgeSpacing { get; init; } = new();
            public List<PartDto> Parts { get; init; } = new();
        }

        public record PartDto
        {
            public int DrawingId { get; init; }
            public double X { get; init; }
            public double Y { get; init; }
            public double Rotation { get; init; }
        }

        public record SizeDto
        {
            public double Width { get; init; }
            public double Height { get; init; }
        }

        public record MaterialDto
        {
            public string Name { get; init; } = "";
            public string Grade { get; init; } = "";
            public double Density { get; init; }
        }

        public record SpacingDto
        {
            public double Left { get; init; }
            public double Top { get; init; }
            public double Right { get; init; }
            public double Bottom { get; init; }
        }

        public record ColorDto
        {
            public int A { get; init; } = 255;
            public int R { get; init; }
            public int G { get; init; }
            public int B { get; init; }
        }

        public record QuantityDto
        {
            public int Required { get; init; }
        }

        public record ConstraintsDto
        {
            public double StepAngle { get; init; }
            public double StartAngle { get; init; }
            public double EndAngle { get; init; }
            public bool Allow180Equivalent { get; init; }
        }

        public record SourceDto
        {
            public string Path { get; init; } = "";
            public OffsetDto Offset { get; init; } = new();
        }

        public record OffsetDto
        {
            public double X { get; init; }
            public double Y { get; init; }
        }
    }
}
```

- [ ] **Step 2: Build to verify DTOs compile**

Run: `dotnet build OpenNest.IO/OpenNest.IO.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add OpenNest.IO/NestFormat.cs
git commit -m "feat: add NestFormat DTOs for JSON nest file format v2"
```

---

## Chunk 2: Rewrite NestWriter

### Task 2: Rewrite NestWriter to use JSON serialization

**Files:**
- Rewrite: `OpenNest.IO/NestWriter.cs`

The writer keeps the same public API: `NestWriter(Nest nest)` constructor and `bool Write(string file)`. Internally it builds a `NestDto` from the domain model, serializes it to `nest.json`, and writes each drawing's program to `programs/program-N`.

The G-code writing methods (`WriteDrawing`, `GetCodeString`, `GetLayerString`) are preserved exactly — they write program G-code to streams, which is unchanged. The `WritePlate` method and all XML methods (`AddNestInfo`, `AddPlateInfo`, `AddDrawingInfo`) are removed.

- [ ] **Step 1: Rewrite `NestWriter.cs`**

Replace the entire file. Key changes:
- Remove `using System.Xml`
- Add `using System.Text.Json`
- Remove `AddNestInfo()`, `AddPlateInfo()`, `AddDrawingInfo()`, `AddPlates()`, `WritePlate()` methods
- Add `BuildNestDto()` method that maps domain model → DTOs
- `Write()` now serializes `NestDto` to `nest.json` and writes programs to `programs/program-N`
- Keep `WriteDrawing()`, `GetCodeString()`, `GetLayerString()` exactly as-is

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using OpenNest.CNC;
using OpenNest.Math;
using static OpenNest.IO.NestFormat;

namespace OpenNest.IO
{
    public sealed class NestWriter
    {
        private const int OutputPrecision = 10;
        private const string CoordinateFormat = "0.##########";

        private readonly Nest nest;
        private Dictionary<int, Drawing> drawingDict;

        public NestWriter(Nest nest)
        {
            this.drawingDict = new Dictionary<int, Drawing>();
            this.nest = nest;
        }

        public bool Write(string file)
        {
            nest.DateLastModified = DateTime.Now;
            SetDrawingIds();

            using var fileStream = new FileStream(file, FileMode.Create);
            using var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            WriteNestJson(zipArchive);
            WritePrograms(zipArchive);

            return true;
        }

        private void SetDrawingIds()
        {
            var id = 1;
            foreach (var drawing in nest.Drawings)
            {
                drawingDict.Add(id, drawing);
                id++;
            }
        }

        private void WriteNestJson(ZipArchive zipArchive)
        {
            var dto = BuildNestDto();
            var json = JsonSerializer.Serialize(dto, JsonOptions);

            var entry = zipArchive.CreateEntry("nest.json");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(json);
        }

        private NestDto BuildNestDto()
        {
            return new NestDto
            {
                Version = 2,
                Name = nest.Name ?? "",
                Units = nest.Units.ToString(),
                Customer = nest.Customer ?? "",
                DateCreated = nest.DateCreated.ToString("o"),
                DateLastModified = nest.DateLastModified.ToString("o"),
                Notes = nest.Notes ?? "",
                PlateDefaults = BuildPlateDefaultsDto(),
                Drawings = BuildDrawingDtos(),
                Plates = BuildPlateDtos()
            };
        }

        private PlateDefaultsDto BuildPlateDefaultsDto()
        {
            var pd = nest.PlateDefaults;
            return new PlateDefaultsDto
            {
                Size = new SizeDto { Width = pd.Size.Width, Height = pd.Size.Height },
                Thickness = pd.Thickness,
                Quadrant = pd.Quadrant,
                PartSpacing = pd.PartSpacing,
                Material = new MaterialDto
                {
                    Name = pd.Material.Name ?? "",
                    Grade = pd.Material.Grade ?? "",
                    Density = pd.Material.Density
                },
                EdgeSpacing = new SpacingDto
                {
                    Left = pd.EdgeSpacing.Left,
                    Top = pd.EdgeSpacing.Top,
                    Right = pd.EdgeSpacing.Right,
                    Bottom = pd.EdgeSpacing.Bottom
                }
            };
        }

        private List<DrawingDto> BuildDrawingDtos()
        {
            var list = new List<DrawingDto>();
            foreach (var kvp in drawingDict.OrderBy(k => k.Key))
            {
                var d = kvp.Value;
                list.Add(new DrawingDto
                {
                    Id = kvp.Key,
                    Name = d.Name ?? "",
                    Customer = d.Customer ?? "",
                    Color = new ColorDto { A = d.Color.A, R = d.Color.R, G = d.Color.G, B = d.Color.B },
                    Quantity = new QuantityDto { Required = d.Quantity.Required },
                    Priority = d.Priority,
                    Constraints = new ConstraintsDto
                    {
                        StepAngle = d.Constraints.StepAngle,
                        StartAngle = d.Constraints.StartAngle,
                        EndAngle = d.Constraints.EndAngle,
                        Allow180Equivalent = d.Constraints.Allow180Equivalent
                    },
                    Material = new MaterialDto
                    {
                        Name = d.Material.Name ?? "",
                        Grade = d.Material.Grade ?? "",
                        Density = d.Material.Density
                    },
                    Source = new SourceDto
                    {
                        Path = d.Source.Path ?? "",
                        Offset = new OffsetDto { X = d.Source.Offset.X, Y = d.Source.Offset.Y }
                    }
                });
            }
            return list;
        }

        private List<PlateDto> BuildPlateDtos()
        {
            var list = new List<PlateDto>();
            for (var i = 0; i < nest.Plates.Count; i++)
            {
                var plate = nest.Plates[i];
                var parts = new List<PartDto>();
                foreach (var part in plate.Parts)
                {
                    var match = drawingDict.Where(dwg => dwg.Value == part.BaseDrawing).FirstOrDefault();
                    parts.Add(new PartDto
                    {
                        DrawingId = match.Key,
                        X = part.Location.X,
                        Y = part.Location.Y,
                        Rotation = part.Rotation
                    });
                }

                list.Add(new PlateDto
                {
                    Id = i + 1,
                    Size = new SizeDto { Width = plate.Size.Width, Height = plate.Size.Height },
                    Thickness = plate.Thickness,
                    Quadrant = plate.Quadrant,
                    Quantity = plate.Quantity,
                    PartSpacing = plate.PartSpacing,
                    Material = new MaterialDto
                    {
                        Name = plate.Material.Name ?? "",
                        Grade = plate.Material.Grade ?? "",
                        Density = plate.Material.Density
                    },
                    EdgeSpacing = new SpacingDto
                    {
                        Left = plate.EdgeSpacing.Left,
                        Top = plate.EdgeSpacing.Top,
                        Right = plate.EdgeSpacing.Right,
                        Bottom = plate.EdgeSpacing.Bottom
                    },
                    Parts = parts
                });
            }
            return list;
        }

        private void WritePrograms(ZipArchive zipArchive)
        {
            foreach (var kvp in drawingDict.OrderBy(k => k.Key))
            {
                var name = $"programs/program-{kvp.Key}";
                var stream = new MemoryStream();
                WriteDrawing(stream, kvp.Value);

                var entry = zipArchive.CreateEntry(name);
                using var entryStream = entry.Open();
                stream.CopyTo(entryStream);
            }
        }

        private void WriteDrawing(Stream stream, Drawing drawing)
        {
            var program = drawing.Program;
            var writer = new StreamWriter(stream);
            writer.AutoFlush = true;

            writer.WriteLine(program.Mode == Mode.Absolute ? "G90" : "G91");

            for (var i = 0; i < drawing.Program.Length; ++i)
            {
                var code = drawing.Program[i];
                writer.WriteLine(GetCodeString(code));
            }

            stream.Position = 0;
        }

        private string GetCodeString(ICode code)
        {
            switch (code.Type)
            {
                case CodeType.ArcMove:
                    {
                        var sb = new StringBuilder();
                        var arcMove = (ArcMove)code;

                        var x = System.Math.Round(arcMove.EndPoint.X, OutputPrecision).ToString(CoordinateFormat);
                        var y = System.Math.Round(arcMove.EndPoint.Y, OutputPrecision).ToString(CoordinateFormat);
                        var i = System.Math.Round(arcMove.CenterPoint.X, OutputPrecision).ToString(CoordinateFormat);
                        var j = System.Math.Round(arcMove.CenterPoint.Y, OutputPrecision).ToString(CoordinateFormat);

                        if (arcMove.Rotation == RotationType.CW)
                            sb.Append(string.Format("G02X{0}Y{1}I{2}J{3}", x, y, i, j));
                        else
                            sb.Append(string.Format("G03X{0}Y{1}I{2}J{3}", x, y, i, j));

                        if (arcMove.Layer != LayerType.Cut)
                            sb.Append(GetLayerString(arcMove.Layer));

                        return sb.ToString();
                    }

                case CodeType.Comment:
                    {
                        var comment = (Comment)code;
                        return ":" + comment.Value;
                    }

                case CodeType.LinearMove:
                    {
                        var sb = new StringBuilder();
                        var linearMove = (LinearMove)code;

                        sb.Append(string.Format("G01X{0}Y{1}",
                            System.Math.Round(linearMove.EndPoint.X, OutputPrecision).ToString(CoordinateFormat),
                            System.Math.Round(linearMove.EndPoint.Y, OutputPrecision).ToString(CoordinateFormat)));

                        if (linearMove.Layer != LayerType.Cut)
                            sb.Append(GetLayerString(linearMove.Layer));

                        return sb.ToString();
                    }

                case CodeType.RapidMove:
                    {
                        var rapidMove = (RapidMove)code;

                        return string.Format("G00X{0}Y{1}",
                            System.Math.Round(rapidMove.EndPoint.X, OutputPrecision).ToString(CoordinateFormat),
                            System.Math.Round(rapidMove.EndPoint.Y, OutputPrecision).ToString(CoordinateFormat));
                    }

                case CodeType.SetFeedrate:
                    {
                        var setFeedrate = (Feedrate)code;
                        return "F" + setFeedrate.Value;
                    }

                case CodeType.SetKerf:
                    {
                        var setKerf = (Kerf)code;

                        switch (setKerf.Value)
                        {
                            case KerfType.None: return "G40";
                            case KerfType.Left: return "G41";
                            case KerfType.Right: return "G42";
                        }

                        break;
                    }

                case CodeType.SubProgramCall:
                    {
                        var subProgramCall = (SubProgramCall)code;
                        break;
                    }
            }

            return string.Empty;
        }

        private string GetLayerString(LayerType layer)
        {
            switch (layer)
            {
                case LayerType.Display:
                    return ":DISPLAY";

                case LayerType.Leadin:
                    return ":LEADIN";

                case LayerType.Leadout:
                    return ":LEADOUT";

                case LayerType.Scribe:
                    return ":SCRIBE";

                default:
                    return string.Empty;
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify NestWriter compiles**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add OpenNest.IO/NestWriter.cs
git commit -m "feat: rewrite NestWriter to use JSON format v2"
```

---

## Chunk 3: Rewrite NestReader

### Task 3: Rewrite NestReader to use JSON deserialization

**Files:**
- Rewrite: `OpenNest.IO/NestReader.cs`

The reader keeps the same public API: `NestReader(string file)`, `NestReader(Stream stream)`, and `Nest Read()`. Internally it reads `nest.json`, deserializes to `NestDto`, reads programs from `programs/program-N`, and assembles the domain model.

All XML parsing, plate G-code parsing, dictionary-linking (`LinkProgramsToDrawings`, `LinkPartsToPlates`), and the helper enums/methods are removed.

- [ ] **Step 1: Rewrite `NestReader.cs`**

Replace the entire file:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using OpenNest.CNC;
using OpenNest.Geometry;
using static OpenNest.IO.NestFormat;

namespace OpenNest.IO
{
    public sealed class NestReader
    {
        private readonly Stream stream;
        private readonly ZipArchive zipArchive;

        public NestReader(string file)
        {
            stream = new FileStream(file, FileMode.Open, FileAccess.Read);
            zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        }

        public NestReader(Stream stream)
        {
            this.stream = stream;
            zipArchive = new ZipArchive(stream, ZipArchiveMode.Read);
        }

        public Nest Read()
        {
            var nestJson = ReadEntry("nest.json");
            var dto = JsonSerializer.Deserialize<NestDto>(nestJson, JsonOptions);

            var programs = ReadPrograms(dto.Drawings.Count);
            var drawingMap = BuildDrawings(dto, programs);
            var nest = BuildNest(dto, drawingMap);

            zipArchive.Dispose();
            stream.Close();

            return nest;
        }

        private string ReadEntry(string name)
        {
            var entry = zipArchive.GetEntry(name)
                ?? throw new InvalidDataException($"Nest file is missing required entry '{name}'.");
            using var entryStream = entry.Open();
            using var reader = new StreamReader(entryStream);
            return reader.ReadToEnd();
        }

        private Dictionary<int, Program> ReadPrograms(int count)
        {
            var programs = new Dictionary<int, Program>();
            for (var i = 1; i <= count; i++)
            {
                var entry = zipArchive.GetEntry($"programs/program-{i}");
                if (entry == null) continue;

                using var entryStream = entry.Open();
                var memStream = new MemoryStream();
                entryStream.CopyTo(memStream);
                memStream.Position = 0;

                var reader = new ProgramReader(memStream);
                programs[i] = reader.Read();
            }
            return programs;
        }

        private Dictionary<int, Drawing> BuildDrawings(NestDto dto, Dictionary<int, Program> programs)
        {
            var map = new Dictionary<int, Drawing>();
            foreach (var d in dto.Drawings)
            {
                var drawing = new Drawing(d.Name);
                drawing.Customer = d.Customer;
                drawing.Color = Color.FromArgb(d.Color.A, d.Color.R, d.Color.G, d.Color.B);
                drawing.Quantity.Required = d.Quantity.Required;
                drawing.Priority = d.Priority;
                drawing.Constraints.StepAngle = d.Constraints.StepAngle;
                drawing.Constraints.StartAngle = d.Constraints.StartAngle;
                drawing.Constraints.EndAngle = d.Constraints.EndAngle;
                drawing.Constraints.Allow180Equivalent = d.Constraints.Allow180Equivalent;
                drawing.Material = new Material(d.Material.Name, d.Material.Grade, d.Material.Density);
                drawing.Source.Path = d.Source.Path;
                drawing.Source.Offset = new Vector(d.Source.Offset.X, d.Source.Offset.Y);

                if (programs.TryGetValue(d.Id, out var pgm))
                    drawing.Program = pgm;

                map[d.Id] = drawing;
            }
            return map;
        }

        private Nest BuildNest(NestDto dto, Dictionary<int, Drawing> drawingMap)
        {
            var nest = new Nest();
            nest.Name = dto.Name;

            Units units;
            if (Enum.TryParse(dto.Units, true, out units))
                nest.Units = units;

            nest.Customer = dto.Customer;
            nest.DateCreated = DateTime.Parse(dto.DateCreated);
            nest.DateLastModified = DateTime.Parse(dto.DateLastModified);
            nest.Notes = dto.Notes;

            // Plate defaults
            var pd = dto.PlateDefaults;
            nest.PlateDefaults.Size = new Size(pd.Size.Width, pd.Size.Height);
            nest.PlateDefaults.Thickness = pd.Thickness;
            nest.PlateDefaults.Quadrant = pd.Quadrant;
            nest.PlateDefaults.PartSpacing = pd.PartSpacing;
            nest.PlateDefaults.Material = new Material(pd.Material.Name, pd.Material.Grade, pd.Material.Density);
            nest.PlateDefaults.EdgeSpacing = new Spacing(pd.EdgeSpacing.Left, pd.EdgeSpacing.Bottom, pd.EdgeSpacing.Right, pd.EdgeSpacing.Top);

            // Drawings
            foreach (var d in drawingMap.OrderBy(k => k.Key))
                nest.Drawings.Add(d.Value);

            // Plates
            foreach (var p in dto.Plates.OrderBy(p => p.Id))
            {
                var plate = new Plate();
                plate.Size = new Size(p.Size.Width, p.Size.Height);
                plate.Thickness = p.Thickness;
                plate.Quadrant = p.Quadrant;
                plate.Quantity = p.Quantity;
                plate.PartSpacing = p.PartSpacing;
                plate.Material = new Material(p.Material.Name, p.Material.Grade, p.Material.Density);
                plate.EdgeSpacing = new Spacing(p.EdgeSpacing.Left, p.EdgeSpacing.Bottom, p.EdgeSpacing.Right, p.EdgeSpacing.Top);

                foreach (var partDto in p.Parts)
                {
                    if (!drawingMap.TryGetValue(partDto.DrawingId, out var dwg))
                        continue;

                    var part = new Part(dwg);
                    part.Rotate(partDto.Rotation);
                    part.Offset(new Vector(partDto.X, partDto.Y));
                    plate.Parts.Add(part);
                }

                nest.Plates.Add(plate);
            }

            return nest;
        }
    }
}
```

- [ ] **Step 2: Build to verify NestReader compiles**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add OpenNest.IO/NestReader.cs
git commit -m "feat: rewrite NestReader to use JSON format v2"
```

---

## Chunk 4: Smoke Test

### Task 4: Manual smoke test via OpenNest.Console

**Files:** None modified — this is a verification step.

Use the `OpenNest.Console` project (or the MCP server) to verify round-trip: create a nest, save it, reload it, confirm data is intact.

- [ ] **Step 1: Build the full solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded with no errors.

- [ ] **Step 2: Round-trip test via MCP tools**

Use the OpenNest MCP tools to:
1. Create a drawing (e.g. a rectangle via `create_drawing`)
2. Create a plate via `create_plate`
3. Fill the plate via `fill_plate`
4. Save the nest via the console app or verify `get_plate_info` shows parts
5. If a nest file exists on disk, load it with `load_nest` and verify `get_plate_info` returns the same data

- [ ] **Step 3: Inspect the ZIP contents**

Unzip a saved nest file and verify:
- `nest.json` exists with correct structure
- `programs/program-1` (etc.) exist with G-code content
- No `info`, `drawing-info`, `plate-info`, or `plate-NNN` files exist

- [ ] **Step 4: Commit any fixes**

If any issues were found and fixed, commit them:

```bash
git add -u
git commit -m "fix: address issues found during nest format v2 smoke test"
```
