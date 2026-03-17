# Shape Library Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create a parametric shape library in OpenNest.Core with an abstract `ShapeDefinition` base class and 10 concrete shape classes that each produce a `Drawing`.

**Architecture:** Each shape is a class in `OpenNest.Core/Shapes/` (namespace `OpenNest.Shapes`) inheriting from `ShapeDefinition`. The base class provides a `CreateDrawing(List<Entity>)` helper that converts geometry to a `Drawing` via `ConvertGeometry.ToProgram()`. Each subclass implements `GetDrawing()` by building a `List<Entity>` from `Line`, `Arc`, and `Circle` primitives.

**Tech Stack:** .NET 8, xUnit, OpenNest.Geometry (Line, Arc, Circle, Vector), OpenNest.Converters (ConvertGeometry)

**Spec:** `docs/superpowers/specs/2026-03-17-shape-library-design.md`

---

## Chunk 1: Base Class + Simple Shapes (Rectangle, Circle, Ring)

### Task 1: ShapeDefinition Base Class

**Files:**
- Create: `OpenNest.Core/Shapes/ShapeDefinition.cs`

- [ ] **Step 1: Create ShapeDefinition.cs**

```csharp
using System;
using System.Collections.Generic;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public abstract class ShapeDefinition
    {
        public string Name { get; set; }

        protected ShapeDefinition()
        {
            var typeName = GetType().Name;
            Name = typeName.EndsWith("Shape")
                ? typeName.Substring(0, typeName.Length - 5)
                : typeName;
        }

        public abstract Drawing GetDrawing();

        protected Drawing CreateDrawing(List<Entity> entities)
        {
            var pgm = ConvertGeometry.ToProgram(entities);

            if (pgm == null)
                throw new InvalidOperationException(
                    $"Failed to create program for shape '{Name}'. Check that parameters produce valid geometry.");

            return new Drawing(Name, pgm);
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build OpenNest.Core/OpenNest.Core.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```
feat(core): add ShapeDefinition base class
```

---

### Task 2: RectangleShape

**Files:**
- Create: `OpenNest.Core/Shapes/RectangleShape.cs`
- Create: `OpenNest.Tests/Shapes/RectangleShapeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class RectangleShapeTests
{
    [Fact]
    public void GetDrawing_ReturnsDrawingWithCorrectBoundingBox()
    {
        var shape = new RectangleShape { Width = 10, Height = 5 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Width, 0.01);
        Assert.Equal(5, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsRectangle()
    {
        var shape = new RectangleShape { Width = 10, Height = 5 };
        var drawing = shape.GetDrawing();

        Assert.Equal("Rectangle", drawing.Name);
    }

    [Fact]
    public void GetDrawing_CustomName_IsUsed()
    {
        var shape = new RectangleShape { Name = "Plate1", Width = 10, Height = 5 };
        var drawing = shape.GetDrawing();

        Assert.Equal("Plate1", drawing.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~RectangleShapeTests"`
Expected: FAIL — `RectangleShape` does not exist

- [ ] **Step 3: Implement RectangleShape**

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class RectangleShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Line(0, 0, Width, 0),
                new Line(Width, 0, Width, Height),
                new Line(Width, Height, 0, Height),
                new Line(0, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~RectangleShapeTests"`
Expected: 3 passed

- [ ] **Step 5: Commit**

```
feat(core): add RectangleShape
```

---

### Task 3: CircleShape

**Files:**
- Create: `OpenNest.Core/Shapes/CircleShape.cs`
- Create: `OpenNest.Tests/Shapes/CircleShapeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class CircleShapeTests
{
    [Fact]
    public void GetDrawing_ReturnsDrawingWithCorrectBoundingBox()
    {
        var shape = new CircleShape { Diameter = 10 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Width, 0.01);
        Assert.Equal(10, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsCircle()
    {
        var shape = new CircleShape { Diameter = 10 };
        var drawing = shape.GetDrawing();

        Assert.Equal("Circle", drawing.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~CircleShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement CircleShape**

The `Circle` entity constructor takes `(x, y, radius)`. `Diameter` is the user-facing parameter, so divide by 2.

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class CircleShape : ShapeDefinition
    {
        public double Diameter { get; set; }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Circle(0, 0, Diameter / 2.0)
            };

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~CircleShapeTests"`
Expected: 2 passed

- [ ] **Step 5: Commit**

```
feat(core): add CircleShape
```

---

### Task 4: RingShape

**Files:**
- Create: `OpenNest.Core/Shapes/RingShape.cs`
- Create: `OpenNest.Tests/Shapes/RingShapeTests.cs`

- [ ] **Step 1: Write failing tests**

The ring is two concentric circles. `ConvertGeometry.ToProgram()` identifies the larger circle as the perimeter and the smaller as a cutout. The bounding box should match the outer diameter.

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class RingShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesOuterDiameter()
    {
        var shape = new RingShape { OuterDiameter = 20, InnerDiameter = 10 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(20, bbox.Width, 0.01);
        Assert.Equal(20, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaExcludesInnerHole()
    {
        var shape = new RingShape { OuterDiameter = 20, InnerDiameter = 10 };
        var drawing = shape.GetDrawing();

        // Area = pi * (10^2 - 5^2) = pi * 75
        var expectedArea = System.Math.PI * 75;
        Assert.Equal(expectedArea, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_DefaultName_IsRing()
    {
        var shape = new RingShape { OuterDiameter = 20, InnerDiameter = 10 };
        var drawing = shape.GetDrawing();

        Assert.Equal("Ring", drawing.Name);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~RingShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement RingShape**

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class RingShape : ShapeDefinition
    {
        public double OuterDiameter { get; set; }
        public double InnerDiameter { get; set; }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Circle(0, 0, OuterDiameter / 2.0),
                new Circle(0, 0, InnerDiameter / 2.0)
            };

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~RingShapeTests"`
Expected: 3 passed

- [ ] **Step 5: Commit**

```
feat(core): add RingShape
```

---

## Chunk 2: Triangle Shapes + Trapezoid + Octagon

### Task 5: RightTriangleShape

**Files:**
- Create: `OpenNest.Core/Shapes/RightTriangleShape.cs`
- Create: `OpenNest.Tests/Shapes/RightTriangleShapeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class RightTriangleShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new RightTriangleShape { Width = 12, Height = 8 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(12, bbox.Width, 0.01);
        Assert.Equal(8, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaIsHalfWidthTimesHeight()
    {
        var shape = new RightTriangleShape { Width = 12, Height = 8 };
        var drawing = shape.GetDrawing();

        Assert.Equal(48, drawing.Area, 0.5);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~RightTriangleShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement RightTriangleShape**

Right angle at origin (0,0). Vertices: (0,0), (Width,0), (0,Height).

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class RightTriangleShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }

        public override Drawing GetDrawing()
        {
            var entities = new List<Entity>
            {
                new Line(0, 0, Width, 0),
                new Line(Width, 0, 0, Height),
                new Line(0, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~RightTriangleShapeTests"`
Expected: 2 passed

- [ ] **Step 5: Commit**

```
feat(core): add RightTriangleShape
```

---

### Task 6: IsoscelesTriangleShape

**Files:**
- Create: `OpenNest.Core/Shapes/IsoscelesTriangleShape.cs`
- Create: `OpenNest.Tests/Shapes/IsoscelesTriangleShapeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class IsoscelesTriangleShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new IsoscelesTriangleShape { Base = 10, Height = 8 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Width, 0.01);
        Assert.Equal(8, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaIsHalfBaseTimesHeight()
    {
        var shape = new IsoscelesTriangleShape { Base = 10, Height = 8 };
        var drawing = shape.GetDrawing();

        Assert.Equal(40, drawing.Area, 0.5);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~IsoscelesTriangleShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement IsoscelesTriangleShape**

Base along the bottom from (0,0) to (Base,0). Apex at (Base/2, Height).

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class IsoscelesTriangleShape : ShapeDefinition
    {
        public double Base { get; set; }
        public double Height { get; set; }

        public override Drawing GetDrawing()
        {
            var midX = Base / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, Base, 0),
                new Line(Base, 0, midX, Height),
                new Line(midX, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~IsoscelesTriangleShapeTests"`
Expected: 2 passed

- [ ] **Step 5: Commit**

```
feat(core): add IsoscelesTriangleShape
```

---

### Task 7: TrapezoidShape

**Files:**
- Create: `OpenNest.Core/Shapes/TrapezoidShape.cs`
- Create: `OpenNest.Tests/Shapes/TrapezoidShapeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class TrapezoidShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new TrapezoidShape { BottomWidth = 20, TopWidth = 10, Height = 8 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(20, bbox.Width, 0.01);
        Assert.Equal(8, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_AreaIsCorrect()
    {
        var shape = new TrapezoidShape { BottomWidth = 20, TopWidth = 10, Height = 8 };
        var drawing = shape.GetDrawing();

        // Area = (top + bottom) / 2 * height = (10 + 20) / 2 * 8 = 120
        Assert.Equal(120, drawing.Area, 0.5);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~TrapezoidShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement TrapezoidShape**

Bottom edge from (0,0) to (BottomWidth,0). Top edge centered above, offset by `(BottomWidth - TopWidth) / 2`.

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class TrapezoidShape : ShapeDefinition
    {
        public double TopWidth { get; set; }
        public double BottomWidth { get; set; }
        public double Height { get; set; }

        public override Drawing GetDrawing()
        {
            var offset = (BottomWidth - TopWidth) / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, BottomWidth, 0),
                new Line(BottomWidth, 0, offset + TopWidth, Height),
                new Line(offset + TopWidth, Height, offset, Height),
                new Line(offset, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~TrapezoidShapeTests"`
Expected: 2 passed

- [ ] **Step 5: Commit**

```
feat(core): add TrapezoidShape
```

---

### Task 8: OctagonShape

**Files:**
- Create: `OpenNest.Core/Shapes/OctagonShape.cs`
- Create: `OpenNest.Tests/Shapes/OctagonShapeTests.cs`

- [ ] **Step 1: Write failing tests**

`Width` is the flat-to-flat distance. The octagon is centered at (Width/2, Width/2) so all geometry is in positive X/Y.

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class OctagonShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxFitsWithinExpectedSize()
    {
        var shape = new OctagonShape { Width = 20 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        // Corner-to-corner is larger than flat-to-flat
        Assert.True(bbox.Width >= 20 - 0.01);
        Assert.True(bbox.Length >= 20 - 0.01);
        // But should not be wildly larger (corner-to-corner ~ width / cos(22.5°) ≈ width * 1.0824)
        Assert.True(bbox.Width < 22);
        Assert.True(bbox.Length < 22);
    }

    [Fact]
    public void GetDrawing_HasEightEdges()
    {
        var shape = new OctagonShape { Width = 20 };
        var drawing = shape.GetDrawing();

        // An octagon program should have 8 linear moves (one per edge)
        var moves = drawing.Program.Codes
            .OfType<OpenNest.CNC.LinearMove>()
            .Count();
        Assert.Equal(8, moves);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~OctagonShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement OctagonShape**

Regular octagon with flat-to-flat distance = `Width`. The circumscribed radius (center to corner) = `Width / (2 * cos(π/8))`. Generate 8 vertices at 45° intervals, starting at 22.5° so flats are horizontal/vertical. Center at `(Width/2, Width/2)` to keep geometry in positive quadrant.

```csharp
using System;
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class OctagonShape : ShapeDefinition
    {
        public double Width { get; set; }

        public override Drawing GetDrawing()
        {
            var center = Width / 2.0;
            var circumRadius = Width / (2.0 * System.Math.Cos(System.Math.PI / 8.0));

            var vertices = new Vector[8];
            for (var i = 0; i < 8; i++)
            {
                var angle = System.Math.PI / 8.0 + i * System.Math.PI / 4.0;
                vertices[i] = new Vector(
                    center + circumRadius * System.Math.Cos(angle),
                    center + circumRadius * System.Math.Sin(angle));
            }

            var entities = new List<Entity>();
            for (var i = 0; i < 8; i++)
            {
                var next = (i + 1) % 8;
                entities.Add(new Line(vertices[i], vertices[next]));
            }

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~OctagonShapeTests"`
Expected: 2 passed

- [ ] **Step 5: Commit**

```
feat(core): add OctagonShape
```

---

## Chunk 3: Compound Shapes (L, T, Rounded Rectangle)

### Task 9: LShape

**Files:**
- Create: `OpenNest.Core/Shapes/LShape.cs`
- Create: `OpenNest.Tests/Shapes/LShapeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class LShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new LShape { Width = 10, Height = 20 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(10, bbox.Width, 0.01);
        Assert.Equal(20, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_DefaultLegDimensions()
    {
        var shape = new LShape { Width = 10, Height = 20 };
        var drawing = shape.GetDrawing();

        // Default legs: LegWidth = Width/2 = 5, LegHeight = Height/2 = 10
        // Area = Width*Height - (Width - LegWidth) * (Height - LegHeight)
        // Area = 10*20 - 5*10 = 150
        Assert.Equal(150, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_CustomLegDimensions()
    {
        var shape = new LShape { Width = 10, Height = 20, LegWidth = 3, LegHeight = 5 };
        var drawing = shape.GetDrawing();

        // Area = Width*Height - (Width - LegWidth) * (Height - LegHeight)
        // Area = 10*20 - 7*15 = 200 - 105 = 95
        Assert.Equal(95, drawing.Area, 0.5);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~LShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement LShape**

L-shape: full width along the bottom, a vertical leg on the left side. `LegWidth` controls how wide the vertical leg is. `LegHeight` controls how tall the horizontal base portion is. When `LegWidth`/`LegHeight` are 0, they default to half the overall dimension.

```
    ┌──────┐ LegWidth
    │      │
    │      │ Height
    │      │
    │      ├──────────┐
    │      │          │ LegHeight
    └──────┴──────────┘
           Width
```

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class LShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double LegWidth { get; set; }
        public double LegHeight { get; set; }

        public override Drawing GetDrawing()
        {
            var lw = LegWidth > 0 ? LegWidth : Width / 2.0;
            var lh = LegHeight > 0 ? LegHeight : Height / 2.0;

            var entities = new List<Entity>
            {
                new Line(0, 0, Width, 0),
                new Line(Width, 0, Width, lh),
                new Line(Width, lh, lw, lh),
                new Line(lw, lh, lw, Height),
                new Line(lw, Height, 0, Height),
                new Line(0, Height, 0, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~LShapeTests"`
Expected: 3 passed

- [ ] **Step 5: Commit**

```
feat(core): add LShape
```

---

### Task 10: TShape

**Files:**
- Create: `OpenNest.Core/Shapes/TShape.cs`
- Create: `OpenNest.Tests/Shapes/TShapeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class TShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new TShape { Width = 12, Height = 18 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(12, bbox.Width, 0.01);
        Assert.Equal(18, bbox.Length, 0.01);
    }

    [Fact]
    public void GetDrawing_DefaultStemAndBarDimensions()
    {
        var shape = new TShape { Width = 12, Height = 18 };
        var drawing = shape.GetDrawing();

        // Default: StemWidth = Width/3 = 4, BarHeight = Height/3 = 6
        // Area = Width * BarHeight + StemWidth * (Height - BarHeight)
        // Area = 12 * 6 + 4 * 12 = 72 + 48 = 120
        Assert.Equal(120, drawing.Area, 0.5);
    }

    [Fact]
    public void GetDrawing_CustomStemAndBarDimensions()
    {
        var shape = new TShape { Width = 12, Height = 18, StemWidth = 6, BarHeight = 4 };
        var drawing = shape.GetDrawing();

        // Area = Width * BarHeight + StemWidth * (Height - BarHeight)
        // Area = 12 * 4 + 6 * 14 = 48 + 84 = 132
        Assert.Equal(132, drawing.Area, 0.5);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~TShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement TShape**

T-shape: full-width top bar, centered stem below. The T is oriented with the bar at the top.

```
    ┌──────────────────┐
    │    Bar (Width)    │ BarHeight
    └───┬────────┬─────┘
        │  Stem  │  Height - BarHeight
        │        │
        └────────┘
        StemWidth
```

```csharp
using System.Collections.Generic;
using OpenNest.Geometry;

namespace OpenNest.Shapes
{
    public class TShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double StemWidth { get; set; }
        public double BarHeight { get; set; }

        public override Drawing GetDrawing()
        {
            var sw = StemWidth > 0 ? StemWidth : Width / 3.0;
            var bh = BarHeight > 0 ? BarHeight : Height / 3.0;
            var stemLeft = (Width - sw) / 2.0;
            var stemRight = stemLeft + sw;
            var stemTop = Height - bh;

            var entities = new List<Entity>
            {
                new Line(stemLeft, 0, stemRight, 0),
                new Line(stemRight, 0, stemRight, stemTop),
                new Line(stemRight, stemTop, Width, stemTop),
                new Line(Width, stemTop, Width, Height),
                new Line(Width, Height, 0, Height),
                new Line(0, Height, 0, stemTop),
                new Line(0, stemTop, stemLeft, stemTop),
                new Line(stemLeft, stemTop, stemLeft, 0)
            };

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~TShapeTests"`
Expected: 3 passed

- [ ] **Step 5: Commit**

```
feat(core): add TShape
```

---

### Task 11: RoundedRectangleShape

**Files:**
- Create: `OpenNest.Core/Shapes/RoundedRectangleShape.cs`
- Create: `OpenNest.Tests/Shapes/RoundedRectangleShapeTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
using OpenNest.Shapes;

namespace OpenNest.Tests.Shapes;

public class RoundedRectangleShapeTests
{
    [Fact]
    public void GetDrawing_BoundingBoxMatchesDimensions()
    {
        var shape = new RoundedRectangleShape { Width = 20, Height = 10, Radius = 2 };
        var drawing = shape.GetDrawing();

        var bbox = drawing.Program.BoundingBox();
        Assert.Equal(20, bbox.Width, 0.1);
        Assert.Equal(10, bbox.Length, 0.1);
    }

    [Fact]
    public void GetDrawing_AreaIsLessThanFullRectangle()
    {
        var shape = new RoundedRectangleShape { Width = 20, Height = 10, Radius = 2 };
        var drawing = shape.GetDrawing();

        // Area should be less than 20*10=200 because corners are rounded
        // Area = W*H - (4 - pi) * r^2 = 200 - (4 - pi) * 4 ≈ 196.57
        Assert.True(drawing.Area < 200);
        Assert.True(drawing.Area > 190);
    }

    [Fact]
    public void GetDrawing_ZeroRadius_MatchesRectangleArea()
    {
        var shape = new RoundedRectangleShape { Width = 20, Height = 10, Radius = 0 };
        var drawing = shape.GetDrawing();

        Assert.Equal(200, drawing.Area, 0.5);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~RoundedRectangleShapeTests"`
Expected: FAIL

- [ ] **Step 3: Implement RoundedRectangleShape**

Four straight edges with 90° arcs at each corner. Arc constructor: `Arc(center, radius, startAngle, endAngle, reversed)`. Angles are in radians. Arcs go CCW (not reversed). The shape chains: bottom edge → bottom-right arc → right edge → top-right arc → top edge → top-left arc → left edge → bottom-left arc.

When `Radius` is 0, skip arcs and produce a plain rectangle (all lines).

```csharp
using System;
using System.Collections.Generic;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Shapes
{
    public class RoundedRectangleShape : ShapeDefinition
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Radius { get; set; }

        public override Drawing GetDrawing()
        {
            var r = Radius;
            var entities = new List<Entity>();

            if (r <= 0)
            {
                entities.Add(new Line(0, 0, Width, 0));
                entities.Add(new Line(Width, 0, Width, Height));
                entities.Add(new Line(Width, Height, 0, Height));
                entities.Add(new Line(0, Height, 0, 0));
            }
            else
            {
                // Bottom edge (left to right, above bottom-left arc to bottom-right arc)
                entities.Add(new Line(r, 0, Width - r, 0));

                // Bottom-right corner arc: center at (Width-r, r), from -90° to 0°
                entities.Add(new Arc(Width - r, r, r,
                    Angle.ToRadians(270), Angle.ToRadians(360)));

                // Right edge
                entities.Add(new Line(Width, r, Width, Height - r));

                // Top-right corner arc: center at (Width-r, Height-r), from 0° to 90°
                entities.Add(new Arc(Width - r, Height - r, r,
                    Angle.ToRadians(0), Angle.ToRadians(90)));

                // Top edge (right to left)
                entities.Add(new Line(Width - r, Height, r, Height));

                // Top-left corner arc: center at (r, Height-r), from 90° to 180°
                entities.Add(new Arc(r, Height - r, r,
                    Angle.ToRadians(90), Angle.ToRadians(180)));

                // Left edge
                entities.Add(new Line(0, Height - r, 0, r));

                // Bottom-left corner arc: center at (r, r), from 180° to 270°
                entities.Add(new Arc(r, r, r,
                    Angle.ToRadians(180), Angle.ToRadians(270)));
            }

            return CreateDrawing(entities);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj --filter "FullyQualifiedName~RoundedRectangleShapeTests"`
Expected: 3 passed

- [ ] **Step 5: Commit**

```
feat(core): add RoundedRectangleShape
```

---

## Chunk 4: MCP Refactoring + Final Build Verification

### Task 12: Refactor MCP InputTools to Use Shape Library

**Files:**
- Modify: `OpenNest.Mcp/Tools/InputTools.cs`

- [ ] **Step 1: Replace private shape methods with shape library calls**

Replace the body of the `CreateDrawing` MCP tool method and remove the four private methods (`CreateRectangle`, `CreateCircle`, `CreateLShape`, `CreateTShape`). The MCP tool maps its flat parameters to shape class properties.

In `InputTools.cs`, replace lines 100-203 (from `switch (shape.ToLower())` through the end of `CreateTShape`) with:

```csharp
// Inside CreateDrawing method, replace the switch block:
ShapeDefinition shapeDef;

switch (shape.ToLower())
{
    case "rectangle":
        shapeDef = new RectangleShape { Name = name, Width = width, Height = height };
        break;

    case "circle":
        shapeDef = new CircleShape { Name = name, Diameter = radius * 2 };
        break;

    case "l_shape":
        shapeDef = new LShape { Name = name, Width = width, Height = height };
        break;

    case "t_shape":
        shapeDef = new TShape { Name = name, Width = width, Height = height };
        break;

    case "gcode":
        if (string.IsNullOrWhiteSpace(gcode))
            return "Error: gcode parameter is required when shape is 'gcode'";
        var pgm = ParseGcode(gcode);
        if (pgm == null)
            return "Error: failed to parse G-code";
        var gcodeDrawing = new Drawing(name, pgm);
        _session.Drawings.Add(gcodeDrawing);
        var gcodeBbox = pgm.BoundingBox();
        return $"Created drawing '{name}': bbox={gcodeBbox.Width:F2} x {gcodeBbox.Length:F2}";

    default:
        return $"Error: unknown shape '{shape}'. Use: rectangle, circle, l_shape, t_shape, gcode";
}

var drawing = shapeDef.GetDrawing();
_session.Drawings.Add(drawing);

var bbox = drawing.Program.BoundingBox();
return $"Created drawing '{name}': bbox={bbox.Width:F2} x {bbox.Length:F2}";
```

Remove the four private methods: `CreateRectangle`, `CreateCircle`, `CreateLShape`, `CreateTShape`. Keep `ParseGcode`.

Add `using OpenNest.Shapes;` to the top of the file. Remove `using OpenNest.Converters;` and `using OpenNest.Geometry;` if no longer used (check — `ParseGcode` uses `OpenNest.IO` types only, and the `gcode` case creates a `Drawing` directly).

- [ ] **Step 2: Build the MCP project**

Run: `dotnet build OpenNest.Mcp/OpenNest.Mcp.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```
refactor(mcp): use shape library in InputTools
```

---

### Task 13: Full Solution Build + All Tests

- [ ] **Step 1: Build entire solution**

Run: `dotnet build OpenNest.sln`
Expected: Build succeeded

- [ ] **Step 2: Run all tests**

Run: `dotnet test OpenNest.Tests/OpenNest.Tests.csproj`
Expected: All tests pass (existing + new shape tests)

- [ ] **Step 3: Commit if any fixups were needed**

```
fix(core): address build/test issues in shape library
```
