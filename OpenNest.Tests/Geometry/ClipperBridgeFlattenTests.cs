using System.Collections.Generic;
using System.Linq;
using OpenNest.Benchmark;
using OpenNest.CNC;
using OpenNest.Geometry;
using OpenNest.Math;

namespace OpenNest.Tests.Geometry;

public class ClipperBridgeFlattenTests
{
    private const double Fillet = 0.03125;

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.001)]
    public void Flatten_Circumscribed_StaysWithinStraightEdgesAndTolerance(double tolerance)
    {
        // Circumscribing used to push arc endpoints outward too, so a small corner fillet
        // poked 0.013 past the straight edges it meets.
        var polygon = ClipperBridge.Flatten(FilletedRectangle(), tolerance, circumscribe: true);

        Assert.Equal(0, polygon.BoundingBox.Left, 9);
        Assert.Equal(0, polygon.BoundingBox.Bottom, 9);
        Assert.Equal(2, polygon.BoundingBox.Right, 9);
        Assert.Equal(4, polygon.BoundingBox.Top, 9);

        // Every vertex is on or outside the true outline, by no more than the tolerance.
        var shape = FilletedRectangle();

        foreach (var v in polygon.Vertices)
        {
            var d = shape.Entities.Min(e => e.ClosestPointTo(v).DistanceTo(v));
            Assert.True(d <= tolerance + 1e-9, $"Vertex {v.X},{v.Y} is {d} from the outline.");
        }
    }

    [Theory]
    [InlineData(0.25, true)] // Exactly at the spacing.
    [InlineData(0.2505, true)]
    [InlineData(0.245, false)]
    public void NestValidator_FilletedPartsAtSpacing(double gap, bool valid)
    {
        var drawing = new Drawing("filleted", FilletedRectangleProgram());
        var plate = new Plate(100, 100) { PartSpacing = 0.25 };

        // Off-grid locations, so Clipper's 1e-4 rounding cannot line things up exactly.
        var a = new Part(drawing) { Location = new Vector(10.123456, 10.654321) };
        var b = new Part(drawing) { Location = new Vector(10.123456 + 2 + gap, 10.654321) };

        var result = NestValidator.Validate(
            new List<(Plate, List<Part>)> { (plate, new List<Part> { a, b }) },
            new Dictionary<Drawing, (string, int)> { [drawing] = ("filleted", 2) }
        );

        Assert.True(valid == result.Valid, string.Join("; ", result.Violations));
    }

    /// <summary>2 x 4 rectangle with 0.03125 corner fillets, CCW from the origin.</summary>
    private static Shape FilletedRectangle()
    {
        var f = Fillet;
        var shape = new Shape();
        shape.Entities.Add(new Line(f, 0, 2 - f, 0));
        shape.Entities.Add(new Arc(2 - f, f, f, -Angle.HalfPI, 0));
        shape.Entities.Add(new Line(2, f, 2, 4 - f));
        shape.Entities.Add(new Arc(2 - f, 4 - f, f, 0, Angle.HalfPI));
        shape.Entities.Add(new Line(2 - f, 4, f, 4));
        shape.Entities.Add(new Arc(f, 4 - f, f, Angle.HalfPI, System.Math.PI));
        shape.Entities.Add(new Line(0, 4 - f, 0, f));
        shape.Entities.Add(new Arc(f, f, f, System.Math.PI, 3 * Angle.HalfPI));
        return shape;
    }

    private static Program FilletedRectangleProgram()
    {
        var f = Fillet;
        var pgm = new Program();
        pgm.Codes.Add(new RapidMove(new Vector(f, 0)));
        pgm.Codes.Add(new LinearMove(new Vector(2 - f, 0)));
        pgm.Codes.Add(new ArcMove(new Vector(2, f), new Vector(2 - f, f)));
        pgm.Codes.Add(new LinearMove(new Vector(2, 4 - f)));
        pgm.Codes.Add(new ArcMove(new Vector(2 - f, 4), new Vector(2 - f, 4 - f)));
        pgm.Codes.Add(new LinearMove(new Vector(f, 4)));
        pgm.Codes.Add(new ArcMove(new Vector(0, 4 - f), new Vector(f, 4 - f)));
        pgm.Codes.Add(new LinearMove(new Vector(0, f)));
        pgm.Codes.Add(new ArcMove(new Vector(f, 0), new Vector(f, f)));
        return pgm;
    }
}
