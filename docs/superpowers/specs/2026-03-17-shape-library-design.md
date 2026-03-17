# Shape Library Design Spec

## Overview

A parametric shape library for OpenNest that provides reusable, self-describing shape classes for generating `Drawing` objects. Each shape is its own class with typed parameters, inheriting from an abstract `ShapeDefinition` base class. Inspired by PEP's WINSHAPE library.

## Location

- Project: `OpenNest.Core`
- Folder: `Shapes/`
- Namespace: `OpenNest.Shapes`

## Architecture

### Base Class — `ShapeDefinition`

Abstract base class that all shapes inherit from. `Name` defaults to the shape type name (e.g. `"Rectangle"`) but can be overridden.

```csharp
public abstract class ShapeDefinition
{
    public string Name { get; set; }

    protected ShapeDefinition()
    {
        // Default name to the concrete class name, stripping "Shape" suffix
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
```

- `Name`: The name assigned to the resulting `Drawing`. Defaults to the shape class name without the "Shape" suffix. Never null.
- `GetDrawing()`: Each shape implements this to build its geometry and return a `Drawing`.
- `CreateDrawing()`: Shared helper that converts a list of geometry entities into a `Drawing` via `ConvertGeometry.ToProgram()`. Throws `InvalidOperationException` if the geometry is degenerate (prevents null `Program` from reaching `Drawing.UpdateArea()`).

### Shape Classes

#### Tier 1 — Basics (extracted from MCP InputTools)

| Class | Parameters | Description |
|-------|-----------|-------------|
| `RectangleShape` | `Width`, `Height` | Axis-aligned rectangle from origin |
| `CircleShape` | `Diameter` | Circle centered at origin. Implementation divides by 2 for the `Circle` entity's radius parameter. |
| `LShape` | `Width`, `Height`, `LegWidth`?, `LegHeight`? | L-shaped profile. `LegWidth` defaults to `Width/2`, `LegHeight` defaults to `Height/2`. |
| `TShape` | `Width`, `Height`, `StemWidth`?, `BarHeight`? | T-shaped profile. `StemWidth` defaults to `Width/3`, `BarHeight` defaults to `Height/3`. |

#### Tier 2 — Common CNC shapes

| Class | Parameters | Description |
|-------|-----------|-------------|
| `RingShape` | `OuterDiameter`, `InnerDiameter` | Annular ring (two concentric circles). Both converted to radius internally. |
| `RightTriangleShape` | `Width`, `Height` | Right triangle with the right angle at origin |
| `IsoscelesTriangleShape` | `Base`, `Height` | Isosceles triangle centered on base |
| `TrapezoidShape` | `TopWidth`, `BottomWidth`, `Height` | Trapezoid with bottom edge centered under top |
| `OctagonShape` | `Width` | Regular octagon where `Width` is the flat-to-flat distance |
| `RoundedRectangleShape` | `Width`, `Height`, `Radius` | Rectangle with 90-degree CCW arc corners |

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

- **Lines** for straight edges — endpoints must chain end-to-end for `ShapeBuilder` to detect closed shapes.
- **Arcs** for rounded corners (`RoundedRectangleShape`). Arcs use CCW direction (not reversed) with angles in radians.
- **Circles** for `CircleShape` and `RingShape` outer/inner boundaries.

### MCP Integration

`InputTools.CreateDrawing` in `OpenNest.Mcp` will be refactored to instantiate the appropriate `ShapeDefinition` subclass and call `GetDrawing()`, replacing the existing private `CreateRectangle`, `CreateCircle`, `CreateLShape`, `CreateTShape` methods. The MCP tool's existing flat parameter names (`radius`, `width`, `height`) are mapped to the shape class properties at the MCP layer. The `gcode` case remains as-is.

New Tier 2 shapes can be exposed via the MCP tool by extending the `shape` parameter's accepted values and mapping to the new shape classes, with additional MCP parameters as needed.

## Future Expansion

- Additional shapes (Tier 3): Single-D, Parallelogram, House, Stair, Rectangle with chamfer(s), Ring segment, Slot rectangle
- UI shape picker with per-shape parameter editors
- Shape discovery via reflection or static registry
- `LShape`/`TShape` additional sub-dimension parameters for full parametric control
