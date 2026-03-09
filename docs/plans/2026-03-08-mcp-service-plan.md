# OpenNest MCP Service + IO Library Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Create an MCP server that allows Claude Code to load nest files, run nesting algorithms, and inspect results for rapid iteration on nesting strategies.

**Architecture:** Extract IO classes from the WinForms project into a new `OpenNest.IO` class library, add `Plate.GetRemnants()` to Core, then build an `OpenNest.Mcp` console app that references Core + Engine + IO and exposes nesting operations as MCP tools over stdio.

**Tech Stack:** .NET 8, ModelContextProtocol SDK, Microsoft.Extensions.Hosting, ACadSharp 3.1.32

---

### Task 1: Create the OpenNest.IO class library

**Files:**
- Create: `OpenNest.IO/OpenNest.IO.csproj`
- Move: `OpenNest/IO/DxfImporter.cs` → `OpenNest.IO/DxfImporter.cs`
- Move: `OpenNest/IO/DxfExporter.cs` → `OpenNest.IO/DxfExporter.cs`
- Move: `OpenNest/IO/NestReader.cs` → `OpenNest.IO/NestReader.cs`
- Move: `OpenNest/IO/NestWriter.cs` → `OpenNest.IO/NestWriter.cs`
- Move: `OpenNest/IO/ProgramReader.cs` → `OpenNest.IO/ProgramReader.cs`
- Move: `OpenNest/IO/Extensions.cs` → `OpenNest.IO/Extensions.cs`
- Modify: `OpenNest/OpenNest.csproj` — replace ACadSharp ref with OpenNest.IO project ref
- Modify: `OpenNest.sln` — add OpenNest.IO project

**Step 1: Create the IO project**

```bash
cd C:/Users/AJ/Desktop/Projects/OpenNest
dotnet new classlib -n OpenNest.IO --framework net8.0-windows
```

Delete the auto-generated `Class1.cs`.

**Step 2: Configure the csproj**

`OpenNest.IO/OpenNest.IO.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>OpenNest.IO</RootNamespace>
    <AssemblyName>OpenNest.IO</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenNest.Core\OpenNest.Core.csproj" />
    <PackageReference Include="ACadSharp" Version="3.1.32" />
  </ItemGroup>
</Project>
```

**Step 3: Move files**

Move all 6 files from `OpenNest/IO/` to `OpenNest.IO/`:
- `DxfImporter.cs`
- `DxfExporter.cs`
- `NestReader.cs`
- `NestWriter.cs`
- `ProgramReader.cs`
- `Extensions.cs`

These files already use `namespace OpenNest.IO` so no namespace changes needed.

**Step 4: Update the WinForms csproj**

In `OpenNest/OpenNest.csproj`, replace the ACadSharp PackageReference with a project reference to OpenNest.IO:

Remove:
```xml
<PackageReference Include="ACadSharp" Version="3.1.32" />
```

Add:
```xml
<ProjectReference Include="..\OpenNest.IO\OpenNest.IO.csproj" />
```

**Step 5: Add to solution**

```bash
dotnet sln OpenNest.sln add OpenNest.IO/OpenNest.IO.csproj
```

**Step 6: Build and verify**

```bash
dotnet build OpenNest.sln
```

Expected: clean build, zero errors. The WinForms project's `using OpenNest.IO` statements should resolve via the transitive reference.

**Step 7: Commit**

```bash
git add OpenNest.IO/ OpenNest/OpenNest.csproj OpenNest.sln
git add -u OpenNest/IO/  # stages the deletions
git commit -m "refactor: extract OpenNest.IO class library from WinForms project

Move DxfImporter, DxfExporter, NestReader, NestWriter, ProgramReader,
and Extensions into a new OpenNest.IO class library. The WinForms project
now references OpenNest.IO instead of ACadSharp directly."
```

---

### Task 2: Add Plate.GetRemnants()

**Files:**
- Modify: `OpenNest.Core/Plate.cs`

**Step 1: Read Plate.cs to find the insertion point**

Read the full `Plate.cs` to understand the existing structure and find the right location for the new method (after `HasOverlappingParts`).

**Step 2: Implement GetRemnants**

Add this method to the `Plate` class. The algorithm:
1. Get the work area (plate bounds minus edge spacing).
2. Collect all part bounding boxes, inflated by `PartSpacing`.
3. Find the rightmost part edge — the strip to the right is a remnant.
4. Find the topmost part edge — the strip above is a remnant.
5. Filter out boxes that are too small to be useful (area < 1.0) or overlap existing parts.

```csharp
/// <summary>
/// Finds rectangular remnant (empty) regions on the plate.
/// Returns strips along edges that are clear of parts.
/// </summary>
public List<Box> GetRemnants()
{
    var work = WorkArea();
    var results = new List<Box>();

    if (Parts.Count == 0)
    {
        results.Add(work);
        return results;
    }

    var obstacles = new List<Box>();
    foreach (var part in Parts)
        obstacles.Add(part.BoundingBox.Offset(PartSpacing));

    // Right strip: from the rightmost part edge to the work area right edge
    var maxRight = double.MinValue;
    foreach (var box in obstacles)
    {
        if (box.Right > maxRight)
            maxRight = box.Right;
    }

    if (maxRight < work.Right)
    {
        var strip = new Box(maxRight, work.Bottom, work.Right - maxRight, work.Height);
        if (strip.Area() > 1.0)
            results.Add(strip);
    }

    // Top strip: from the topmost part edge to the work area top edge
    var maxTop = double.MinValue;
    foreach (var box in obstacles)
    {
        if (box.Top > maxTop)
            maxTop = box.Top;
    }

    if (maxTop < work.Top)
    {
        var strip = new Box(work.Left, maxTop, work.Width, work.Top - maxTop);
        if (strip.Area() > 1.0)
            results.Add(strip);
    }

    // Bottom strip: from work area bottom to the lowest part edge
    var minBottom = double.MaxValue;
    foreach (var box in obstacles)
    {
        if (box.Bottom < minBottom)
            minBottom = box.Bottom;
    }

    if (minBottom > work.Bottom)
    {
        var strip = new Box(work.Left, work.Bottom, work.Width, minBottom - work.Bottom);
        if (strip.Area() > 1.0)
            results.Add(strip);
    }

    // Left strip: from work area left to the leftmost part edge
    var minLeft = double.MaxValue;
    foreach (var box in obstacles)
    {
        if (box.Left < minLeft)
            minLeft = box.Left;
    }

    if (minLeft > work.Left)
    {
        var strip = new Box(work.Left, work.Bottom, minLeft - work.Left, work.Height);
        if (strip.Area() > 1.0)
            results.Add(strip);
    }

    return results;
}
```

**Step 3: Build and verify**

```bash
dotnet build OpenNest.sln
```

Expected: clean build.

**Step 4: Commit**

```bash
git add OpenNest.Core/Plate.cs
git commit -m "feat: add Plate.GetRemnants() for finding empty edge strips"
```

---

### Task 3: Create the OpenNest.Mcp project scaffold

**Files:**
- Create: `OpenNest.Mcp/OpenNest.Mcp.csproj`
- Create: `OpenNest.Mcp/Program.cs`
- Create: `OpenNest.Mcp/NestSession.cs`
- Modify: `OpenNest.sln`

**Step 1: Create the console project**

```bash
cd C:/Users/AJ/Desktop/Projects/OpenNest
dotnet new console -n OpenNest.Mcp --framework net8.0-windows
```

**Step 2: Configure the csproj**

`OpenNest.Mcp/OpenNest.Mcp.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>OpenNest.Mcp</RootNamespace>
    <AssemblyName>OpenNest.Mcp</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenNest.Core\OpenNest.Core.csproj" />
    <ProjectReference Include="..\OpenNest.Engine\OpenNest.Engine.csproj" />
    <ProjectReference Include="..\OpenNest.IO\OpenNest.IO.csproj" />
    <PackageReference Include="ModelContextProtocol" Version="0.*-*" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.*" />
  </ItemGroup>
</Project>
```

**Step 3: Create NestSession.cs**

This holds the in-memory state across tool calls — the current `Nest` object, a list of standalone plates and drawings for synthetic tests.

```csharp
using System.Collections.Generic;

namespace OpenNest.Mcp
{
    public class NestSession
    {
        public Nest Nest { get; set; }
        public List<Plate> Plates { get; } = new();
        public List<Drawing> Drawings { get; } = new();

        public Plate GetPlate(int index)
        {
            if (Nest != null && index < Nest.Plates.Count)
                return Nest.Plates[index];

            var adjustedIndex = index - (Nest?.Plates.Count ?? 0);
            if (adjustedIndex >= 0 && adjustedIndex < Plates.Count)
                return Plates[adjustedIndex];

            return null;
        }

        public Drawing GetDrawing(string name)
        {
            if (Nest != null)
            {
                foreach (var d in Nest.Drawings)
                {
                    if (d.Name == name)
                        return d;
                }
            }

            foreach (var d in Drawings)
            {
                if (d.Name == name)
                    return d;
            }

            return null;
        }

        public List<Plate> AllPlates()
        {
            var all = new List<Plate>();
            if (Nest != null)
                all.AddRange(Nest.Plates);
            all.AddRange(Plates);
            return all;
        }

        public List<Drawing> AllDrawings()
        {
            var all = new List<Drawing>();
            if (Nest != null)
                all.AddRange(Nest.Drawings);
            all.AddRange(Drawings);
            return all;
        }
    }
}
```

**Step 4: Create Program.cs**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using OpenNest.Mcp;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<NestSession>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(Program).Assembly);

var app = builder.Build();
await app.RunAsync();
```

**Step 5: Add to solution and build**

```bash
dotnet sln OpenNest.sln add OpenNest.Mcp/OpenNest.Mcp.csproj
dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj
```

**Step 6: Commit**

```bash
git add OpenNest.Mcp/ OpenNest.sln
git commit -m "feat: scaffold OpenNest.Mcp project with session state"
```

---

### Task 4: Implement input tools (load_nest, import_dxf, create_drawing)

**Files:**
- Create: `OpenNest.Mcp/Tools/InputTools.cs`

**Step 1: Create the tools file**

`OpenNest.Mcp/Tools/InputTools.cs`:

```csharp
using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using OpenNest.CNC;
using OpenNest.Converters;
using OpenNest.Geometry;
using OpenNest.IO;

namespace OpenNest.Mcp.Tools
{
    public class InputTools
    {
        private readonly NestSession _session;

        public InputTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "load_nest")]
        [Description("Load a .nest zip file. Returns a summary of plates, drawings, and part counts.")]
        public string LoadNest(
            [Description("Full path to the .nest zip file")] string path)
        {
            var nest = NestReader.Read(path);
            _session.Nest = nest;

            var sb = new StringBuilder();
            sb.AppendLine($"Loaded: {nest.Name}");
            sb.AppendLine($"Plates: {nest.Plates.Count}");
            sb.AppendLine($"Drawings: {nest.Drawings.Count}");

            for (var i = 0; i < nest.Plates.Count; i++)
            {
                var plate = nest.Plates[i];
                sb.AppendLine($"  Plate {i}: {plate.Size.Width}x{plate.Size.Height}, " +
                    $"{plate.Parts.Count} parts, " +
                    $"utilization: {plate.Utilization():P1}");
            }

            for (var i = 0; i < nest.Drawings.Count; i++)
            {
                var dwg = nest.Drawings[i];
                var bbox = dwg.Program.BoundingBox();
                sb.AppendLine($"  Drawing: \"{dwg.Name}\" ({bbox.Width:F4}x{bbox.Height:F4})");
            }

            return sb.ToString();
        }

        [McpServerTool(Name = "import_dxf")]
        [Description("Import a DXF file as a drawing.")]
        public string ImportDxf(
            [Description("Full path to the DXF file")] string path,
            [Description("Name for the drawing (defaults to filename)")] string name = null)
        {
            var importer = new DxfImporter();
            var geometry = importer.Import(path);

            if (geometry == null || geometry.Count == 0)
                return "Error: No geometry found in DXF file.";

            var pgm = ConvertGeometry.ToProgram(geometry);
            if (pgm == null)
                return "Error: Could not convert DXF geometry to program.";

            var drawingName = name ?? System.IO.Path.GetFileNameWithoutExtension(path);
            var drawing = new Drawing(drawingName) { Program = pgm };
            drawing.UpdateArea();
            _session.Drawings.Add(drawing);

            var bbox = pgm.BoundingBox();
            return $"Imported \"{drawingName}\": {bbox.Width:F4}x{bbox.Height:F4}, area: {drawing.Area:F4}";
        }

        [McpServerTool(Name = "create_drawing")]
        [Description("Create a drawing from a built-in shape (rectangle, circle, l_shape, t_shape) or raw G-code.")]
        public string CreateDrawing(
            [Description("Name for the drawing")] string name,
            [Description("Shape type: rectangle, circle, l_shape, t_shape, gcode")] string shape,
            [Description("Width (for rectangle, l_shape, t_shape)")] double width = 0,
            [Description("Height (for rectangle, l_shape, t_shape)")] double height = 0,
            [Description("Radius (for circle)")] double radius = 0,
            [Description("Secondary width (for l_shape: notch width, t_shape: stem width)")] double width2 = 0,
            [Description("Secondary height (for l_shape: notch height, t_shape: stem height)")] double height2 = 0,
            [Description("Raw G-code string (for gcode shape)")] string gcode = null)
        {
            Program pgm;

            switch (shape.ToLowerInvariant())
            {
                case "rectangle":
                    pgm = BuildRectangle(width, height);
                    break;
                case "circle":
                    pgm = BuildCircle(radius);
                    break;
                case "l_shape":
                    pgm = BuildLShape(width, height, width2, height2);
                    break;
                case "t_shape":
                    pgm = BuildTShape(width, height, width2, height2);
                    break;
                case "gcode":
                    if (string.IsNullOrEmpty(gcode))
                        return "Error: gcode parameter required for gcode shape.";
                    pgm = ProgramReader.Parse(gcode);
                    break;
                default:
                    return $"Error: Unknown shape '{shape}'. Use: rectangle, circle, l_shape, t_shape, gcode.";
            }

            var drawing = new Drawing(name) { Program = pgm };
            drawing.UpdateArea();
            _session.Drawings.Add(drawing);

            var bbox = pgm.BoundingBox();
            return $"Created \"{name}\": {bbox.Width:F4}x{bbox.Height:F4}, area: {drawing.Area:F4}";
        }

        private static Program BuildRectangle(double w, double h)
        {
            var shape = new Shape();
            shape.Entities.Add(new Line(0, 0, w, 0));
            shape.Entities.Add(new Line(w, 0, w, h));
            shape.Entities.Add(new Line(w, h, 0, h));
            shape.Entities.Add(new Line(0, h, 0, 0));
            return ConvertGeometry.ToProgram(shape);
        }

        private static Program BuildCircle(double r)
        {
            var shape = new Shape();
            shape.Entities.Add(new Circle(0, 0, r));
            return ConvertGeometry.ToProgram(shape);
        }

        private static Program BuildLShape(double w, double h, double w2, double h2)
        {
            // L-shape: full rectangle minus top-right notch
            var shape = new Shape();
            shape.Entities.Add(new Line(0, 0, w, 0));
            shape.Entities.Add(new Line(w, 0, w, h - h2));
            shape.Entities.Add(new Line(w, h - h2, w - w2, h - h2));
            shape.Entities.Add(new Line(w - w2, h - h2, w - w2, h));
            shape.Entities.Add(new Line(w - w2, h, 0, h));
            shape.Entities.Add(new Line(0, h, 0, 0));
            return ConvertGeometry.ToProgram(shape);
        }

        private static Program BuildTShape(double w, double h, double stemW, double stemH)
        {
            // T-shape: wide top + centered stem
            var stemLeft = (w - stemW) / 2.0;
            var stemRight = stemLeft + stemW;
            var shape = new Shape();
            shape.Entities.Add(new Line(stemLeft, 0, stemRight, 0));
            shape.Entities.Add(new Line(stemRight, 0, stemRight, stemH));
            shape.Entities.Add(new Line(stemRight, stemH, w, stemH));
            shape.Entities.Add(new Line(w, stemH, w, h));
            shape.Entities.Add(new Line(w, h, 0, h));
            shape.Entities.Add(new Line(0, h, 0, stemH));
            shape.Entities.Add(new Line(0, stemH, stemLeft, stemH));
            shape.Entities.Add(new Line(stemLeft, stemH, stemLeft, 0));
            return ConvertGeometry.ToProgram(shape);
        }
    }
}
```

Note: `DxfImporter.Import()` may have a different signature — check the actual method. It might return `List<Entity>` or take different parameters. Also check if `ProgramReader.Parse(string)` exists or if it reads from files. Adapt as needed.

**Step 2: Build and verify**

```bash
dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj
```

Fix any compilation issues from API mismatches (DxfImporter signature, ProgramReader usage).

**Step 3: Commit**

```bash
git add OpenNest.Mcp/Tools/InputTools.cs
git commit -m "feat(mcp): add input tools — load_nest, import_dxf, create_drawing"
```

---

### Task 5: Implement setup tools (create_plate, clear_plate)

**Files:**
- Create: `OpenNest.Mcp/Tools/SetupTools.cs`

**Step 1: Create the tools file**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using OpenNest.Geometry;

namespace OpenNest.Mcp.Tools
{
    public class SetupTools
    {
        private readonly NestSession _session;

        public SetupTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "create_plate")]
        [Description("Create a new plate with specified dimensions and spacing.")]
        public string CreatePlate(
            [Description("Plate width")] double width,
            [Description("Plate height")] double height,
            [Description("Spacing between parts (default 0.125)")] double partSpacing = 0.125,
            [Description("Edge spacing on all sides (default 0.25)")] double edgeSpacing = 0.25,
            [Description("Quadrant 1-4 (default 3 = bottom-left origin)")] int quadrant = 3)
        {
            var plate = new Plate(width, height)
            {
                PartSpacing = partSpacing,
                Quadrant = quadrant,
                EdgeSpacing = new Spacing(edgeSpacing)
            };

            _session.Plates.Add(plate);
            var index = _session.AllPlates().Count - 1;
            var work = plate.WorkArea();

            return $"Created plate {index}: {width}x{height}, " +
                $"work area: {work.Width:F4}x{work.Height:F4}, " +
                $"quadrant: {quadrant}, part spacing: {partSpacing}, edge spacing: {edgeSpacing}";
        }

        [McpServerTool(Name = "clear_plate")]
        [Description("Remove all parts from a plate.")]
        public string ClearPlate(
            [Description("Plate index (0-based)")] int plate)
        {
            var p = _session.GetPlate(plate);
            if (p == null)
                return $"Error: Plate {plate} not found.";

            var count = p.Parts.Count;
            p.Parts.Clear();

            return $"Cleared plate {plate}: removed {count} parts.";
        }
    }
}
```

Note: Check that `Spacing` has a constructor that takes a single value for all sides. If not, set `Top`, `Bottom`, `Left`, `Right` individually.

**Step 2: Build and verify**

```bash
dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj
```

**Step 3: Commit**

```bash
git add OpenNest.Mcp/Tools/SetupTools.cs
git commit -m "feat(mcp): add setup tools — create_plate, clear_plate"
```

---

### Task 6: Implement nesting tools (fill_plate, fill_area, fill_remnants, pack_plate)

**Files:**
- Create: `OpenNest.Mcp/Tools/NestingTools.cs`

**Step 1: Create the tools file**

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using OpenNest.Geometry;

namespace OpenNest.Mcp.Tools
{
    public class NestingTools
    {
        private readonly NestSession _session;

        public NestingTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "fill_plate")]
        [Description("Fill an entire plate with a single drawing using NestEngine.Fill.")]
        public string FillPlate(
            [Description("Plate index (0-based)")] int plate,
            [Description("Drawing name")] string drawing,
            [Description("Max quantity (0 = unlimited)")] int quantity = 0)
        {
            var p = _session.GetPlate(plate);
            if (p == null) return $"Error: Plate {plate} not found.";

            var d = _session.GetDrawing(drawing);
            if (d == null) return $"Error: Drawing '{drawing}' not found.";

            var before = p.Parts.Count;
            var engine = new NestEngine(p);
            var item = new NestItem { Drawing = d, Quantity = quantity };
            var success = engine.Fill(item);

            var after = p.Parts.Count;
            return $"Fill plate {plate}: added {after - before} parts " +
                $"(total: {after}), utilization: {p.Utilization():P1}";
        }

        [McpServerTool(Name = "fill_area")]
        [Description("Fill a specific rectangular area on a plate with a drawing.")]
        public string FillArea(
            [Description("Plate index (0-based)")] int plate,
            [Description("Drawing name")] string drawing,
            [Description("Area left X")] double x,
            [Description("Area bottom Y")] double y,
            [Description("Area width")] double width,
            [Description("Area height")] double height,
            [Description("Max quantity (0 = unlimited)")] int quantity = 0)
        {
            var p = _session.GetPlate(plate);
            if (p == null) return $"Error: Plate {plate} not found.";

            var d = _session.GetDrawing(drawing);
            if (d == null) return $"Error: Drawing '{drawing}' not found.";

            var before = p.Parts.Count;
            var engine = new NestEngine(p);
            var area = new Box(x, y, width, height);
            var item = new NestItem { Drawing = d, Quantity = quantity };
            var success = engine.Fill(item, area);

            var after = p.Parts.Count;
            return $"Fill area: added {after - before} parts " +
                $"(total: {after}), utilization: {p.Utilization():P1}";
        }

        [McpServerTool(Name = "fill_remnants")]
        [Description("Auto-detect empty remnant strips on a plate and fill each with a drawing.")]
        public string FillRemnants(
            [Description("Plate index (0-based)")] int plate,
            [Description("Drawing name")] string drawing,
            [Description("Max quantity per remnant (0 = unlimited)")] int quantity = 0)
        {
            var p = _session.GetPlate(plate);
            if (p == null) return $"Error: Plate {plate} not found.";

            var d = _session.GetDrawing(drawing);
            if (d == null) return $"Error: Drawing '{drawing}' not found.";

            var remnants = p.GetRemnants();
            if (remnants.Count == 0)
                return "No remnants found on the plate.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {remnants.Count} remnant(s):");

            var totalAdded = 0;
            var before = p.Parts.Count;

            foreach (var remnant in remnants)
            {
                var partsBefore = p.Parts.Count;
                var engine = new NestEngine(p);
                var item = new NestItem { Drawing = d, Quantity = quantity };
                engine.Fill(item, remnant);
                var added = p.Parts.Count - partsBefore;
                totalAdded += added;

                sb.AppendLine($"  Remnant ({remnant.X:F2},{remnant.Y:F2}) " +
                    $"{remnant.Width:F2}x{remnant.Height:F2}: +{added} parts");
            }

            sb.AppendLine($"Total: +{totalAdded} parts ({p.Parts.Count} total), " +
                $"utilization: {p.Utilization():P1}");

            return sb.ToString();
        }

        [McpServerTool(Name = "pack_plate")]
        [Description("Pack multiple drawings onto a plate using bin-packing (PackBottomLeft).")]
        public string PackPlate(
            [Description("Plate index (0-based)")] int plate,
            [Description("Comma-separated list of drawing names")] string drawings,
            [Description("Comma-separated quantities for each drawing (default: 1 each)")] string quantities = null)
        {
            var p = _session.GetPlate(plate);
            if (p == null) return $"Error: Plate {plate} not found.";

            var names = drawings.Split(',');
            var qtys = quantities?.Split(',');
            var items = new List<NestItem>();

            for (var i = 0; i < names.Length; i++)
            {
                var d = _session.GetDrawing(names[i].Trim());
                if (d == null) return $"Error: Drawing '{names[i].Trim()}' not found.";

                var qty = 1;
                if (qtys != null && i < qtys.Length)
                    int.TryParse(qtys[i].Trim(), out qty);

                items.Add(new NestItem { Drawing = d, Quantity = qty });
            }

            var before = p.Parts.Count;
            var engine = new NestEngine(p);
            engine.Pack(items);

            var after = p.Parts.Count;
            return $"Pack plate {plate}: added {after - before} parts " +
                $"(total: {after}), utilization: {p.Utilization():P1}";
        }
    }
}
```

**Step 2: Build and verify**

```bash
dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj
```

**Step 3: Commit**

```bash
git add OpenNest.Mcp/Tools/NestingTools.cs
git commit -m "feat(mcp): add nesting tools — fill_plate, fill_area, fill_remnants, pack_plate"
```

---

### Task 7: Implement inspection tools (get_plate_info, get_parts, check_overlaps)

**Files:**
- Create: `OpenNest.Mcp/Tools/InspectionTools.cs`

**Step 1: Create the tools file**

```csharp
using System.ComponentModel;
using System.Linq;
using System.Text;
using ModelContextProtocol.Server;
using OpenNest.Geometry;

namespace OpenNest.Mcp.Tools
{
    public class InspectionTools
    {
        private readonly NestSession _session;

        public InspectionTools(NestSession session)
        {
            _session = session;
        }

        [McpServerTool(Name = "get_plate_info")]
        [Description("Get plate dimensions, part count, utilization, and remnant areas.")]
        public string GetPlateInfo(
            [Description("Plate index (0-based)")] int plate)
        {
            var p = _session.GetPlate(plate);
            if (p == null) return $"Error: Plate {plate} not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Plate {plate}:");
            sb.AppendLine($"  Size: {p.Size.Width}x{p.Size.Height}");
            sb.AppendLine($"  Quadrant: {p.Quadrant}");
            sb.AppendLine($"  Part spacing: {p.PartSpacing}");
            sb.AppendLine($"  Edge spacing: T={p.EdgeSpacing.Top} B={p.EdgeSpacing.Bottom} " +
                $"L={p.EdgeSpacing.Left} R={p.EdgeSpacing.Right}");

            var work = p.WorkArea();
            sb.AppendLine($"  Work area: ({work.X:F4},{work.Y:F4}) {work.Width:F4}x{work.Height:F4}");
            sb.AppendLine($"  Parts: {p.Parts.Count}");
            sb.AppendLine($"  Area: {p.Area():F4}");
            sb.AppendLine($"  Utilization: {p.Utilization():P2}");

            if (p.Material != null)
                sb.AppendLine($"  Material: {p.Material.Name}");

            var remnants = p.GetRemnants();
            sb.AppendLine($"  Remnants: {remnants.Count}");
            foreach (var r in remnants)
            {
                sb.AppendLine($"    ({r.X:F2},{r.Y:F2}) {r.Width:F2}x{r.Height:F2} " +
                    $"(area: {r.Area():F2})");
            }

            // List unique drawings and their counts
            var drawingCounts = p.Parts
                .GroupBy(part => part.BaseDrawing.Name)
                .Select(g => new { Name = g.Key, Count = g.Count() });

            sb.AppendLine($"  Drawing breakdown:");
            foreach (var dc in drawingCounts)
                sb.AppendLine($"    \"{dc.Name}\": {dc.Count}");

            return sb.ToString();
        }

        [McpServerTool(Name = "get_parts")]
        [Description("List all placed parts on a plate with location, rotation, and bounding box.")]
        public string GetParts(
            [Description("Plate index (0-based)")] int plate,
            [Description("Max parts to return (default 50)")] int limit = 50)
        {
            var p = _session.GetPlate(plate);
            if (p == null) return $"Error: Plate {plate} not found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Plate {plate}: {p.Parts.Count} parts (showing up to {limit})");

            var count = 0;
            foreach (var part in p.Parts)
            {
                if (count >= limit) break;

                var bbox = part.BoundingBox;
                sb.AppendLine($"  [{count}] \"{part.BaseDrawing.Name}\" " +
                    $"loc:({part.Location.X:F4},{part.Location.Y:F4}) " +
                    $"rot:{OpenNest.Math.Angle.ToDegrees(part.Rotation):F1}° " +
                    $"bbox:({bbox.X:F4},{bbox.Y:F4} {bbox.Width:F4}x{bbox.Height:F4})");

                count++;
            }

            if (p.Parts.Count > limit)
                sb.AppendLine($"  ... and {p.Parts.Count - limit} more");

            return sb.ToString();
        }

        [McpServerTool(Name = "check_overlaps")]
        [Description("Run overlap detection on a plate and report any collisions.")]
        public string CheckOverlaps(
            [Description("Plate index (0-based)")] int plate)
        {
            var p = _session.GetPlate(plate);
            if (p == null) return $"Error: Plate {plate} not found.";

            System.Collections.Generic.List<Vector> pts;
            var hasOverlaps = p.HasOverlappingParts(out pts);

            if (!hasOverlaps)
                return $"Plate {plate}: No overlaps detected ({p.Parts.Count} parts).";

            var sb = new StringBuilder();
            sb.AppendLine($"Plate {plate}: OVERLAPS DETECTED — {pts.Count} intersection point(s)");

            var limit = System.Math.Min(pts.Count, 20);
            for (var i = 0; i < limit; i++)
                sb.AppendLine($"  Intersection at ({pts[i].X:F4},{pts[i].Y:F4})");

            if (pts.Count > limit)
                sb.AppendLine($"  ... and {pts.Count - limit} more");

            return sb.ToString();
        }
    }
}
```

**Step 2: Build and verify**

```bash
dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj
```

**Step 3: Commit**

```bash
git add OpenNest.Mcp/Tools/InspectionTools.cs
git commit -m "feat(mcp): add inspection tools — get_plate_info, get_parts, check_overlaps"
```

---

### Task 8: Publish and register the MCP server

**Step 1: Build the full solution**

```bash
dotnet build OpenNest.sln
```

Verify zero errors across all projects.

**Step 2: Publish the MCP server**

```bash
dotnet publish OpenNest.Mcp/OpenNest.Mcp.csproj -c Release -o "$USERPROFILE/.claude/mcp/OpenNest.Mcp"
```

**Step 3: Register with Claude Code**

Create or update the project-level `.mcp.json` in the repo root:

```json
{
  "mcpServers": {
    "opennest": {
      "command": "C:/Users/AJ/.claude/mcp/OpenNest.Mcp/OpenNest.Mcp.exe",
      "args": []
    }
  }
}
```

Alternatively, register at user level:

```bash
claude mcp add --transport stdio --scope user opennest -- "C:/Users/AJ/.claude/mcp/OpenNest.Mcp/OpenNest.Mcp.exe"
```

**Step 4: Commit**

```bash
git add .mcp.json OpenNest.Mcp/
git commit -m "feat(mcp): publish OpenNest.Mcp and register in .mcp.json"
```

---

### Task 9: Smoke test with N0308-008.zip

After restarting Claude Code, verify the MCP tools work end-to-end:

1. `load_nest` with `C:/Users/AJ/Desktop/N0308-008.zip`
2. `get_plate_info` for plate 0 — verify 75 parts, 36x36 plate
3. `get_parts` — verify part locations look reasonable
4. `fill_remnants` — fill empty strips with the existing drawing
5. `check_overlaps` — verify no collisions
6. `get_plate_info` again — verify increased utilization

This is a manual verification step. Fix any runtime issues discovered.

**Commit any fixes:**

```bash
git add -u
git commit -m "fix(mcp): address issues found during smoke testing"
```

---

### Task 10: Update CLAUDE.md and memory

**Files:**
- Modify: `CLAUDE.md` — update architecture section to include OpenNest.IO and OpenNest.Mcp

**Step 1: Update project description in CLAUDE.md**

Add OpenNest.IO and OpenNest.Mcp to the architecture section. Update the dependency description.

**Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: update CLAUDE.md with OpenNest.IO and OpenNest.Mcp projects"
```
