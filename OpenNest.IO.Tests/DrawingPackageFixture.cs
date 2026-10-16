using OpenNest.Bending;
using OpenNest.Converters;
using OpenNest.Geometry;

namespace OpenNest.IO.Tests;

/// <summary>
/// A code-built, neutrally named library drawing: a rounded outside, a hole, a scribe
/// mark, a bend, source entities with one suppressed, and a nonzero source offset.
/// </summary>
internal static class DrawingPackageFixture
{
    public static Drawing Build()
    {
        var entities = new List<Entity>
        {
            new Line(0, 0, 10, 0),
            new Line(10, 0, 10, 4),
            new Arc(8, 4, 2, 0, System.Math.PI / 2),
            new Line(8, 6, 0, 6),
            new Line(0, 6, 0, 0),
            new Circle(3, 3, 1),
            new Line(5, 2, 7, 2) { Layer = SpecialLayers.Scribe },
            new Line(20, 20, 21, 21),
        };
        var drawing = new Drawing("Synthetic bracket", ConvertGeometry.ToProgram(entities.Take(7).ToList()))
        {
            Customer = "Synthetic customer",
            Material = new Material("Synthetic Steel") { Grade = "Grade A", Density = 0.284 },
            Priority = 3,
            SourceEntities = entities,
        };
        drawing.SuppressedEntityIds.Add(entities[7].Id);
        drawing.Source.Path = "synthetic.dxf";
        drawing.Source.Offset = new Vector(3.5, -2.25);
        drawing.Bends.Add(new Bend
        {
            StartPoint = new Vector(0, 3),
            EndPoint = new Vector(10, 3),
            Direction = BendDirection.Up,
            Angle = 90,
            Radius = 0.125,
            NoteText = "UP 90",
        });
        drawing.Quantity.Required = 12;
        return drawing;
    }
}
