# .NET 8 Migration Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Convert OpenNest from .NET Framework 4.8 to .NET 8, replacing netDxf with ACadSharp.

**Architecture:** Three projects (OpenNest.Core, OpenNest.Engine, OpenNest) converted from legacy csproj to SDK-style targeting `net8.0-windows`. The netDxf NuGet dependency is replaced with ACadSharp 3.1.32 in the WinForms project. The 3 IO files (DxfImporter, DxfExporter, Extensions) are rewritten against the ACadSharp API.

**Tech Stack:** .NET 8, WinForms, ACadSharp 3.1.32, System.Drawing.Common, CSMath

---

## Reference Material

- ACadSharp usage examples: `C:\Users\aisaacs\Desktop\Projects\Geometry\Geometry.Dxf\Extensions.cs` and `DxfImport.cs`
- ACadSharp key types:
  - Reading: `ACadSharp.IO.DxfReader` → `.Read()` returns `CadDocument`
  - Writing: `ACadSharp.IO.DxfWriter`
  - Entities: `ACadSharp.Entities.{Line, Arc, Circle, Polyline, LwPolyline, Spline, Ellipse}`
  - Vectors: `CSMath.XY`, `CSMath.XYZ`
  - Layers: `ACadSharp.Tables.Layer`
  - Colors: `ACadSharp.Color` (ACI index-based)
  - Document: `ACadSharp.CadDocument`
- netDxf → ACadSharp type mapping:
  - `netDxf.Vector2` → `CSMath.XY`
  - `netDxf.Vector3` → `CSMath.XYZ`
  - `netDxf.DxfDocument` → `ACadSharp.CadDocument`
  - `netDxf.DxfDocument.Load(path)` → `new DxfReader(path).Read()`
  - `netDxf.DxfDocument.Save(stream)` → `new DxfWriter(stream, doc).Write()`
  - `netDxf.Tables.Layer` → `ACadSharp.Tables.Layer`
  - `netDxf.AciColor` → `ACadSharp.Color`
  - `netDxf.Linetype` → `ACadSharp.Tables.LineType`
  - `doc.Entities.Lines` → `doc.Entities.OfType<ACadSharp.Entities.Line>()`
  - `doc.Entities.Arcs` → `doc.Entities.OfType<ACadSharp.Entities.Arc>()`
  - `spline.PolygonalVertexes(n)` → manual control point interpolation
  - `polyline.Vertexes` → `polyline.Vertices`
  - `ellipse.ToPolyline2D(n)` → manual parametric calculation

---

### Task 1: Convert OpenNest.Core to SDK-style .NET 8

**Files:**
- Replace: `OpenNest.Core/OpenNest.Core.csproj`
- Delete: `OpenNest.Core/Properties/AssemblyInfo.cs`

**Step 1: Replace the csproj with SDK-style**

Replace `OpenNest.Core/OpenNest.Core.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>OpenNest</RootNamespace>
    <AssemblyName>OpenNest.Core</AssemblyName>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="8.0.10" />
  </ItemGroup>
</Project>
```

Notes:
- `net8.0-windows` needed because `System.Drawing.Common` requires Windows on .NET 8
- `GenerateAssemblyInfo` false to keep existing AssemblyInfo.cs (we'll remove it in step 2 and re-enable)
- SDK-style auto-includes all `.cs` files, so no `<Compile>` items needed
- `System.Drawing.Common` NuGet replaces the Framework's built-in `System.Drawing`

**Step 2: Delete AssemblyInfo.cs and enable auto-generated assembly info**

Delete `OpenNest.Core/Properties/AssemblyInfo.cs`.

Then update the csproj to remove `GenerateAssemblyInfo`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>OpenNest</RootNamespace>
    <AssemblyName>OpenNest.Core</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="8.0.10" />
  </ItemGroup>
</Project>
```

**Step 3: Build to verify**

Run: `dotnet build OpenNest.Core/OpenNest.Core.csproj`
Expected: Build succeeds with 0 errors.

**Step 4: Commit**

```
feat: convert OpenNest.Core to .NET 8 SDK-style project
```

---

### Task 2: Convert OpenNest.Engine to SDK-style .NET 8

**Files:**
- Replace: `OpenNest.Engine/OpenNest.Engine.csproj`
- Delete: `OpenNest.Engine/Properties/AssemblyInfo.cs`

**Step 1: Replace the csproj with SDK-style**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>OpenNest</RootNamespace>
    <AssemblyName>OpenNest.Engine</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenNest.Core\OpenNest.Core.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Delete AssemblyInfo.cs**

Delete `OpenNest.Engine/Properties/AssemblyInfo.cs`.

**Step 3: Build to verify**

Run: `dotnet build OpenNest.Engine/OpenNest.Engine.csproj`
Expected: Build succeeds.

**Step 4: Commit**

```
feat: convert OpenNest.Engine to .NET 8 SDK-style project
```

---

### Task 3: Convert OpenNest (WinForms) to SDK-style .NET 8

**Files:**
- Replace: `OpenNest/OpenNest.csproj`
- Delete: `OpenNest/Properties/AssemblyInfo.cs`
- Delete: `OpenNest/packages.config`
- Keep: `OpenNest/Properties/Settings.settings`, `Settings.Designer.cs`, `Resources.resx`, `Resources.Designer.cs`
- Keep: `OpenNest/app.config`

**Step 1: Replace the csproj with SDK-style**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <RootNamespace>OpenNest</RootNamespace>
    <AssemblyName>OpenNest</AssemblyName>
    <UseWindowsForms>true</UseWindowsForms>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <ApplicationHighDpiMode>DpiUnaware</ApplicationHighDpiMode>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\OpenNest.Core\OpenNest.Core.csproj" />
    <ProjectReference Include="..\OpenNest.Engine\OpenNest.Engine.csproj" />
    <PackageReference Include="ACadSharp" Version="3.1.32" />
    <PackageReference Include="System.Drawing.Common" Version="8.0.10" />
  </ItemGroup>
  <ItemGroup>
    <Compile Update="Properties\Resources.Designer.cs">
      <AutoGen>True</AutoGen>
      <DependentUpon>Resources.resx</DependentUpon>
      <DesignTime>True</DesignTime>
    </Compile>
    <Compile Update="Properties\Settings.Designer.cs">
      <AutoGen>True</AutoGen>
      <DependentUpon>Settings.settings</DependentUpon>
      <DesignTimeSharedInput>True</DesignTimeSharedInput>
    </Compile>
  </ItemGroup>
  <ItemGroup>
    <EmbeddedResource Update="Properties\Resources.resx">
      <Generator>ResXFileCodeGenerator</Generator>
      <LastGenOutput>Resources.Designer.cs</LastGenOutput>
    </EmbeddedResource>
  </ItemGroup>
  <ItemGroup>
    <None Update="Properties\Settings.settings">
      <Generator>SettingsSingleFileGenerator</Generator>
      <LastGenOutput>Settings.Designer.cs</LastGenOutput>
    </None>
  </ItemGroup>
</Project>
```

Notes:
- `UseWindowsForms` enables WinForms support on .NET 8
- `ApplicationHighDpiMode` replaces the manual `SetProcessDPIAware()` P/Invoke
- ACadSharp replaces netDxf package reference
- `GenerateAssemblyInfo` false initially — we'll clean up AssemblyInfo and re-enable after

**Step 2: Delete AssemblyInfo.cs and packages.config**

Delete `OpenNest/Properties/AssemblyInfo.cs` and `OpenNest/packages.config`.

Then remove `GenerateAssemblyInfo` from the csproj.

**Step 3: Build to check what breaks**

Run: `dotnet build OpenNest/OpenNest.csproj`
Expected: Build fails — the 3 IO files reference `netDxf` types. This is expected and will be fixed in Tasks 4-6.

**Step 4: Commit (with build errors expected)**

```
feat: convert OpenNest WinForms project to .NET 8 SDK-style

Build errors expected in IO files — netDxf references not yet migrated to ACadSharp.
```

---

### Task 4: Rewrite Extensions.cs for ACadSharp

**Files:**
- Modify: `OpenNest/IO/Extensions.cs`
- Reference: `C:\Users\aisaacs\Desktop\Projects\Geometry\Geometry.Dxf\Extensions.cs`

**Step 1: Rewrite Extensions.cs**

Replace the full file. Key changes:
- `netDxf.Vector2` → `CSMath.XY`
- `netDxf.Vector3` → `CSMath.XYZ`
- `netDxf.Entities.*` → `ACadSharp.Entities.*`
- `netDxf.Tables.Layer` → `ACadSharp.Tables.Layer`
- `layer.Color.ToColor()` → `System.Drawing.Color.FromArgb(layer.Color.GetColorIndex()...)` or equivalent
- `spline.PolygonalVertexes(n)` → manual control point iteration
- `polyline.Vertexes` → `polyline.Vertices`
- `ellipse.ToPolyline2D(n)` → manual parametric calculation
- Polyline2D closed check: use `Flags` property
- `arc.StartAngle`/`arc.EndAngle` — ACadSharp stores arc angles in **radians** already (netDxf used degrees)

```csharp
using System.Collections.Generic;
using System.Linq;
using ACadSharp.Entities;
using CSMath;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.IO
{
    internal static class Extensions
    {
        public static Vector ToOpenNest(this XY v)
        {
            return new Vector(v.X, v.Y);
        }

        public static Vector ToOpenNest(this XYZ v)
        {
            return new Vector(v.X, v.Y);
        }

        public static Arc ToOpenNest(this ACadSharp.Entities.Arc arc)
        {
            // ACadSharp stores angles in radians already
            return new Arc(
                arc.Center.X, arc.Center.Y, arc.Radius,
                arc.StartAngle,
                arc.EndAngle)
            {
                Layer = arc.Layer.ToOpenNest()
            };
        }

        public static Circle ToOpenNest(this ACadSharp.Entities.Circle circle)
        {
            return new Circle(
                circle.Center.X, circle.Center.Y,
                circle.Radius)
            {
                Layer = circle.Layer.ToOpenNest()
            };
        }

        public static Line ToOpenNest(this ACadSharp.Entities.Line line)
        {
            return new Line(
                line.StartPoint.X, line.StartPoint.Y,
                line.EndPoint.X, line.EndPoint.Y)
            {
                Layer = line.Layer.ToOpenNest()
            };
        }

        public static List<Line> ToOpenNest(this Spline spline, int precision = 200)
        {
            var lines = new List<Line>();
            var controlPoints = spline.ControlPoints
                .Select(p => new Vector(p.X, p.Y)).ToList();

            if (controlPoints.Count < 2)
                return lines;

            var lastPoint = controlPoints[0];

            for (int i = 1; i < controlPoints.Count; i++)
            {
                var nextPoint = controlPoints[i];
                lines.Add(new Line(lastPoint, nextPoint)
                    { Layer = spline.Layer.ToOpenNest() });
                lastPoint = nextPoint;
            }

            if (spline.IsClosed)
                lines.Add(new Line(lastPoint, controlPoints[0])
                    { Layer = spline.Layer.ToOpenNest() });

            return lines;
        }

        public static List<Line> ToOpenNest(this Polyline polyline)
        {
            var lines = new List<Line>();
            var vertices = polyline.Vertices.ToList();

            if (vertices.Count == 0)
                return lines;

            var lastPoint = vertices[0].Location.ToOpenNest();

            for (int i = 1; i < vertices.Count; i++)
            {
                var nextPoint = vertices[i].Location.ToOpenNest();
                lines.Add(new Line(lastPoint, nextPoint)
                    { Layer = polyline.Layer.ToOpenNest() });
                lastPoint = nextPoint;
            }

            if ((polyline.Flags & PolylineFlags.ClosedPolylineOrClosedPolygonMeshInM) != 0)
                lines.Add(new Line(lastPoint, vertices[0].Location.ToOpenNest())
                    { Layer = polyline.Layer.ToOpenNest() });

            return lines;
        }

        public static List<Line> ToOpenNest(this LwPolyline polyline)
        {
            var lines = new List<Line>();
            var vertices = polyline.Vertices.ToList();

            if (vertices.Count == 0)
                return lines;

            var lastPoint = new Vector(vertices[0].Location.X, vertices[0].Location.Y);

            for (int i = 1; i < vertices.Count; i++)
            {
                var nextPoint = new Vector(vertices[i].Location.X, vertices[i].Location.Y);
                lines.Add(new Line(lastPoint, nextPoint)
                    { Layer = polyline.Layer.ToOpenNest() });
                lastPoint = nextPoint;
            }

            if ((polyline.Flags & LwPolylineFlags.Closed) != 0)
                lines.Add(new Line(lastPoint,
                    new Vector(vertices[0].Location.X, vertices[0].Location.Y))
                    { Layer = polyline.Layer.ToOpenNest() });

            return lines;
        }

        public static List<Line> ToOpenNest(this Ellipse ellipse, int precision = 200)
        {
            var lines = new List<Line>();
            var points = new List<Vector>();
            var startParam = ellipse.StartParameter;
            var endParam = ellipse.EndParameter;
            var paramRange = endParam - startParam;
            var radius = System.Math.Sqrt(
                ellipse.EndPoint.X * ellipse.EndPoint.X +
                ellipse.EndPoint.Y * ellipse.EndPoint.Y);

            for (int i = 0; i <= precision; i++)
            {
                var t = i / (double)precision;
                var angle = startParam + paramRange * t;
                var x = ellipse.Center.X + radius * System.Math.Cos(angle);
                var y = ellipse.Center.Y + radius * System.Math.Sin(angle);
                points.Add(new Vector(x, y));
            }

            for (int i = 1; i < points.Count; i++)
            {
                lines.Add(new Line(points[i - 1], points[i])
                    { Layer = ellipse.Layer.ToOpenNest() });
            }

            if (points.Count > 1 &&
                System.Math.Abs(paramRange - 2 * System.Math.PI) < 0.0001)
            {
                lines.Add(new Line(points[points.Count - 1], points[0])
                    { Layer = ellipse.Layer.ToOpenNest() });
            }

            return lines;
        }

        public static Layer ToOpenNest(this ACadSharp.Tables.Layer layer)
        {
            return new Layer(layer.Name)
            {
                Color = System.Drawing.Color.FromArgb(
                    layer.Color.R, layer.Color.G, layer.Color.B),
                IsVisible = layer.IsOn
            };
        }

        public static XY ToAcad(this Vector v)
        {
            return new XY(v.X, v.Y);
        }

        public static XYZ ToAcadXYZ(this Vector v)
        {
            return new XYZ(v.X, v.Y, 0);
        }
    }
}
```

**Step 2: Commit**

```
refactor: rewrite IO Extensions for ACadSharp
```

---

### Task 5: Rewrite DxfImporter.cs for ACadSharp

**Files:**
- Modify: `OpenNest/IO/DxfImporter.cs`

**Step 1: Rewrite DxfImporter.cs**

Key changes:
- `DxfDocument.Load(path)` → `new DxfReader(path).Read()` returning `CadDocument`
- `doc.Entities.Lines` → `doc.Entities.OfType<ACadSharp.Entities.Line>()`
- Same pattern for Arcs, Circles, Splines, etc.
- `doc.Entities.Polylines2D` → `doc.Entities.OfType<LwPolyline>()` + `doc.Entities.OfType<Polyline>()`
- `doc.Entities.Polylines3D` → handled via `Polyline` type

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using ACadSharp;
using ACadSharp.IO;
using OpenNest.Geometry;

namespace OpenNest.IO
{
    public class DxfImporter
    {
        public int SplinePrecision { get; set; }

        public DxfImporter()
        {
        }

        private List<Entity> GetGeometry(CadDocument doc)
        {
            var entities = new List<Entity>();
            var lines = new List<Line>();
            var arcs = new List<Arc>();

            foreach (var entity in doc.Entities)
            {
                switch (entity)
                {
                    case ACadSharp.Entities.Spline spline:
                        lines.AddRange(spline.ToOpenNest(SplinePrecision));
                        break;

                    case ACadSharp.Entities.LwPolyline lwPolyline:
                        lines.AddRange(lwPolyline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Polyline polyline:
                        lines.AddRange(polyline.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Ellipse ellipse:
                        lines.AddRange(ellipse.ToOpenNest(SplinePrecision));
                        break;

                    case ACadSharp.Entities.Line line:
                        lines.Add(line.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Arc arc:
                        arcs.Add(arc.ToOpenNest());
                        break;

                    case ACadSharp.Entities.Circle circle:
                        entities.Add(circle.ToOpenNest());
                        break;
                }
            }

            Helper.Optimize(lines);
            Helper.Optimize(arcs);

            entities.AddRange(lines);
            entities.AddRange(arcs);

            return entities;
        }

        public bool GetGeometry(Stream stream, out List<Entity> geometry)
        {
            bool success = false;

            try
            {
                using var reader = new DxfReader(stream);
                var doc = reader.Read();
                geometry = GetGeometry(doc);
                success = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                geometry = new List<Entity>();
            }

            return success;
        }

        public bool GetGeometry(string path, out List<Entity> geometry)
        {
            bool success = false;

            try
            {
                using var reader = new DxfReader(path);
                var doc = reader.Read();
                geometry = GetGeometry(doc);
                success = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                geometry = new List<Entity>();
            }

            return success;
        }
    }
}
```

**Step 2: Commit**

```
refactor: rewrite DxfImporter for ACadSharp
```

---

### Task 6: Rewrite DxfExporter.cs for ACadSharp

**Files:**
- Modify: `OpenNest/IO/DxfExporter.cs`

**Step 1: Rewrite DxfExporter.cs**

Key changes:
- `new DxfDocument()` → `new CadDocument()`
- `doc.Entities.Add(entity)` → `doc.Entities.Add(entity)` (same pattern)
- `doc.Save(stream)` → `using var writer = new DxfWriter(stream); writer.Write(doc);`
- `new netDxf.Entities.Line(pt1, pt2)` → `new ACadSharp.Entities.Line { StartPoint = xyz1, EndPoint = xyz2 }`
- `new netDxf.Vector2(x, y)` → `new XYZ(x, y, 0)` (ACadSharp Lines use XYZ)
- `AciColor.Red` → `new Color(1)` (ACI color index 1 = red)
- `AciColor.Blue` → `new Color(5)` (ACI color index 5 = blue)
- `AciColor.Cyan` → `new Color(4)` (ACI color index 4 = cyan)
- `Linetype.Dashed` → look up or create dashed linetype
- `Vector2 curpos` → `XYZ curpos` (track as 3D)
- Arc angles: netDxf used degrees, ACadSharp uses radians — remove degree conversions

```csharp
using System;
using System.Diagnostics;
using System.IO;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using CSMath;
using OpenNest.CNC;
using OpenNest.Math;

namespace OpenNest.IO
{
    using AcadLine = ACadSharp.Entities.Line;
    using AcadArc = ACadSharp.Entities.Arc;
    using AcadCircle = ACadSharp.Entities.Circle;

    public class DxfExporter
    {
        private CadDocument doc;
        private XYZ curpos;
        private Mode mode;
        private readonly Layer cutLayer;
        private readonly Layer rapidLayer;
        private readonly Layer plateLayer;

        public DxfExporter()
        {
            doc = new CadDocument();

            cutLayer = new Layer("Cut");
            cutLayer.Color = new Color(1); // Red

            rapidLayer = new Layer("Rapid");
            rapidLayer.Color = new Color(5); // Blue

            plateLayer = new Layer("Plate");
            plateLayer.Color = new Color(4); // Cyan
        }

        public void ExportProgram(Program program, Stream stream)
        {
            doc = new CadDocument();
            EnsureLayers();
            AddProgram(program);

            using var writer = new DxfWriter(stream);
            writer.Write(doc);
        }

        public bool ExportProgram(Program program, string path)
        {
            Stream stream = null;
            bool success = false;

            try
            {
                stream = File.Create(path);
                ExportProgram(program, stream);
                success = true;
            }
            catch
            {
                Debug.Fail("DxfExporter.ExportProgram failed to write program to file: " + path);
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            return success;
        }

        public void ExportPlate(Plate plate, Stream stream)
        {
            doc = new CadDocument();
            EnsureLayers();
            AddPlateOutline(plate);

            foreach (var part in plate.Parts)
            {
                var endpt = part.Location.ToAcadXYZ();
                var line = new AcadLine();
                line.StartPoint = curpos;
                line.EndPoint = endpt;
                line.Layer = rapidLayer;
                doc.Entities.Add(line);
                curpos = endpt;
                AddProgram(part.Program);
            }

            using var writer = new DxfWriter(stream);
            writer.Write(doc);
        }

        public bool ExportPlate(Plate plate, string path)
        {
            Stream stream = null;
            bool success = false;

            try
            {
                stream = File.Create(path);
                ExportPlate(plate, stream);
                success = true;
            }
            catch
            {
                Debug.Fail("DxfExporter.ExportPlate failed to write plate to file: " + path);
            }
            finally
            {
                if (stream != null)
                    stream.Close();
            }

            return success;
        }

        private void EnsureLayers()
        {
            if (!doc.Layers.Contains(cutLayer.Name))
                doc.Layers.Add(cutLayer);
            if (!doc.Layers.Contains(rapidLayer.Name))
                doc.Layers.Add(rapidLayer);
            if (!doc.Layers.Contains(plateLayer.Name))
                doc.Layers.Add(plateLayer);
        }

        private void AddPlateOutline(Plate plate)
        {
            XYZ pt1, pt2, pt3, pt4;

            switch (plate.Quadrant)
            {
                case 1:
                    pt1 = new XYZ(0, 0, 0);
                    pt2 = new XYZ(0, plate.Size.Height, 0);
                    pt3 = new XYZ(plate.Size.Width, plate.Size.Height, 0);
                    pt4 = new XYZ(plate.Size.Width, 0, 0);
                    break;

                case 2:
                    pt1 = new XYZ(0, 0, 0);
                    pt2 = new XYZ(0, plate.Size.Height, 0);
                    pt3 = new XYZ(-plate.Size.Width, plate.Size.Height, 0);
                    pt4 = new XYZ(-plate.Size.Width, 0, 0);
                    break;

                case 3:
                    pt1 = new XYZ(0, 0, 0);
                    pt2 = new XYZ(0, -plate.Size.Height, 0);
                    pt3 = new XYZ(-plate.Size.Width, -plate.Size.Height, 0);
                    pt4 = new XYZ(-plate.Size.Width, 0, 0);
                    break;

                case 4:
                    pt1 = new XYZ(0, 0, 0);
                    pt2 = new XYZ(0, -plate.Size.Height, 0);
                    pt3 = new XYZ(plate.Size.Width, -plate.Size.Height, 0);
                    pt4 = new XYZ(plate.Size.Width, 0, 0);
                    break;

                default:
                    return;
            }

            AddLine(pt1, pt2, plateLayer);
            AddLine(pt2, pt3, plateLayer);
            AddLine(pt3, pt4, plateLayer);
            AddLine(pt4, pt1, plateLayer);

            // Inner margin lines
            var m1 = new XYZ(pt1.X + plate.EdgeSpacing.Left, pt1.Y + plate.EdgeSpacing.Bottom, 0);
            var m2 = new XYZ(m1.X, pt2.Y - plate.EdgeSpacing.Top, 0);
            var m3 = new XYZ(pt3.X - plate.EdgeSpacing.Right, m2.Y, 0);
            var m4 = new XYZ(m3.X, m1.Y, 0);

            AddLine(m1, m2, plateLayer);
            AddLine(m2, m3, plateLayer);
            AddLine(m3, m4, plateLayer);
            AddLine(m4, m1, plateLayer);
        }

        private void AddLine(XYZ start, XYZ end, Layer layer)
        {
            var line = new AcadLine();
            line.StartPoint = start;
            line.EndPoint = end;
            line.Layer = layer;
            doc.Entities.Add(line);
        }

        private void AddProgram(Program program)
        {
            mode = program.Mode;

            for (int i = 0; i < program.Length; ++i)
            {
                var code = program[i];

                switch (code.Type)
                {
                    case CodeType.ArcMove:
                        var arc = (ArcMove)code;
                        AddArcMove(arc);
                        break;

                    case CodeType.LinearMove:
                        var line = (LinearMove)code;
                        AddLinearMove(line);
                        break;

                    case CodeType.RapidMove:
                        var rapid = (RapidMove)code;
                        AddRapidMove(rapid);
                        break;

                    case CodeType.SubProgramCall:
                        var tmpmode = mode;
                        var subpgm = (CNC.SubProgramCall)code;
                        AddProgram(subpgm.Program);
                        mode = tmpmode;
                        break;
                }
            }
        }

        private void AddLinearMove(LinearMove line)
        {
            var pt = line.EndPoint.ToAcadXYZ();

            if (mode == Mode.Incremental)
                pt = new XYZ(pt.X + curpos.X, pt.Y + curpos.Y, 0);

            AddLine(curpos, pt, cutLayer);
            curpos = pt;
        }

        private void AddRapidMove(RapidMove rapid)
        {
            var pt = rapid.EndPoint.ToAcadXYZ();

            if (mode == Mode.Incremental)
                pt = new XYZ(pt.X + curpos.X, pt.Y + curpos.Y, 0);

            AddLine(curpos, pt, rapidLayer);
            curpos = pt;
        }

        private void AddArcMove(ArcMove arc)
        {
            var center = arc.CenterPoint.ToAcadXYZ();
            var endpt = arc.EndPoint.ToAcadXYZ();

            if (mode == Mode.Incremental)
            {
                endpt = new XYZ(endpt.X + curpos.X, endpt.Y + curpos.Y, 0);
                center = new XYZ(center.X + curpos.X, center.Y + curpos.Y, 0);
            }

            // ACadSharp uses radians — no conversion needed
            var startAngle = System.Math.Atan2(
                curpos.Y - center.Y,
                curpos.X - center.X);

            var endAngle = System.Math.Atan2(
                endpt.Y - center.Y,
                endpt.X - center.X);

            if (arc.Rotation == OpenNest.RotationType.CW)
                Generic.Swap(ref startAngle, ref endAngle);

            var dx = endpt.X - center.X;
            var dy = endpt.Y - center.Y;
            var radius = System.Math.Sqrt(dx * dx + dy * dy);

            if (startAngle.IsEqualTo(endAngle))
            {
                var circle = new AcadCircle();
                circle.Center = center;
                circle.Radius = radius;
                circle.Layer = cutLayer;
                doc.Entities.Add(circle);
            }
            else
            {
                var arc2 = new AcadArc();
                arc2.Center = center;
                arc2.Radius = radius;
                arc2.StartAngle = startAngle;
                arc2.EndAngle = endAngle;
                arc2.Layer = cutLayer;
                doc.Entities.Add(arc2);
            }

            curpos = endpt;
        }
    }
}
```

**Step 2: Commit**

```
refactor: rewrite DxfExporter for ACadSharp
```

---

### Task 7: Update MainApp.cs for .NET 8

**Files:**
- Modify: `OpenNest/MainApp.cs`

**Step 1: Simplify MainApp.cs**

.NET 8 WinForms handles DPI awareness via the `ApplicationHighDpiMode` project property. Remove the manual `SetProcessDPIAware()` P/Invoke.

```csharp
using System;
using System.Windows.Forms;
using OpenNest.Forms;

namespace OpenNest
{
    internal static class MainApp
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
```

**Step 2: Commit**

```
refactor: simplify MainApp for .NET 8 DPI handling
```

---

### Task 8: Update solution file and clean up

**Files:**
- Modify: `OpenNest.sln`
- Delete: `packages/` directory (if present on disk)
- Verify: `.gitignore` already ignores `packages/`

**Step 1: Update solution file**

Replace `OpenNest.sln` with a clean version. The project GUIDs stay the same but the solution format is updated:

```
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.0.0
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "OpenNest", "OpenNest\OpenNest.csproj", "{1F1E40E0-5C53-474F-A258-69C9C3FAC15A}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "OpenNest.Core", "OpenNest.Core\OpenNest.Core.csproj", "{5A5FDE8D-F8DB-440E-866C-C4807E1686CF}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "OpenNest.Engine", "OpenNest.Engine\OpenNest.Engine.csproj", "{0083B9CC-54AD-4085-A30D-56BC6834B71A}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{1F1E40E0-5C53-474F-A258-69C9C3FAC15A}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{1F1E40E0-5C53-474F-A258-69C9C3FAC15A}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{1F1E40E0-5C53-474F-A258-69C9C3FAC15A}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{1F1E40E0-5C53-474F-A258-69C9C3FAC15A}.Release|Any CPU.Build.0 = Release|Any CPU
		{5A5FDE8D-F8DB-440E-866C-C4807E1686CF}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{5A5FDE8D-F8DB-440E-866C-C4807E1686CF}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{5A5FDE8D-F8DB-440E-866C-C4807E1686CF}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{5A5FDE8D-F8DB-440E-866C-C4807E1686CF}.Release|Any CPU.Build.0 = Release|Any CPU
		{0083B9CC-54AD-4085-A30D-56BC6834B71A}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{0083B9CC-54AD-4085-A30D-56BC6834B71A}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{0083B9CC-54AD-4085-A30D-56BC6834B71A}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{0083B9CC-54AD-4085-A30D-56BC6834B71A}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
```

**Step 2: Full solution build**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeds with 0 errors.

**Step 3: Fix any remaining build errors**

If there are build errors, fix them. Common issues:
- `System.IO.Compression` — now built into .NET 8, no reference needed
- Missing `using` statements for new namespaces
- API differences in `System.Drawing.Common` on .NET 8
- `LayoutViewGL.cs` exists on disk but was never in the old csproj — SDK-style will auto-include it. Exclude or delete it if it causes errors.

**Step 4: Commit**

```
feat: complete .NET 8 migration — update solution and clean up
```

---

### Task 9: Update CLAUDE.md

**Files:**
- Modify: `CLAUDE.md`

**Step 1: Update build instructions and dependency info**

Update the Build section to reflect `dotnet build` instead of `msbuild`, remove references to `packages/` folder and `nuget restore`, update netDxf references to ACadSharp, and note .NET 8 target.

**Step 2: Commit**

```
docs: update CLAUDE.md for .NET 8 migration
```
