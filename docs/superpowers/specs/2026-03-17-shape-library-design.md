# Shape Library Design Spec

## Overview

A parametric shape library for OpenNest that provides reusable, self-describing shape classes for generating `Drawing` objects. Each shape is its own class with typed parameters, inheriting from an abstract `ShapeDefinition` base class. Inspired by PEP's WINSHAPE library.

## Location

- Project: `OpenNest.Core`
- Folder: `Shapes/`
- Namespace: `OpenNest.Shapes`

## Architecture

### Base Class — `ShapeDefinition`

Abstract base class that all shapes inherit from.

```csharp
public abstract class ShapeDefinition
{
    public string Name { get; set; }
    public abstract Drawing GetDrawing();

    protected Drawing CreateDrawing(List<Entity> entities)
    {
        var pgm = ConvertGeometry.ToProgram(entities);
        return new Drawing(Name, pgm);
    }
}
```

- `Name`: The name assigned to the resulting `Drawing`.
- `GetDrawing()`: Each shape implements this to build its geometry and return a `Drawing`.
- `CreateDrawing()`: Shared helper that converts a list of geometry entities into a `Drawing` via `ConvertGeometry.ToProgram()`.

### Shape Classes

#### Tier 1 — Basics (extracted from MCP InputTools)

| Class | Parameters | Description |
|-------|-----------|-------------|
| `RectangleShape` | `Width`, `Height` | Axis-aligned rectangle from origin |
| `CircleShape` | `Diameter` | Circle centered at origin |
| `LShape` | `Width`, `Height` | L-shaped profile — full width bottom, half-width top-left |
| `TShape` | `Width`, `Height` | T-shaped profile — full-width top bar, centered stem |

#### Tier 2 — Common CNC shapes

| Class | Parameters | Description |
|-------|-----------|-------------|
| `RingShape` | `OuterDiameter`, `InnerDiameter` | Annular ring (two concentric circles) |
| `RightTriangleShape` | `Width`, `Height` | Right triangle with the right angle at origin |
| `IsoscelesTriangleShape` | `Base`, `Height` | Isosceles triangle centered on base |
| `TrapezoidShape` | `TopWidth`, `BottomWidth`, `Height` | Trapezoid with bottom edge centered under top |
| `OctagonShape` | `Width` | Regular octagon fitting within the given width |
| `RoundedRectangleShape` | `Width`, `Height`, `Radius` | Rectangle with arc corners |

### File Structure

```
OpenNest.Core/
  Shapes/
    ShapeDefinition.cs
    CircleShape.cs
    RectangleShape.cs
    RingShape.cs
    RightTriangleShape.cs
    IsoscelesTriangleShape.cs
    TrapezoidShape.cs
    OctagonShape.cs
    RoundedRectangleShape.cs
    LShape.cs
    TShape.cs
```

### Geometry Construction

Each shape builds a `List<Entity>` (using `Line`, `Arc`, `Circle` from `OpenNest.Geometry`) and passes it to the base `CreateDrawing()` helper. Shapes are constructed at the origin (0,0) with positive X/Y extents.

- **Lines** for straight edges
- **Arcs** for rounded corners (RoundedRectangleShape) and ring segments
- **Circles** for CircleShape and RingShape outer/inner boundaries

### MCP Integration

`InputTools.CreateDrawing` in `OpenNest.Mcp` will be refactored to instantiate the appropriate `ShapeDefinition` subclass and call `GetDrawing()`, replacing the existing private `CreateRectangle`, `CreateCircle`, `CreateLShape`, `CreateTShape` methods.

## Future Expansion

- Additional shapes (Tier 3): Single-D, Parallelogram, House, Stair, Rectangle with chamfer(s), Ring segment, Slot rectangle
- UI shape picker with per-shape parameter editors
- Shape discovery via reflection or static registry
